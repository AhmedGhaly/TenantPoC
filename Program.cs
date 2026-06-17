using Microsoft.EntityFrameworkCore;
using TenantPoC.Data;
using TenantPoC.Data.Context;
using TenantPoC.Data.Provisioning;
using TenantPoC.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<MasterStoreRepo>();
builder.Services.AddSingleton<IDatabaseProvisionerFactory, DatabaseProvisionerFactory>();
builder.Services.AddSingleton<ContainerProvisioner>();
builder.Services.AddSingleton<BackgroundMigrationRunner>();
builder.Services.AddScoped<TenantConnectionFactory>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// Tenant resolution middleware
app.Use(async (context, next) =>
{
    var slug = context.Request.Headers["X-Tenant-Slug"].FirstOrDefault();
    if (slug is not null)
    {
        var store = context.RequestServices.GetRequiredService<MasterStoreRepo>();
        var tenant = store.GetBySlug(slug);
        if (tenant is not null)
        {
            var factory = context.RequestServices.GetRequiredService<TenantConnectionFactory>();
            factory.SwitchTo(tenant);
            var bgRunner = context.RequestServices.GetRequiredService<BackgroundMigrationRunner>();
            bgRunner.RunAsync(tenant);
        }
    }
    await next();
});

// === Tenant Endpoints ===

app.MapGet("/api/tenants", (MasterStoreRepo store) =>
{
    var tenants = store.GetAll().Select(t => new
    {
        t.Id,
        t.Name,
        t.Slug,
        t.CreatedAt,
        Provider = t.DatabaseInfo.Provider.ToString(),
        Container = t.DatabaseInfo.ContainerName,
        Port = t.DatabaseInfo.Port
    });
    return Results.Ok(tenants);
});

app.MapPost("/api/tenants", async (
    CreateTenantRequest request,
    MasterStoreRepo store,
    ContainerProvisioner provisioner,
    ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
        return Results.BadRequest(new { error = "Name is required." });

    var slug = request.Name.ToLower().Replace(" ", "-");
    if (store.GetBySlug(slug) is not null)
        return Results.Conflict(new { error = $"Tenant '{request.Name}' already exists." });

    var provider = request.Provider?.ToLower() switch
    {
        "postgres" or "postgresql" => DatabaseProvider.Postgres,
        "mongo" or "mongodb" => DatabaseProvider.MongoDB,
        _ => DatabaseProvider.SqlServer
    };

    var tenant = new Tenant { Name = request.Name, Slug = slug };
    try
    {
        tenant.DatabaseInfo = await provisioner.SetUp(tenant, provider);
        store.Add(tenant);
        logger.LogInformation("Tenant '{Name}' provisioned with {Provider}", tenant.Name, provider);
        return Results.Created($"/api/tenants/{tenant.Id}", new
        {
            tenant.Id,
            tenant.Name,
            tenant.Slug,
            tenant.CreatedAt,
            Provider = provider.ToString(),
            Container = tenant.DatabaseInfo.ContainerName,
            Port = tenant.DatabaseInfo.Port
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to provision tenant '{Tenant}'", request.Name);
        return Results.Problem($"Provisioning failed: {ex.Message}", statusCode: 500);
    }
});

app.MapDelete("/api/tenants/{id:guid}", (Guid id, MasterStoreRepo store) =>
{
    if (store.Remove(id))
        return Results.NoContent();
    return Results.NotFound();
});

// === Migration Endpoints ===

app.MapPost("/api/migrate", async (
    MasterStoreRepo store,
    IDatabaseProvisionerFactory provisionerFactory) =>
{
    var all = store.GetAll();
    var results = new List<object>();
    foreach (var t in all)
    {
        try
        {
            var p = provisionerFactory.Resolve(t.DatabaseInfo.Provider);
            await p.ApplySchemaAsync(t.DatabaseInfo.BuildConnectionString());
            results.Add(new { t.Id, t.Name, Status = "ok" });
        }
        catch (Exception ex)
        {
            results.Add(new { t.Id, t.Name, Status = "failed", Error = ex.Message });
        }
    }
    return Results.Ok(results);
});

app.MapPost("/api/migrate/{id:guid}", async (
    Guid id,
    MasterStoreRepo store,
    IDatabaseProvisionerFactory provisionerFactory) =>
{
    var tenant = store.GetById(id);
    if (tenant is null) return Results.NotFound();
    try
    {
        var p = provisionerFactory.Resolve(tenant.DatabaseInfo.Provider);
        await p.ApplySchemaAsync(tenant.DatabaseInfo.BuildConnectionString());
        return Results.Ok(new { tenant.Id, tenant.Name, Status = "ok" });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: 500);
    }
});

// === Backup Endpoints ===

app.MapPost("/api/backups/{id:guid}", async (
    Guid id,
    MasterStoreRepo store,
    ContainerProvisioner provisioner,
    ILogger<Program> logger) =>
{
    var tenant = store.GetById(id);
    if (tenant is null) return Results.NotFound(new { error = "Tenant not found." });

    try
    {
        await provisioner.TakeBackupForTenantAsync(tenant);
        logger.LogInformation("Backup completed for tenant '{Tenant}'", tenant.Name);
        return Results.Ok(new { tenant.Id, tenant.Name, Status = "ok" });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Backup failed for tenant '{Tenant}'", tenant.Name);
        return Results.Problem($"Backup failed: {ex.Message}", statusCode: 500);
    }
});

app.MapPost("/api/backups", async (
    MasterStoreRepo store,
    ContainerProvisioner provisioner,
    ILogger<Program> logger) =>
{
    var tenants = store.GetAll();
    var results = new List<object>();
    foreach (var t in tenants)
    {
        try
        {
            await provisioner.TakeBackupForTenantAsync(t);
            results.Add(new { t.Id, t.Name, Status = "ok" });
            logger.LogInformation("Backup completed for tenant '{Tenant}'", t.Name);
        }
        catch (Exception ex)
        {
            results.Add(new { t.Id, t.Name, Status = "failed", Error = ex.Message });
            logger.LogError(ex, "Backup failed for tenant '{Tenant}'", t.Name);
        }
    }
    return Results.Ok(results);
});

app.MapGet("/api/tenants/{id:guid}/backups", (Guid id, MasterStoreRepo store) =>
{
    var tenant = store.GetById(id);
    if (tenant is null) return Results.NotFound(new { error = "Tenant not found." });

    var backupsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "myapp", "tenants", id.ToString(), "backups");

    if (!Directory.Exists(backupsPath))
        return Results.Ok(new { tenant.Id, tenant.Name, Files = Array.Empty<object>() });

    var files = Directory.GetFiles(backupsPath)
        .Select(f => new FileInfo(f))
        .Select(f => new { f.Name, SizeBytes = f.Length, LastModified = f.LastWriteTimeUtc })
        .OrderByDescending(f => f.LastModified)
        .ToList();

    return Results.Ok(new { tenant.Id, tenant.Name, Files = files });
});

// === Seed Data Endpoint ===

app.MapPost("/api/tenants/seed", async (
    MasterStoreRepo store,
    TenantConnectionFactory factory,
    ILogger<Program> logger) =>
{
    var tenants = store.GetAll();
    var results = new List<object>();
    foreach (var t in tenants)
    {
        try
        {
            await using var ctx = factory.CreateDbContext(t);
            var dummyWidgets = Enumerable.Range(1, 10).Select(i => new Widget
            {
                Name = $"Dummy Widget {i}",
                NameEn = $"Dummy Widget {i} EN",
                NameAr = $"Dummy Widget {i} AR",
                Test = $"Seed data for tenant {t.Name} - widget {i}",
                CreatedAt = DateTime.UtcNow
            }).ToList();
            ctx.Widgets.AddRange(dummyWidgets);
            await ctx.SaveChangesAsync();
            results.Add(new { t.Id, t.Name, Status = "ok", WidgetsSeeded = 10 });
            logger.LogInformation("Seeded 10 widgets into tenant '{Tenant}'", t.Name);
        }
        catch (Exception ex)
        {
            results.Add(new { t.Id, t.Name, Status = "failed", Error = ex.Message });
            logger.LogError(ex, "Seeding failed for tenant '{Tenant}'", t.Name);
        }
    }
    return Results.Ok(results);
});

app.MapPost("/api/tenants/seed-and-backup", async (
    MasterStoreRepo store,
    TenantConnectionFactory factory,
    ContainerProvisioner provisioner,
    ILogger<Program> logger) =>
{
    var tenants = store.GetAll();
    var results = new List<object>();
    foreach (var t in tenants)
    {
        try
        {
            await using var ctx = factory.CreateDbContext(t);
            var dummyWidgets = Enumerable.Range(1, 10).Select(i => new Widget
            {
                Name = $"Dummy Widget {i}",
                NameEn = $"Dummy Widget {i} EN",
                NameAr = $"Dummy Widget {i} AR",
                Test = $"Seed data for tenant backup test - {t.Name} - widget {i}",
                CreatedAt = DateTime.UtcNow
            }).ToList();
            ctx.Widgets.AddRange(dummyWidgets);
            await ctx.SaveChangesAsync();

            await provisioner.TakeBackupForTenantAsync(t);

            results.Add(new { t.Id, t.Name, Status = "ok", WidgetsSeeded = 10, BackupTaken = true });
            logger.LogInformation("Seeded + backed up tenant '{Tenant}'", t.Name);
        }
        catch (Exception ex)
        {
            results.Add(new { t.Id, t.Name, Status = "failed", Error = ex.Message });
            logger.LogError(ex, "Seed+backup failed for tenant '{Tenant}'", t.Name);
        }
    }
    return Results.Ok(results);
});

// === Widget Endpoints ===

app.MapGet("/api/widgets", async (TenantConnectionFactory factory) =>
{
    if (factory.CurrentTenant is null)
        return Results.BadRequest(new { error = "X-Tenant-Slug header is required." });
    await using var ctx = factory.CreateDbContext();
    var widgets = await ctx.Widgets.OrderBy(w => w.Id).ToListAsync();
    return Results.Ok(widgets);
});

app.MapPost("/api/widgets", async (CreateWidgetRequest request, TenantConnectionFactory factory) =>
{
    if (factory.CurrentTenant is null)
        return Results.BadRequest(new { error = "X-Tenant-Slug header is required." });
    await using var ctx = factory.CreateDbContext();
    var widget = new Widget { Name = request.Name };
    ctx.Widgets.Add(widget);
    await ctx.SaveChangesAsync();
    return Results.Created($"/api/widgets/{widget.Id}", widget);
});

app.MapGet("/api/widgets/search", async (string name, TenantConnectionFactory factory) =>
{
    if (factory.CurrentTenant is null)
        return Results.BadRequest(new { error = "X-Tenant-Slug header is required." });
    await using var ctx = factory.CreateDbContext();
    var widgets = await ctx.Widgets.Where(w => w.Name.Contains(name)).OrderBy(w => w.Id).ToListAsync();
    return Results.Ok(widgets);
});

app.MapDelete("/api/widgets/{id:int}", async (int id, TenantConnectionFactory factory) =>
{
    if (factory.CurrentTenant is null)
        return Results.BadRequest(new { error = "X-Tenant-Slug header is required." });
    await using var ctx = factory.CreateDbContext();
    var widget = await ctx.Widgets.FindAsync(id);
    if (widget is null) return Results.NotFound();
    ctx.Widgets.Remove(widget);
    await ctx.SaveChangesAsync();
    return Results.NoContent();
});

app.Run();

record CreateTenantRequest(string Name, string? Provider);
record CreateWidgetRequest(string Name);
