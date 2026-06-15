using Microsoft.EntityFrameworkCore;
using TenantPoC.Models;

namespace TenantPoC.Data.Context;

public class PgsqlTenantDbContext : TenantDbContext
{
    public PgsqlTenantDbContext(DbContextOptions<PgsqlTenantDbContext> options)
        : base(options, DatabaseProvider.Postgres) { }
}
