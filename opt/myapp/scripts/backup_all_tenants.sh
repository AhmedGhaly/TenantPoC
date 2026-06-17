#!/bin/bash
# Creates database backups for all tenants using docker exec
# Supports: PostgreSQL (pg_dump), SQL Server (sqlcmd), MongoDB (mongodump)
set -o errexit
set -o pipefail

TENANT_ROOT="${HOME:-/home/vboxuser}/myapp/tenants"
LOG_FILE="${HOME:-/home/vboxuser}/myapp/log/maintenance.log"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
if [[ -f "$SCRIPT_DIR/../../../tenants.json" ]]; then
    TENANTS_JSON="$(cd "$SCRIPT_DIR/../../../" && pwd)/tenants.json"
else
    TENANTS_JSON="/home/vboxuser/TenantPoC/tenants.json"
fi

mkdir -p "$(dirname "$LOG_FILE")"
touch "$LOG_FILE"

log_info()  { echo "$(date -Iseconds)  INFO   $*" >> "$LOG_FILE"; }
log_warn()  { echo "$(date -Iseconds)  WARN   $*" >> "$LOG_FILE"; }
log_error() { echo "$(date -Iseconds)  ERROR  $*" >> "$LOG_FILE"; }

if [[ ! -f "$TENANTS_JSON" ]]; then
    log_error "tenants.json not found at $TENANTS_JSON"
    exit 1
fi

log_info "=== Backup All Tenants started ==="

count=$(jq length "$TENANTS_JSON")
for (( i = 0; i < count; i++ )); do
    tenant=$(jq ".[$i]" "$TENANTS_JSON")
    id=$(jq -r '.Id' <<< "$tenant")
    name=$(jq -r '.Name' <<< "$tenant")
    provider=$(jq -r '.DatabaseInfo.Provider' <<< "$tenant")
    container_name=$(jq -r '.DatabaseInfo.ContainerName' <<< "$tenant")
    container_id=$(jq -r '.DatabaseInfo.ContainerId // ""' <<< "$tenant")
    password=$(jq -r '.DatabaseInfo.Password' <<< "$tenant")
    port=$(jq -r '.DatabaseInfo.Port' <<< "$tenant")

    backup_dir="$TENANT_ROOT/$id/backups"
    mkdir -p "$backup_dir"
    timestamp=$(date -u +%Y%m%d_%H%M%S)

    # Use container ID if present, fall back to name
    cid="${container_id:-$container_name}"
    if [[ -z "$cid" ]]; then
        log_error "[$name] No container ID/name found – skipping."
        continue
    fi

    # Check if container is running
    running=$(docker inspect -f '{{.State.Running}}' "$cid" 2>/dev/null || echo "false")
    if [[ "$running" != "true" ]]; then
        log_warn "[$name] Container $cid is not running – skipping."
        continue
    fi

    case "$provider" in
        0|"SqlServer")
            backup_file="TenantDb_${timestamp}.bak"
            log_info "[$name] Backing up SQL Server -> $backup_file"
            if docker exec "$cid" /opt/mssql-tools18/bin/sqlcmd \
                -S localhost -U sa -P "$password" -C \
                -Q "BACKUP DATABASE [TenantDb] TO DISK = N'/app/backups/${backup_file}' WITH INIT, COMPRESSION" \
                >> "$LOG_FILE" 2>&1; then
                log_info "[$name] SQL Server backup complete"
            else
                log_error "[$name] SQL Server backup FAILED"
            fi
            ;;
        1|"Postgres")
            backup_file="TenantDb_${timestamp}.sql"
            log_info "[$name] Backing up PostgreSQL -> $backup_file"
            if docker exec -e "PGPASSWORD=$password" "$cid" \
                pg_dump -U postgres -d TenantDb --column-inserts \
                -f "/app/backups/${backup_file}" \
                >> "$LOG_FILE" 2>&1; then
                log_info "[$name] PostgreSQL backup complete"
            else
                log_error "[$name] PostgreSQL backup FAILED"
            fi
            ;;
        2|"MongoDB")
            mongo_outdir="TenantDb_${timestamp}"
            log_info "[$name] Backing up MongoDB -> $mongo_outdir"
            if docker exec "$cid" \
                mongodump --username root --password "$password" \
                --authenticationDatabase admin --db TenantDb \
                --out "/app/backups/${mongo_outdir}" \
                >> "$LOG_FILE" 2>&1; then
                log_info "[$name] MongoDB backup complete"
            else
                log_error "[$name] MongoDB backup FAILED"
            fi
            ;;
        *)
            log_error "[$name] Unknown provider $provider – skipping."
            ;;
    esac
done

log_info "=== Backup All Tenants finished ==="
