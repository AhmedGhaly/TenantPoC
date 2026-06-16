namespace TenantPoC.Models;

public enum DatabaseProvider
{
    SqlServer,
    Postgres,
    MongoDB
}





Act as an expert DevOps and Linux backend developer.I am building a multi-tenant console application running on Linux.I need you to implement a robust, secure backup mechanism for the containers my application provisions.

Here are the strict requirements for the implementation:

1. ** Dynamic Tenant Directory Creation:**
   - When a new tenant container is provisioned, the console app must automatically create a dedicated backup directory on the host machine using a unique tenant ID(e.g., `/var/myapp/tenants/{ tenant_id}/backups`).
   - Ensure strict Linux permissions are applied to this directory (e.g., `chmod 700`) so tenants are isolated from each other.

2. **Container Bind Mounting:**
   - Configure the container-spawning logic to bind-mount this host directory into the container's internal backup folder (e.g., `-v /var/myapp/tenants/{tenant_id}/backups:/app/backups`).

3. **Application-Side Backup Naming:**
   - Ensure that whenever the containerized application writes a backup file, it appends a standardized timestamp to the filename (e.g., `tenant_001_YYYY-MM-DD_HHMMSS.sql`) to prevent accidental overwrites.

4. **Secondary "Double-Safety" Copy Logic:**
   - Provide a secondary script or automated function (preferably using `rsync` for efficiency) that clones everything from the primary host directory(`/var/myapp/tenants/`) to a secondary, user-defined destination path(e.g., `/mnt/secure_backups/daily_clones/`).
   - The secondary copy must preserve file attributes and permissions.

5. ** Retention Policy:**
   - Include a cleanup mechanism(like a `find` command or a script function) that automatically purges backups older than 30 days from the secondary location to prevent the disk from filling up.

Please provide the necessary code snippets, shell commands, or script templates required to integrate this workflow into my console application.