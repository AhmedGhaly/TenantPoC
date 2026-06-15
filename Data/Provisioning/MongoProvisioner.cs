using MongoDB.Driver;

namespace TenantPoC.Data.Provisioning;

public sealed class MongoProvisioner : IDatabaseProvisioner
{
    public Task CreateDatabaseAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public async Task ApplySchemaAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var database = GetDatabase(connectionString);

        var collections = await (await database.ListCollectionNamesAsync(cancellationToken: cancellationToken))
            .ToListAsync(cancellationToken);

        await EnsureCollectionAsync(database, "Widgets", collections, cancellationToken);
        await EnsureCollectionAsync(database, "Users", collections, cancellationToken);
        await EnsureCollectionAsync(database, "Settings", collections, cancellationToken);

        var widgets = database.GetCollection<MongoDB.Bson.BsonDocument>("Widgets");
        var indexKeys = Builders<MongoDB.Bson.BsonDocument>.IndexKeys.Ascending("Name");
        await widgets.Indexes.CreateOneAsync(
            new CreateIndexModel<MongoDB.Bson.BsonDocument>(indexKeys),
            cancellationToken: cancellationToken);

        var users = database.GetCollection<MongoDB.Bson.BsonDocument>("Users");
        var emailIndexKeys = Builders<MongoDB.Bson.BsonDocument>.IndexKeys.Ascending("Email");
        await users.Indexes.CreateOneAsync(
            new CreateIndexModel<MongoDB.Bson.BsonDocument>(emailIndexKeys, new CreateIndexOptions { Unique = true }),
            cancellationToken: cancellationToken);

        var tenantIndexKeys = Builders<MongoDB.Bson.BsonDocument>.IndexKeys.Ascending("TenantId");
        await users.Indexes.CreateOneAsync(
            new CreateIndexModel<MongoDB.Bson.BsonDocument>(tenantIndexKeys),
            cancellationToken: cancellationToken);
    }

    private static IMongoDatabase GetDatabase(string connectionString)
    {
        var url = new MongoUrl(connectionString);
        var client = new MongoClient(connectionString);
        return client.GetDatabase(url.DatabaseName);
    }

    private static async Task EnsureCollectionAsync(
        IMongoDatabase database,
        string name,
        List<string> existing,
        CancellationToken ct)
    {
        if (!existing.Contains(name, StringComparer.OrdinalIgnoreCase))
            await database.CreateCollectionAsync(name, cancellationToken: ct);
    }
}
