using TenantPoC.Models;

namespace TenantPoC.Data.Provisioning;

public sealed class DatabaseProvisionerFactory : IDatabaseProvisionerFactory
{
    private readonly SqlServerProvisioner _sqlServer = new();
    private readonly PostgresProvisioner _postgres = new();
    private readonly MongoProvisioner _mongo = new();

    public IDatabaseProvisioner Resolve(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer => _sqlServer,
            DatabaseProvider.Postgres => _postgres,
            DatabaseProvider.MongoDB => _mongo,
            _ => throw new NotSupportedException($"Database provider '{provider}' is not supported.")
        };
    }
}
