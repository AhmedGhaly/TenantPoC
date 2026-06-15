using Docker.DotNet;
using Docker.DotNet.Models;
using MongoDB.Driver;
using Npgsql;
using TenantPoC.Data.Provisioning;
using TenantPoC.Models;

namespace TenantPoC.Data;

public class ContainerProvisioner
{
    private const string SqlServerImage = "mcr.microsoft.com/mssql/server:2022-latest";
    private const string PostgresImage = "postgres:17";
    private const string MongoImage = "mongo:latest";
    private const int SqlServerBasePort = 14331;
    private const int PostgresBasePort = 54321;
    private const int MongoBasePort = 27017;

    //private const long MemoryLimitBytes = 2L * 1024 * 1024 * 1024;       // 2 GB hard limit
    //private const long MemoryReservationBytes = 1L * 1024 * 1024 * 1024; // 1 GB soft limit
    //private const long CpuNanoCPUs = 1_000_000_000L;                    // 1 CPU
    //private const long PidsLimit = 512;                                  // max processes

    private const string MssqlBaseImageRepo = "tenant-base-mssql";
    private const string PostgresBaseImageRepo = "tenant-base-postgres";
    private const string BaseImageTag = "latest";
    private const string BaseImagePassword = "BaseImage@Pwd123!";

    private static readonly DockerClient Docker = new DockerClientConfiguration().CreateClient();

    private readonly IDatabaseProvisionerFactory _provisionerFactory;

    public ContainerProvisioner(IDatabaseProvisionerFactory provisionerFactory)
    {
        _provisionerFactory = provisionerFactory;
    }

    public async Task<TenantDatabaseInfo> SetUp(Tenant tenant, DatabaseProvider provider, CancellationToken ct = default)
    {
        if (provider == DatabaseProvider.SqlServer && await BaseImageExistsAsync(DatabaseProvider.SqlServer, ct))
            return await SetUpFromBaseImageAsync(tenant, ct);

        if (provider == DatabaseProvider.Postgres && await BaseImageExistsAsync(DatabaseProvider.Postgres, ct))
            return await SetUpFromBaseImagePostgresAsync(tenant, ct);

        var (containerId, containerName, port, password) = await CreateTenantContainerAsync(tenant, provider, ct);
        await WaitForDatabaseReadyAsync(containerId, provider, port, password, ct);

        var dbInfo = new TenantDatabaseInfo
        {
            Provider = provider,
            ContainerName = containerName,
            ContainerId = containerId,
            Port = port,
            Password = password,
            DatabaseName = "TenantDb"
        };

        var provisioner = _provisionerFactory.Resolve(provider);
        var cs = dbInfo.BuildConnectionString();
        await provisioner.CreateDatabaseAsync(cs, ct);
        await provisioner.ApplySchemaAsync(cs, ct);

        return dbInfo;
    }

    public async Task<bool> BaseImageExistsAsync(DatabaseProvider provider, CancellationToken ct = default)
    {
        var repo = provider switch
        {
            DatabaseProvider.Postgres => PostgresBaseImageRepo,
            _ => MssqlBaseImageRepo
        };
        var imageRef = $"{repo}:{BaseImageTag}";
        var images = await Docker.Images.ListImagesAsync(
            new ImagesListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["reference"] = new Dictionary<string, bool> { [imageRef] = true }
                }
            }, ct);
        return images.Count > 0;
    }

    public async Task BuildBaseImageAsync(CancellationToken ct = default)
    {
        await BuildMssqlBaseImageAsync(ct);
        await BuildPostgresBaseImageAsync(ct);
    }

    private async Task BuildMssqlBaseImageAsync(CancellationToken ct)
    {
        var containerName = $"base-builder-mssql-{Guid.NewGuid():N}"[..24].ToLower();
        var port = await GetNextAvailablePortAsync(DatabaseProvider.SqlServer, ct);

        await EnsureDockerRunningAsync(ct);
        await EnsureImageExistsAsync(DatabaseProvider.SqlServer, ct);

        var containerId = await CreateContainerAsync(containerName, DatabaseProvider.SqlServer, port, BaseImagePassword, ct);

        try
        {
            await WaitForDatabaseReadyAsync(containerId, DatabaseProvider.SqlServer, port, BaseImagePassword, ct);

            var provisioner = _provisionerFactory.Resolve(DatabaseProvider.SqlServer);
            var cs = $"Server=localhost,{port};Database=TenantDb;User Id=sa;Password={BaseImagePassword};TrustServerCertificate=True;";
            await provisioner.CreateDatabaseAsync(cs, ct);
            await provisioner.ApplySchemaAsync(cs, ct);

            Console.Write("  Stopping container...");
            await Docker.Containers.StopContainerAsync(containerId, new ContainerStopParameters(), ct);
            Console.WriteLine(" stopped.");

            Console.Write("  Committing as image...");
            var commitParams = new CommitContainerChangesParameters
            {
                ContainerID = containerId,
                RepositoryName = MssqlBaseImageRepo,
                Tag = BaseImageTag,
                Comment = $"Tenant schema base image built at {DateTime.UtcNow:O}",
                Author = "TenantPoC"
            };
            var response = await Docker.Images.CommitContainerChangesAsync(commitParams, ct);
            Console.WriteLine($" done. ({response.ID[..12]})");
        }
        finally
        {
            try { await Docker.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters { Force = true }, ct); }
            catch { }
        }
    }

    private async Task BuildPostgresBaseImageAsync(CancellationToken ct)
    {
        var containerName = $"base-builder-pg-{Guid.NewGuid():N}"[..24].ToLower();
        var port = await GetNextAvailablePortAsync(DatabaseProvider.Postgres, ct);

        await EnsureDockerRunningAsync(ct);
        await EnsureImageExistsAsync(DatabaseProvider.Postgres, ct);

        var containerId = await CreateContainerAsync(containerName, DatabaseProvider.Postgres, port, BaseImagePassword, ct);

        try
        {
            await WaitForDatabaseReadyAsync(containerId, DatabaseProvider.Postgres, port, BaseImagePassword, ct);

            var provisioner = _provisionerFactory.Resolve(DatabaseProvider.Postgres);
            var cs = $"Host=localhost;Port={port};Database=TenantDb;Username=postgres;Password={BaseImagePassword};";
            await provisioner.ApplySchemaAsync(cs, ct);

            Console.Write("  Stopping container...");
            await Docker.Containers.StopContainerAsync(containerId, new ContainerStopParameters(), ct);
            Console.WriteLine(" stopped.");

            Console.Write("  Committing as image...");
            var commitParams = new CommitContainerChangesParameters
            {
                ContainerID = containerId,
                RepositoryName = PostgresBaseImageRepo,
                Tag = BaseImageTag,
                Comment = $"Tenant schema base image built at {DateTime.UtcNow:O}",
                Author = "TenantPoC"
            };
            var response = await Docker.Images.CommitContainerChangesAsync(commitParams, ct);
            Console.WriteLine($" done. ({response.ID[..12]})");
        }
        finally
        {
            try { await Docker.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters { Force = true }, ct); }
            catch { }
        }
    }

    private async Task<TenantDatabaseInfo> SetUpFromBaseImagePostgresAsync(Tenant tenant, CancellationToken ct = default)
    {
        var containerName = $"tenant-{tenant.Slug}-{tenant.Id:N}".ToLower();
        var port = await GetNextAvailablePortAsync(DatabaseProvider.Postgres, ct);
        var tenantPwd = $"Tenant@{Guid.NewGuid():N}"[..20];

        await EnsureDockerRunningAsync(ct);

        Console.WriteLine($"  Starting PostgreSQL container '{containerName}' from base image...");
        var containerId = await CreateContainerAsync(containerName, DatabaseProvider.Postgres, port, BaseImagePassword, ct,
            imageOverride: $"{PostgresBaseImageRepo}:{BaseImageTag}");

        await WaitForDatabaseReadyAsync(containerId, DatabaseProvider.Postgres, port, BaseImagePassword, ct);

        var masterCs = new TenantDatabaseInfo
        {
            Provider = DatabaseProvider.Postgres,
            Port = port,
            Password = BaseImagePassword,
            DatabaseName = "postgres"
        }.BuildMasterConnectionString();

        await using var conn = new NpgsqlConnection(masterCs);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"ALTER USER postgres WITH PASSWORD '{tenantPwd}';";
        await cmd.ExecuteNonQueryAsync(ct);

        return new TenantDatabaseInfo
        {
            Provider = DatabaseProvider.Postgres,
            ContainerName = containerName,
            ContainerId = containerId,
            Port = port,
            Password = tenantPwd,
            DatabaseName = "TenantDb"
        };
    }

    private async Task<TenantDatabaseInfo> SetUpFromBaseImageAsync(Tenant tenant, CancellationToken ct = default)
    {
        var containerName = $"tenant-{tenant.Slug}-{tenant.Id:N}".ToLower();
        var port = await GetNextAvailablePortAsync(DatabaseProvider.SqlServer, ct);
        var tenantPwd = $"Tenant@{Guid.NewGuid():N}"[..20];

        await EnsureDockerRunningAsync(ct);

        Console.WriteLine($"  Starting SQL Server container '{containerName}' from base image...");
        var containerId = await CreateContainerAsync(containerName, DatabaseProvider.SqlServer, port, tenantPwd, ct,
            imageOverride: $"{MssqlBaseImageRepo}:{BaseImageTag}");

        await WaitForDatabaseReadyAsync(containerId, DatabaseProvider.SqlServer, port, BaseImagePassword, ct);

        var masterCs = new TenantDatabaseInfo
        {
            Provider = DatabaseProvider.SqlServer,
            Port = port,
            Password = BaseImagePassword,
            DatabaseName = "master"
        }.BuildMasterConnectionString();

        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(masterCs);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"ALTER LOGIN [sa] WITH PASSWORD = N'{tenantPwd}';";
        await cmd.ExecuteNonQueryAsync(ct);

        return new TenantDatabaseInfo
        {
            Provider = DatabaseProvider.SqlServer,
            ContainerName = containerName,
            ContainerId = containerId,
            Port = port,
            Password = tenantPwd,
            DatabaseName = "TenantDb"
        };
    }

    public async Task<(string ContainerId, string ContainerName, int Port, string Password)> CreateTenantContainerAsync(
        Tenant tenant, DatabaseProvider provider, CancellationToken ct = default)
    {
        var containerName = $"tenant-{tenant.Slug}-{tenant.Id:N}".ToLower();
        var port = await GetNextAvailablePortAsync(provider, ct);
        var pwd = $"Tenant@{Guid.NewGuid():N}"[..20];

        await EnsureDockerRunningAsync(ct);
        await EnsureImageExistsAsync(provider, ct);
        var containerId = await CreateContainerAsync(containerName, provider, port, pwd, ct);

        return (containerId, containerName, port, pwd);
    }

    public async Task WaitForDatabaseReadyAsync(
        string containerId, DatabaseProvider provider, int port, string password,
        CancellationToken ct = default)
    {
        await WaitForDatabaseReadyInternalAsync(containerId, provider, port, password, ct);
    }

    private static async Task EnsureDockerRunningAsync(CancellationToken ct)
    {
        try
        {
            await Docker.System.PingAsync(ct);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Docker is not running. Start Docker Desktop and try again.", ex);
        }
    }

    private static async Task EnsureImageExistsAsync(DatabaseProvider provider, CancellationToken ct)
    {
        var image = provider switch
        {
            DatabaseProvider.Postgres => PostgresImage,
            DatabaseProvider.MongoDB => MongoImage,
            _ => SqlServerImage
        };

        var images = await Docker.Images.ListImagesAsync(
            new ImagesListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["reference"] = new Dictionary<string, bool> { [image] = true }
                }
            }, ct);

        if (images.Count > 0) return;

        Console.Write($"  Pulling image '{image}'...");
        var pullParams = new ImagesCreateParameters { FromImage = image };
        await Docker.Images.CreateImageAsync(
            pullParams, null, new Progress<JSONMessage>(m =>
            {
                if (m.Status is not null)
                    Console.Write(".");
            }), ct);
        Console.WriteLine(" done.");
    }

    private static async Task<int> GetNextAvailablePortAsync(DatabaseProvider provider, CancellationToken ct)
    {
        var containers = await Docker.Containers.ListContainersAsync(
            new ContainersListParameters { All = true }, ct);

        var used = containers
            .SelectMany(c => c.Ports ?? [])
            .Where(p => p.PublicPort > 0)
            .Select(p => (int)p.PublicPort)
            .ToHashSet();

        var basePort = provider switch
        {
            DatabaseProvider.Postgres => PostgresBasePort,
            DatabaseProvider.MongoDB => MongoBasePort,
            _ => SqlServerBasePort
        };
        var port = basePort;
        while (used.Contains(port)) port++;
        return port;
    }

    private static async Task<string> CreateContainerAsync(string name, DatabaseProvider provider, int port, string pwd, CancellationToken ct, string? imageOverride = null)
    {
        Console.WriteLine($"  Starting {provider} container '{name}' on port {port}...");

        var (image, containerPort, env) = provider switch
        {
            DatabaseProvider.Postgres => (
                imageOverride ?? PostgresImage, "5432/tcp",
                new List<string> { $"POSTGRES_PASSWORD={pwd}", "POSTGRES_DB=TenantDb" }),
            DatabaseProvider.MongoDB => (
                imageOverride ?? MongoImage, "27017/tcp",
                new List<string> { "MONGO_INITDB_ROOT_USERNAME=root", $"MONGO_INITDB_ROOT_PASSWORD={pwd}", "MONGO_INITDB_DATABASE=TenantDb" }),
            _ => (
                imageOverride ?? SqlServerImage, "1433/tcp",
                new List<string>
                {
                    "ACCEPT_EULA=Y",
                    $"MSSQL_SA_PASSWORD={pwd}",
                    //$"MSSQL_MEMORY_LIMIT_MB={MemoryLimitBytes / (1024 * 1024) * 3 / 4}"
                })
        };

        var createParams = new CreateContainerParameters
        {
            Image = image,
            Name = name,
            Env = env,
            HostConfig = new HostConfig
            {
                PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    [containerPort] = new List<PortBinding>
                    {
                        new() { HostPort = port.ToString() }
                    }
                },
                //Memory = MemoryLimitBytes,
                //MemorySwap = MemoryLimitBytes,
                //MemoryReservation = MemoryReservationBytes,
                //NanoCPUs = CpuNanoCPUs,
                //PidsLimit = PidsLimit
            }
        };

        var created = await Docker.Containers.CreateContainerAsync(createParams, ct);
        await Docker.Containers.StartContainerAsync(created.ID, null, ct);
        Console.WriteLine($"  Container {created.ID[..12]} created.");
        return created.ID;
    }

    private static async Task WaitForDatabaseReadyInternalAsync(
        string containerId, DatabaseProvider provider, int port, string pwd, CancellationToken ct)
    {
        switch (provider)
        {
            case DatabaseProvider.Postgres:
                await WaitForPostgresAsync(containerId, port, pwd, ct);
                break;
            case DatabaseProvider.MongoDB:
                await WaitForMongoAsync(containerId, port, pwd, ct);
                break;
            default:
                await WaitForSqlServerAsync(containerId, port, pwd, ct);
                break;
        }
    }

    private static async Task WaitForSqlServerAsync(string containerId, int port, string pwd, CancellationToken ct)
    {
        var cs = new TenantDatabaseInfo
        {
            Provider = DatabaseProvider.SqlServer,
            Port = port,
            Password = pwd,
            DatabaseName = "master"
        }.BuildMasterConnectionString();

        Console.Write("  Waiting for SQL Server");
        for (var i = 0; i < 120; i++)
        {
            var (status, logs) = await GetContainerStatusAsync(containerId, ct);
            if (status != "running")
            {
                Console.WriteLine($"\n  Container status: {status}");
                if (!string.IsNullOrEmpty(logs)) Console.WriteLine($"  Logs:\n{logs}");
                throw new InvalidOperationException($"Container exited with status '{status}'.");
            }

            try
            {
                await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cs);
                await conn.OpenAsync(ct);
                Console.WriteLine(" ready!");
                return;
            }
            catch
            {
                Console.Write(".");
                await Task.Delay(2000, ct);
            }
        }
        var finalLogs = await GetContainerLogsAsync(containerId, ct);
        throw new TimeoutException($"SQL Server did not become ready.\nContainer logs:\n{finalLogs}");
    }

    private static async Task WaitForPostgresAsync(string containerId, int port, string pwd, CancellationToken ct)
    {
        var cs = new TenantDatabaseInfo
        {
            Provider = DatabaseProvider.Postgres,
            Port = port,
            Password = pwd,
            DatabaseName = "postgres"
        }.BuildMasterConnectionString();

        Console.Write("  Waiting for PostgreSQL");
        for (var i = 0; i < 120; i++)
        {
            var (status, logs) = await GetContainerStatusAsync(containerId, ct);
            if (status != "running")
            {
                Console.WriteLine($"\n  Container status: {status}");
                if (!string.IsNullOrEmpty(logs)) Console.WriteLine($"  Logs:\n{logs}");
                throw new InvalidOperationException($"Container exited with status '{status}'.");
            }

            try
            {
                await using var conn = new NpgsqlConnection(cs);
                await conn.OpenAsync(ct);
                Console.WriteLine(" ready!");
                return;
            }
            catch
            {
                Console.Write(".");
                await Task.Delay(2000, ct);
            }
        }
        var finalLogs = await GetContainerLogsAsync(containerId, ct);
        throw new TimeoutException($"PostgreSQL did not become ready.\nContainer logs:\n{finalLogs}");
    }

    private static async Task WaitForMongoAsync(string containerId, int port, string pwd, CancellationToken ct)
    {
        var cs = new TenantDatabaseInfo
        {
            Provider = DatabaseProvider.MongoDB,
            Port = port,
            Password = pwd,
            DatabaseName = "admin"
        }.BuildMasterConnectionString();

        var client = new MongoClient(cs);
        var adminDb = client.GetDatabase("admin");

        Console.Write("  Waiting for MongoDB");
        for (var i = 0; i < 120; i++)
        {
            var (status, logs) = await GetContainerStatusAsync(containerId, ct);
            if (status != "running")
            {
                Console.WriteLine($"\n  Container status: {status}");
                if (!string.IsNullOrEmpty(logs)) Console.WriteLine($"  Logs:\n{logs}");
                throw new InvalidOperationException($"Container exited with status '{status}'.");
            }

            try
            {
                await adminDb.RunCommandAsync<MongoDB.Bson.BsonDocument>(
                    MongoDB.Bson.BsonDocument.Parse("{ ping: 1 }"), cancellationToken: ct);
                Console.WriteLine(" ready!");
                return;
            }
            catch
            {
                Console.Write(".");
                await Task.Delay(2000, ct);
            }
        }
        var finalLogs = await GetContainerLogsAsync(containerId, ct);
        throw new TimeoutException($"MongoDB did not become ready.\nContainer logs:\n{finalLogs}");
    }

    private static async Task<(string status, string logs)> GetContainerStatusAsync(string containerId, CancellationToken ct)
    {
        try
        {
            var inspect = await Docker.Containers.InspectContainerAsync(containerId, ct);
            var status = inspect.State.Running ? "running" : inspect.State.Status;
            var logs = status != "running" ? await GetContainerLogsAsync(containerId, ct) : "";
            return (status, logs);
        }
        catch (Docker.DotNet.DockerApiException)
        {
            return ("not found", "");
        }
    }

    private static async Task<string> GetContainerLogsAsync(string containerId, CancellationToken ct)
    {
        try
        {
            var mxStream = await Docker.Containers.GetContainerLogsAsync(containerId, true,
                new ContainerLogsParameters { Tail = "30", ShowStdout = true, ShowStderr = true }, ct);
            using var ms = new MemoryStream();
            var buf = new byte[8192];
            MultiplexedStream.ReadResult result;
            while (!(result = await mxStream.ReadOutputAsync(buf, 0, buf.Length, ct)).EOF)
                await ms.WriteAsync(buf, 0, result.Count, ct);
            ms.Position = 0;
            using var reader = new StreamReader(ms);
            var raw = await reader.ReadToEndAsync(ct);
            return raw.Trim();
        }
        catch
        {
            return "";
        }
    }
}
