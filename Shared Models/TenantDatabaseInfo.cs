namespace TenantPoC.Models;

public class TenantDatabaseInfo
{
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.SqlServer;
    public string ContainerName { get; set; } = string.Empty;
    public string ContainerId { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Password { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;

    public string BuildConnectionString()
    {
        return Provider switch
        {
            DatabaseProvider.Postgres =>
                $"Host=localhost;Port={Port};Database={DatabaseName};Username=postgres;Password={Password};",
            DatabaseProvider.MongoDB =>
                $"mongodb://root:{Uri.EscapeDataString(Password)}@localhost:{Port}/{DatabaseName}?authSource=admin",
            _ =>
                $"Server=localhost,{Port};Database={DatabaseName};User Id=sa;Password={Password};TrustServerCertificate=True;"
        };
    }

    public string BuildMasterConnectionString()
    {
        return Provider switch
        {
            DatabaseProvider.Postgres =>
                $"Host=localhost;Port={Port};Database=postgres;Username=postgres;Password={Password};",
            DatabaseProvider.MongoDB =>
                $"mongodb://root:{Uri.EscapeDataString(Password)}@localhost:{Port}/admin?authSource=admin",
            _ =>
                $"Server=localhost,{Port};Database=master;User Id=sa;Password={Password};TrustServerCertificate=True;Connect Timeout=3;"
        };
    }
}
