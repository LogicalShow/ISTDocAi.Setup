using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace ISTDocAi.Provisioning;

public sealed record ServerSettings(string TenantId, string ClientId, string Thumbprint, DateTimeOffset NotAfter,
    string BlobEndpoint, string DocumentIntelligenceEndpoint, string Version, DateTimeOffset PackagedAt);
public sealed record ServerInstall(string Package, string Site, string Host, int Port, string TlsThumbprint,
    string Folder, string Account, string SqlServer, string Database, bool TrustServerCertificate,
    string TenantId, string ClientId, string Thumbprint, string Pfx);
public sealed class ServerJournal
{
    public int Version { get; set; } = 1;
    public required ServerInstall Install { get; set; }
    public List<string> Started { get; set; } = [];
    public List<string> Done { get; set; } = [];
    public List<string> Removed { get; set; } = [];
    public bool Complete { get; set; }
    public bool Removing { get; set; }
    public bool KeyRuleAdded { get; set; }
    public bool LoginDbaConfirmed { get; set; }
    public bool LoginCreated { get; set; }
    public bool CertificatePreexisting { get; set; }
    public List<string> PreviousIdentityThumbprints { get; set; } = [];
    public string? PendingIdentityThumbprint { get; set; }
}
public static class ServerSetupModel
{
    public static readonly string[] InstallSteps = ["certificate", "pool", "key-read", "secrets", "logs", "files", "configuration", "folder-read", "site", "binding", "login", "start", "health"];
    public static string Connection(ServerInstall i, string? database = null) => new SqlConnectionStringBuilder
    {
        DataSource = i.SqlServer, InitialCatalog = database ?? i.Database, IntegratedSecurity = true,
        Encrypt = SqlConnectionEncryptOption.Mandatory, TrustServerCertificate = i.TrustServerCertificate, ConnectTimeout = 15
    }.ConnectionString;
    public static string AppLoginSql(string account)
    {
        if (!Regex.IsMatch(account, @"\A[A-Za-z0-9_. $\\-]{1,128}\z")) throw new CliRefusal("Invalid Windows login name.");
        return "-- DocAi creates the database on first start and becomes its owner.\r\n-- No database user or role is needed for the application identity.\r\nUSE [master];\r\nGO\r\n" +
            $"IF SUSER_ID(N'{account}') IS NULL CREATE LOGIN [{account}] FROM WINDOWS;\r\nGO\r\n" +
            $"IF NOT EXISTS (SELECT 1 FROM sys.server_role_members WHERE role_principal_id=SUSER_ID(N'dbcreator') AND member_principal_id=SUSER_ID(N'{account}')) ALTER SERVER ROLE [dbcreator] ADD MEMBER [{account}];\r\nGO\r\n";
    }
    public static string Endpoint(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new CliRefusal("A keyless HTTPS endpoint is required.");
        return uri.AbsoluteUri;
    }
    public static void ValidateIdentity(string tenant, string client, string thumb, DateTimeOffset expiry)
    {
        if (!Guid.TryParse(tenant, out _) || !Guid.TryParse(client, out _) || !Regex.IsMatch(thumb, @"\A[0-9a-fA-F]{40}\z") || expiry <= DateTimeOffset.UtcNow)
            throw new CliRefusal("Identity requires tenantId/clientId GUIDs, thumbprint and a future notAfter.");
    }
    public static JsonObject Configuration(JsonObject root, ServerInstall i, string stateRoot, ServerSettings settings)
    {
        void Set(string key, JsonNode value)
        {
            var current = root; var keys = key.Split(':');
            foreach (var part in keys[..^1]) { if (current[part] is null) current[part] = new JsonObject(); current = current[part]!.AsObject(); }
            current[keys[^1]] = value;
        }
        Set("ConnectionStrings:DefaultConnection", JsonValue.Create(Connection(i))!);
        Set("DocAi:Identity", new JsonObject { ["Mode"] = "ClientCertificate", ["StoreLocation"] = "LocalMachine", ["TenantId"] = i.TenantId, ["ClientId"] = i.ClientId, ["CertificateThumbprint"] = i.Thumbprint });
        Set("DocAi:Blob:AccountUrl", JsonValue.Create(settings.BlobEndpoint)!);
        Set("DocAi:DocumentIntelligence:Endpoint", JsonValue.Create(settings.DocumentIntelligenceEndpoint)!);
        Set("DocAi:KeyVault:Url", JsonValue.Create("")!);
        Set("DocAi:Secrets", new JsonObject { ["Provider"] = "ProtectedFile", ["ProtectedFile"] = new JsonObject { ["Path"] = Path.Combine(stateRoot, "secrets", "webhook-secrets.json") } });
        Set("DocAi:Database", new JsonObject { ["MigrateOnStartup"] = true, ["AllowDatabaseCreation"] = true, ["CommandTimeoutSeconds"] = 300, ["InitializationTimeoutSeconds"] = 900 });
        Set("Worker", new JsonObject { ["Enabled"] = true, ["MaxParallelJobs"] = 4 });
        Set("Retention", new JsonObject { ["Enabled"] = true, ["RunAtUtc"] = "02:00:00" });
        Set("Signals", new JsonObject { ["Enabled"] = true }); Set("Webhooks:Enabled", JsonValue.Create(true)!);
        Set("Serilog:Using", new JsonArray("Serilog.Sinks.Console", "Serilog.Sinks.File"));
        Set("Serilog:WriteTo", new JsonArray(new JsonObject { ["Name"] = "Console" }, new JsonObject { ["Name"] = "File", ["Args"] = new JsonObject { ["path"] = Path.Combine(stateRoot, "logs", "istdocai-.log"), ["rollingInterval"] = "Day", ["retainedFileCountLimit"] = 7 } }));
        return root;
    }
}
