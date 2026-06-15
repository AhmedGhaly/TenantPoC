using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TenantPoC.Data.Context;

public class PgsqlTenantDbContextFactory : IDesignTimeDbContextFactory<PgsqlTenantDbContext>
{
    public PgsqlTenantDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PgsqlTenantDbContext>()
            .UseNpgsql("Host=localhost;Database=TenantDb_DesignTime;Username=postgres;Password=YourPassword123!;")
            .Options;
        return new PgsqlTenantDbContext(options);
    }
}
