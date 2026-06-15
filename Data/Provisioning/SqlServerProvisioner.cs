using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPoC.Data.Context;

namespace TenantPoC.Data.Provisioning;

public sealed class SqlServerProvisioner : IDatabaseProvisioner
{
    public async Task CreateDatabaseAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var masterCs = DeriveMasterConnectionString(connectionString);
        var builder = new DbContextOptionsBuilder<MssqlTenantDbContext>();
        builder.UseSqlServer(masterCs);
        var options = builder.Options;

        await using var ctx = new MssqlTenantDbContext(options);
        var creator = ctx.Database.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync(cancellationToken))
            await creator.CreateAsync(cancellationToken);
    }

    public async Task ApplySchemaAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var builder = new DbContextOptionsBuilder<MssqlTenantDbContext>();
        builder.UseSqlServer(connectionString);
        var options = builder.Options;

        await using var ctx = new MssqlTenantDbContext(options);
        await ctx.Database.MigrateAsync(cancellationToken);
    }

    private static string DeriveMasterConnectionString(string connectionString)
    {
        var csb = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master"
        };
        return csb.ConnectionString;
    }
}
