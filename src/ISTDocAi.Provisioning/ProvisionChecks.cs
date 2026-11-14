using System.Text.Json;
using System.Text;
using Microsoft.Data.SqlClient;

namespace ISTDocAi.Provisioning;

public sealed record ServerConfiguration(string Provider, string Path, bool WebhooksEnabled)
{
    public static ServerConfiguration Resolve(string primary, string? production, string? programData = null)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        void Add(string json)
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            void Walk(JsonElement element, string prefix)
            {
                foreach (var entry in element.EnumerateObject())
                {
                    var key = prefix + entry.Name;
                    if (entry.Value.ValueKind == JsonValueKind.Object) Walk(entry.Value, key + ":");
                    else values[key] = entry.Value.ValueKind == JsonValueKind.Null ? null : entry.Value.ToString();
                }
            }
            Walk(document.RootElement, "");
        }
        Add(primary); if (production is not null) Add(production);
        var provider = values.GetValueOrDefault("DocAi:Secrets:Provider") ?? "ProtectedFile";
        if (provider != "ProtectedFile") throw new CliRefusal("Secrets provider is not supported by this tool yet. Use ProtectedFile; ask the DocAi administrator.");
        var path = values.GetValueOrDefault("DocAi:Secrets:ProtectedFile:Path") ?? System.IO.Path.Combine(programData ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DocAi", "secrets", "webhook-secrets.json");
        if (!System.IO.Path.IsPathFullyQualified(path)) throw new CliRefusal("Protected file path must be absolute. Correct appsettings configuration.");
        var enabledText = values.GetValueOrDefault("Webhooks:Enabled");
        if (enabledText is not null && !bool.TryParse(enabledText, out _)) throw new CliRefusal("Webhooks:Enabled must be true or false. Correct appsettings configuration.");
        return new(provider, path, bool.TryParse(enabledText, out var enabled) && enabled);
    }
}
public sealed record CompanyIdentity(string Database, string? Company, string? ConsumerCode);
public sealed record IdentityDecision(bool Allowed, string Message);
public static class CompanyIdentityCheck
{
    public static string AdjustConnectionString(string original)
    {
        var builder = new SqlConnectionStringBuilder(original);
        if (!new System.Data.Common.DbConnectionStringBuilder { ConnectionString = original }.ContainsKey("Encrypt")) builder.Encrypt = SqlConnectionEncryptOption.Optional;
        builder.ConnectTimeout = 10;
        return builder.ConnectionString;
    }
    public static IdentityDecision Decide(CompanyIdentity row, string product, string tenant)
    {
        var expected = Codes.ConsumerCode(product, tenant);
        if (row.ConsumerCode is null) return new(false, $"Run db/docai/10d_docai_identity.sql on the company database with ProductCode {product} and TenantCode {tenant}.");
        if (row.ConsumerCode != expected) return new(false, "This company database is " + Codes.Text(row.ConsumerCode, "database consumer code", 30) + ". Select the correct company database.");
        return new(true, $"Company database {Codes.Text(row.Database, "database name", 128)} ({Codes.Text(row.Company ?? "unknown", "company name", 200)}) is {expected}");
    }
    public static IdentityDecision Check(string webConfig, string product, string tenant, Func<string, string, CompanyIdentity> query)
    {
        try
        {
            var entry = IisDiscovery.Xml(webConfig).Root?.Element("connectionStrings")?.Elements("add").SingleOrDefault(e => string.Equals((string?)e.Attribute("name"), "ConnectionString", StringComparison.OrdinalIgnoreCase));
            var connection = (string?)entry?.Attribute("connectionString");
            if (string.IsNullOrWhiteSpace(connection)) return new(true, "WARNING: Company connection unavailable. Ask the company DBA to verify identity; the kit checks it at runtime.");
            return Decide(query(AdjustConnectionString(connection), product), product, tenant);
        }
        catch (SqlException error) when (error.Number == 208) { return new(false, "Run `ISTDocAi.KitSetup.exe database` first"); }
        catch (SqlException error) { return new(true, $"WARNING: Company identity check failed (SQL {error.Number}). Ask the company DBA to verify identity; the kit checks it at runtime."); }
        catch (Exception) { return new(true, "WARNING: Company identity check unavailable. Ask the company DBA to verify identity; the kit checks it at runtime."); }
    }
    public static CompanyIdentity Query(string connectionString, string product)
    {
        using var connection = new SqlConnection(connectionString); connection.Open();
        using var command = connection.CreateCommand(); command.CommandTimeout = 10;
        command.CommandText = "SELECT DB_NAME(), (SELECT TOP (1) CompanyName FROM dbo.Defaults ORDER BY ID); SELECT ConsumerCode FROM dbo.DocAiIdentity WHERE ProductCode=@p";
        command.Parameters.AddWithValue("@p", product);
        using var reader = command.ExecuteReader(); reader.Read();
        var db = reader.GetString(0); var company = reader.IsDBNull(1) ? null : reader.GetString(1);
        reader.NextResult(); var code = reader.Read() ? reader.GetString(0) : null;
        return new(db, company, code);
    }
}
public sealed record CatalogResult(bool Accepted, int ExitCode, string Message);
public static class CatalogCheck
{
    public static async Task<CatalogResult> Verify(HttpClient client, string baseUrl, string apiKey, string expected)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Codes.BaseUrl(baseUrl) + "api/v1/catalog");
            request.Headers.Add("X-Api-Key", apiKey);
            using var response = await client.SendAsync(request);
            if ((int)response.StatusCode is 401 or 403) return new(false, 1, $"DocAi does not accept this key yet: has the DBA run register-consumer-{expected}.sql on the DocAi database? DocAi caches consumers for about 60 seconds; try again in a minute.");
            if ((int)response.StatusCode != 200) return new(false, 2, $"DocAi status {(int)response.StatusCode}. Ask the DocAi administrator to check service health.");
            try
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (json.RootElement.GetProperty("consumerCode").GetString() != expected) return new(false, 1, "DocAi consumer code differs. Check registration before activating.");
                return new(true, 0, "DocAi consumer verified.");
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { return new(false, 2, "Invalid catalog response. Ask the DocAi administrator to check the service version."); }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return new(false, 2, $"This server cannot reach or does not trust {baseUrl} ({e.GetType().Name}). Check service availability and certificate trust."); }
    }
}
public static class SqlOutputWriter
{
    public static byte[] Encode(string sql) => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sql)];
    public static bool Identical(byte[] existing, string sql) => existing.AsSpan().SequenceEqual(Encode(sql));
    public static bool Write(string sql, Func<byte[]?> readExisting, Action<byte[]> createNew)
    {
        bool Keep(byte[] existing)
        {
            if (Identical(existing, sql)) return false;
            throw new CliRefusal("Existing SQL file differs. Move the old file away deliberately, then retry.");
        }
        var existing = readExisting();
        if (existing is not null) return Keep(existing);
        try { createNew(Encode(sql)); return true; }
        catch (IOException)
        {
            existing = readExisting();
            if (existing is null) throw;
            return Keep(existing);
        }
    }
    public static bool Write(string path, string sql)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        return Write(sql, () => File.Exists(path) ? File.ReadAllBytes(path) : null, bytes =>
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(bytes); file.Flush(true);
        });
    }
}
