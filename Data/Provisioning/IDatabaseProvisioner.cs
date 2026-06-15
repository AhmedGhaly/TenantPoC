namespace TenantPoC.Data.Provisioning;

public interface IDatabaseProvisioner
{
    Task CreateDatabaseAsync(
        string connectionString,
        CancellationToken cancellationToken = default);

    Task ApplySchemaAsync(
        string connectionString,
        CancellationToken cancellationToken = default);
}
