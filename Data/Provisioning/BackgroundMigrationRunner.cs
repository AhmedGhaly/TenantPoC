using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using TenantPoC.Data.Context;
using TenantPoC.Models;

namespace TenantPoC.Data.Provisioning;

public class BackgroundMigrationRunner
{
    private readonly IDatabaseProvisionerFactory _provisionerFactory;
    private readonly ILogger<BackgroundMigrationRunner> _logger;
    private readonly ConcurrentDictionary<Guid, Task> _running = new();

    public BackgroundMigrationRunner(
        IDatabaseProvisionerFactory provisionerFactory,
        ILogger<BackgroundMigrationRunner> logger)
    {
        _provisionerFactory = provisionerFactory;
        _logger = logger;
    }

    public void RunAsync(Tenant tenant)
    {
        if (!_running.TryAdd(tenant.Id, Task.CompletedTask))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await MigrateAsync(tenant);
            }
            finally
            {
                _running.TryRemove(tenant.Id, out var _);
            }
        });
    }

    private async Task MigrateAsync(Tenant tenant)
    {
        try
        {
            var cs = tenant.DatabaseInfo.BuildConnectionString();
            var provider = tenant.DatabaseInfo.Provider;

            if (provider == DatabaseProvider.MongoDB)
            {
                var provisioner = _provisionerFactory.Resolve(provider);
                await provisioner.ApplySchemaAsync(cs);
                _logger.LogInformation("MongoDB collections ensured for tenant {Tenant}", tenant.Name);
                return;
            }

            var hasPending = await HasPendingMigrationsAsync(cs, provider);
            if (hasPending)
            {
                _logger.LogInformation("Applying pending migrations to tenant {Tenant}...", tenant.Name);
                var provisioner = _provisionerFactory.Resolve(provider);
                await provisioner.ApplySchemaAsync(cs);
                _logger.LogInformation("Pending migrations applied to tenant {Tenant}", tenant.Name);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Background migration failed for tenant {Tenant}", tenant.Name);
        }
    }

    private static async Task<bool> HasPendingMigrationsAsync(string connectionString, DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer => await HasPendingMssqlAsync(connectionString),
            DatabaseProvider.Postgres => await HasPendingPgsqlAsync(connectionString),
            _ => false
        };
    }

    private static async Task<bool> HasPendingMssqlAsync(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<MssqlTenantDbContext>();
        builder.UseSqlServer(connectionString);
        await using var ctx = new MssqlTenantDbContext(builder.Options);
        return (await ctx.Database.GetPendingMigrationsAsync()).Any();
    }

    private static async Task<bool> HasPendingPgsqlAsync(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<PgsqlTenantDbContext>();
        builder.UseNpgsql(connectionString);
        await using var ctx = new PgsqlTenantDbContext(builder.Options);
        return (await ctx.Database.GetPendingMigrationsAsync()).Any();
    }
}
