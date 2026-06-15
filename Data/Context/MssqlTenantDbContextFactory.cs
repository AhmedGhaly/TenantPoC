using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TenantPoC.Data.Context;

public class MssqlTenantDbContextFactory : IDesignTimeDbContextFactory<MssqlTenantDbContext>
{
    public MssqlTenantDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MssqlTenantDbContext>()
            .UseSqlServer("Server=localhost;Database=TenantDb_DesignTime;Trusted_Connection=True;TrustServerCertificate=True;")
            .Options;
        return new MssqlTenantDbContext(options);
    }
}
