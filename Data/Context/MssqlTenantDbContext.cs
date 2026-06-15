using Microsoft.EntityFrameworkCore;
using TenantPoC.Models;

namespace TenantPoC.Data.Context;

public class MssqlTenantDbContext : TenantDbContext
{
    public MssqlTenantDbContext(DbContextOptions<MssqlTenantDbContext> options)
        : base(options, DatabaseProvider.SqlServer) { }
}
