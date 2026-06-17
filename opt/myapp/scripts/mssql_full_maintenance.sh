#!/bin/bash
#
# MSSQL-Style Full Backup Maintenance Plan
# ==========================================
# Iterates all tenant directories under TENANT_ROOT, creates compressed
# tarballs, verifies integrity, and rotates to keep only the last N
# successful archives per tenant.
#
# Designed for low-priority systemd execution (Nice=19, IO idle).
# Logs are written to LOG_FILE for audit trail.
#
set -o errexit
set -o nounset
set -o pipefail

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------
TENANT_ROOT="${HOME:-/home/vboxuser}/myapp/tenants"
VAULT_ROOT="${HOME:-/home/vboxuser}/myapp/vault"
RETENTION_COUNT="${RETENTION_COUNT:-7}"
LOG_FILE="${HOME:-/home/vboxuser}/myapp/log/maintenance.log"
LOCK_FILE="/tmp/.mssql_full_maintenance.lock"

# ---------------------------------------------------------------------------
# Logging helpers  (ISO-8601 timestamps)
# ---------------------------------------------------------------------------
log_info()  { echo "$(date -Iseconds)  INFO   $*" >> "$LOG_FILE"; }
log_error() { echo "$(date -Iseconds)  ERROR  $*" >> "$LOG_FILE"; }
log_warn()  { echo "$(date -Iseconds)  WARN   $*" >> "$LOG_FILE"; }

# ---------------------------------------------------------------------------
# Lock – prevent concurrent runs (e.g. if a run overruns into the next timer)
# ---------------------------------------------------------------------------
if ! mkdir "$LOCK_FILE" 2>/dev/null; then
    log_warn "Lock $LOCK_FILE held – another instance is running. Exiting."
    exit 1
fi
trap 'rm -rf "$LOCK_FILE"' EXIT

mkdir -p "$(dirname "$LOG_FILE")"
touch "$LOG_FILE"

log_info "=== MSSQL Full Maintenance started (retention=$RETENTION_COUNT) ==="

# ---------------------------------------------------------------------------
# Discover tenants
# ---------------------------------------------------------------------------
tenant_dirs=("$TENANT_ROOT"/*/)
if [[ ${#tenant_dirs[@]} -eq 0 ]] || [[ ! -d "${tenant_dirs[0]}" ]]; then
    log_info "No tenant directories found under $TENANT_ROOT. Exiting."
    exit 0
fi

overall_exit=0

for tenant_path in "${tenant_dirs[@]}"; do
    tenant_id=$(basename "$tenant_path")
    backup_src="$tenant_path/backups"
    vault_dir="$VAULT_ROOT/$tenant_id"

    # --- skip if no source data to archive --------------------------------
    if [[ ! -d "$backup_src" ]]; then
        log_info "[$tenant_id] No backups directory – skipping."
        continue
    fi
    if ! find "$backup_src" -mindepth 1 -maxdepth 1 -print -quit | grep -q .; then
        log_info "[$tenant_id] Backups directory empty – skipping."
        continue
    fi

    mkdir -p "$vault_dir"

    # --- Step 1: create archive -------------------------------------------
    timestamp=$(date -u +%Y%m%d_%H%M%S)
    archive_name="FULL_${tenant_id}_${timestamp}.tar.gz"
    archive_path="$vault_dir/$archive_name"
    tmp_archive="${archive_path}.partial"

    log_info "[$tenant_id] Creating archive: $archive_name"

    # tar to a .partial file so we never leave a half-baked archive
    if tar -czf "$tmp_archive" -C "$backup_src" . 2>>"$LOG_FILE"; then
        :
    else
        log_error "[$tenant_id] tar failed for $backup_src – removing partial."
        rm -f "$tmp_archive"
        overall_exit=1
        continue
    fi

    # --- Step 2: integrity verification (MSSQL-style checksum) ------------
    log_info "[$tenant_id] Verifying archive integrity: $archive_name"
    if tar -tzf "$tmp_archive" >/dev/null 2>&1; then
        # Verification OK – promote partial to final
        mv "$tmp_archive" "$archive_path"
        archive_size=$(stat -c%s "$archive_path" 2>/dev/null || echo "unknown")
        log_info "[$tenant_id] Archive verified OK – saved ($archive_size bytes)"
    else
        log_error "[$tenant_id] INTEGRITY FAILURE – archive corrupt. Deleting partial."
        rm -f "$tmp_archive"
        log_error "[$tenant_id] Retention preserved – no existing archives deleted."
        overall_exit=1
        continue
    fi

    # --- Step 3: atomic count-based retention (safety-first) --------------
    # List all FULL_ archives sorted by name (timestamp order), keep newest N
    mapfile -t archives < <(find "$vault_dir" -maxdepth 1 -type f -name "FULL_${tenant_id}_*.tar.gz" -printf '%f\n' | sort -r)

    if [[ ${#archives[@]} -gt $RETENTION_COUNT ]]; then
        delete_count=$(( ${#archives[@]} - RETENTION_COUNT ))
        log_info "[$tenant_id] Retention limit=$RETENTION_COUNT, found ${#archives[@]} – removing $delete_count oldest."

        for (( i = RETENTION_COUNT; i < ${#archives[@]}; i++ )); do
            rm -f "$vault_dir/${archives[$i]}"
            log_info "[$tenant_id] Deleted (rotation): ${archives[$i]}"
        done
    else
        log_info "[$tenant_id] Archives=${#archives[@]} within retention limit=$RETENTION_COUNT – no rotation."
    fi

done

log_info "=== MSSQL Full Maintenance finished (exit=$overall_exit) ==="
exit $overall_exit
