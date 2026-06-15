using Microsoft.EntityFrameworkCore;
using TenantPoC.Models;

namespace TenantPoC.Data.Context;

public class TenantConnectionFactory
{
    private Tenant? _currentTenant;

    public Tenant? CurrentTenant => _currentTenant;

    public void SwitchTo(Tenant tenant)
    {
        _currentTenant = tenant;
    }

    public void Clear()
    {
        _currentTenant = null;
    }

    public TenantDbContext CreateDbContext()
    {
        var tenant = _currentTenant
            ?? throw new InvalidOperationException("No tenant selected.");
        return CreateDbContext(tenant);
    }

    public TenantDbContext CreateDbContext(Tenant tenant)
    {
        var provider = tenant.DatabaseInfo.Provider;
        var options = BuildOptions(tenant, tenant.DatabaseInfo.BuildConnectionString());
        return new TenantDbContext(options, provider);
    }

    private static DbContextOptions BuildOptions(Tenant tenant, string connectionString)
    {
        var builder = new DbContextOptionsBuilder();
        var provider = tenant.DatabaseInfo.Provider;
        if (provider == DatabaseProvider.Postgres)
            builder.UseNpgsql(connectionString);
        else if (provider == DatabaseProvider.MongoDB)
            builder.UseMongoDB(connectionString, tenant.DatabaseInfo.DatabaseName);
        else
            builder.UseSqlServer(connectionString);
        return builder.Options;
    }
}
