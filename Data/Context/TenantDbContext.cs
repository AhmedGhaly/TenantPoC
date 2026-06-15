using Microsoft.EntityFrameworkCore;
using TenantPoC.Models;

namespace TenantPoC.Data.Context;

public class TenantDbContext : DbContext
{
    private readonly DatabaseProvider _provider;

    public DbSet<Widget> Widgets => Set<Widget>();

    public TenantDbContext(DbContextOptions options, DatabaseProvider provider = DatabaseProvider.SqlServer)
        : base(options)
    {
        _provider = provider;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Widget>(e =>
        {
            e.ToTable("WidgetsNewTo");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.NameEn).HasMaxLength(100).IsRequired();
            e.Property(x => x.NameAr).HasMaxLength(100).IsRequired();
            if (_provider != DatabaseProvider.MongoDB)
                e.Property(x => x.CreatedAt).HasDefaultValueSql(
                    _provider == DatabaseProvider.Postgres ? "NOW()" : "SYSUTCDATETIME()");
        });
    }
}

public class Widget
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string Test { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
