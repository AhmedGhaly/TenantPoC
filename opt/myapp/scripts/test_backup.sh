#!/bin/bash
# Manual backup test script
# Seeds dummy data into each tenant and triggers a backup.
# Usage: ./test_backup.sh [tenant_slug]
#   If tenant_slug is provided, only that tenant is processed.
set -o errexit
set -o pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
if [[ -f "$SCRIPT_DIR/../../../tenants.json" ]]; then
    TENANTS_JSON="$(cd "$SCRIPT_DIR/../../../" && pwd)/tenants.json"
else
    TENANTS_JSON="/home/vboxuser/TenantPoC/tenants.json"
fi

TENANT_ROOT="${HOME:-/home/vboxuser}/myapp/tenants"
TENANT_FILTER="${1:-}"

log_info()  { echo "$(date -Iseconds)  INFO   $*"; }
log_error() { echo "$(date -Iseconds)  ERROR  $*"; }

if [[ ! -f "$TENANTS_JSON" ]]; then
    log_error "tenants.json not found at $TENANTS_JSON"
    exit 1
fi

log_info "=== Backup Test Started ==="

count=$(jq length "$TENANTS_JSON")
matched=0

for (( i = 0; i < count; i++ )); do
    tenant=$(jq ".[$i]" "$TENANTS_JSON")
    id=$(jq -r '.Id' <<< "$tenant")
    name=$(jq -r '.Name' <<< "$tenant")
    slug=$(jq -r '.Slug' <<< "$tenant")
    provider=$(jq -r '.DatabaseInfo.Provider' <<< "$tenant")
    container_id=$(jq -r '.DatabaseInfo.ContainerId // ""' <<< "$tenant")
    container_name=$(jq -r '.DatabaseInfo.ContainerName' <<< "$tenant")
    password=$(jq -r '.DatabaseInfo.Password' <<< "$tenant")
    port=$(jq -r '.DatabaseInfo.Port' <<< "$tenant")

    # Apply filter if specified
    if [[ -n "$TENANT_FILTER" && "$slug" != "$TENANT_FILTER" ]]; then
        continue
    fi
    ((matched++))

    cid="${container_id:-$container_name}"
    if [[ -z "$cid" ]]; then
        log_error "[$name] No container ID/name – skipping."
        continue
    fi

    running=$(docker inspect -f '{{.State.Running}}' "$cid" 2>/dev/null || echo "false")
    if [[ "$running" != "true" ]]; then
        log_error "[$name] Container $cid is NOT running – skipping."
        echo "  Start it with: docker start $cid"
        continue
    fi

    backup_dir="$TENANT_ROOT/$id/backups"
    mkdir -p "$backup_dir"
    timestamp=$(date -u +%Y%m%d_%H%M%S)

    log_info "[$name] ($slug) Provider=$provider Container=${cid:0:12}"

    # Seed dummy data by executing SQL/commands inside container
    case "$provider" in
        0|"SqlServer")
            log_info "[$name] Seeding dummy data..."
            # Insert 3 sample widgets via sqlcmd
            docker exec "$cid" /opt/mssql-tools18/bin/sqlcmd \
                -S localhost -U sa -P "$password" -C -d TenantDb \
                -Q "INSERT INTO [WidgetsNewTo] ([Name],[NameEn],[NameAr],[Test],[CreatedAt]) VALUES
                    ('BackupTest-Widget-1','Widget 1 EN','Widget 1 AR','Seeded by test_backup.sh','$(date -u +%Y-%m-%dT%H:%M:%SZ)'),
                    ('BackupTest-Widget-2','Widget 2 EN','Widget 2 AR','Seeded by test_backup.sh','$(date -u +%Y-%m-%dT%H:%M:%SZ)'),
                    ('BackupTest-Widget-3','Widget 3 EN','Widget 3 AR','Seeded by test_backup.sh','$(date -u +%Y-%m-%dT%H:%M:%SZ)')" \
                >> /dev/null 2>&1 && log_info "[$name] Seeded 3 widgets" || log_error "[$name] Seeding FAILED"

            log_info "[$name] Taking SQL Server backup..."
            backup_file="TenantDb_${timestamp}.bak"
            if docker exec "$cid" /opt/mssql-tools18/bin/sqlcmd \
                -S localhost -U sa -P "$password" -C \
                -Q "BACKUP DATABASE [TenantDb] TO DISK = N'/app/backups/${backup_file}' WITH INIT, COMPRESSION" \
                >> /dev/null 2>&1; then
                ls -lh "$backup_dir/$backup_file"
                log_info "[$name] Backup OK: $backup_file"
            else
                log_error "[$name] Backup FAILED"
            fi
            ;;

        1|"Postgres")
            log_info "[$name] Seeding dummy data..."
            docker exec -e "PGPASSWORD=$password" "$cid" \
                psql -U postgres -d TenantDb -c \
                "INSERT INTO \"WidgetsNewTo\" (\"Name\",\"NameEn\",\"NameAr\",\"Test\",\"CreatedAt\") VALUES
                 ('BackupTest-Widget-1','Widget 1 EN','Widget 1 AR','Seeded by test_backup.sh','$(date -u +%Y-%m-%dT%H:%M:%SZ)'),
                 ('BackupTest-Widget-2','Widget 2 EN','Widget 2 AR','Seeded by test_backup.sh','$(date -u +%Y-%m-%dT%H:%M:%SZ)'),
                 ('BackupTest-Widget-3','Widget 3 EN','Widget 3 AR','Seeded by test_backup.sh','$(date -u +%Y-%m-%dT%H:%M:%SZ)')" \
                >> /dev/null 2>&1 && log_info "[$name] Seeded 3 widgets" || log_error "[$name] Seeding FAILED"

            log_info "[$name] Taking PostgreSQL backup..."
            backup_file="TenantDb_${timestamp}.sql"
            if docker exec -e "PGPASSWORD=$password" "$cid" \
                pg_dump -U postgres -d TenantDb --column-inserts \
                -f "/app/backups/${backup_file}" \
                >> /dev/null 2>&1; then
                ls -lh "$backup_dir/$backup_file"
                log_info "[$name] Backup OK: $backup_file"
            else
                log_error "[$name] Backup FAILED"
            fi
            ;;

        *)
            log_error "[$name] Unknown provider $provider – skipping."
            ;;
    esac

    echo ""
done

if [[ -n "$TENANT_FILTER" && "$matched" -eq 0 ]]; then
    log_error "No tenant matched slug '$TENANT_FILTER'"
    exit 1
fi

log_info "=== Backup Test Finished ==="
echo ""
echo "Backup files:"
find "$TENANT_ROOT" -name "TenantDb_*" -type f 2>/dev/null | sort
