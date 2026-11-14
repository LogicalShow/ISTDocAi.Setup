using System.Text;
using Microsoft.Data.SqlClient;

namespace ISTDocAi.Provisioning;

public class ProvisionEnvironment
{
    public virtual string Read(string path) => File.ReadAllText(path);
    public virtual void Sql(string connection, string sql) => new ServerEnvironment().Sql(connection, sql);
    public virtual CompanyIdentity Identity(string connection, string product) => CompanyIdentityCheck.Query(connection, product);
}
public static class ConsumerRegistration
{
    public static bool RunNow(string connection, string sql, ProvisionEnvironment environment, IProvisionConsole console)
    {
        var target = new SqlConnectionStringBuilder(connection);
        if (!ServerSetupRunner.Agree(console, $"Run it now against {target.DataSource}/{target.InitialCatalog} with your Windows login (needs db_owner, or docai_operator + docai_quota_operator)?")) return false;
        target.IntegratedSecurity = true; target.Remove("User ID"); target.Remove("Password");
        environment.Sql(target.ConnectionString, sql); console.WriteLine("DONE consumer registration"); return true;
    }
}
public static class KitDatabase
{
    public static byte[] Bytes()
    {
        using var stream = typeof(KitDatabase).Assembly.GetManifestResourceStream("ISTDocAi.Provisioning.CreateAIDocumentTables.sql") ?? throw new InvalidOperationException("Embedded kit SQL missing.");
        using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray();
    }
    public static void Export(string folder)
    {
        folder = Path.GetFullPath(folder.Trim().Trim('"')); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "CreateAIDocumentTables.sql");
        if (File.Exists(path)) throw new CliRefusal("SQL export refuses to overwrite existing files.");
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); file.Write(Bytes());
    }
    public static string Connection(string webConfig) => (string?)IisDiscovery.Xml(webConfig).Root?.Element("connectionStrings")?.Elements("add")
        .SingleOrDefault(e => string.Equals((string?)e.Attribute("name"), "ConnectionString", StringComparison.OrdinalIgnoreCase))?.Attribute("connectionString") ?? throw new CliRefusal("Kit ConnectionString is missing.");
    public static string Sql(string product, string tenant)
        => Sql(product, tenant, Encoding.UTF8.GetString(Bytes()).TrimStart('\uFEFF'));
    public static string Sql(string product, string tenant, string script)
    {
        product = Codes.Part(product, "ProductCode"); tenant = Codes.Part(tenant, "TenantCode");
        // Export is byte-identical. Runtime parameters replace only the two reviewed declarations.
        foreach (var declaration in new[] { "@ProductCode varchar(100) = 'FWD'", "@TenantCode  varchar(100) = 'UNF'" })
            if (script.Split(declaration, StringSplitOptions.None).Length != 2)
                throw new CliRefusal("Embedded kit SQL format changed; update KitSetup.");
        return script.Replace("@ProductCode varchar(100) = 'FWD'", "@ProductCode varchar(100) = '" + product + "'", StringComparison.Ordinal)
            .Replace("@TenantCode  varchar(100) = 'UNF'", "@TenantCode  varchar(100) = '" + tenant + "'", StringComparison.Ordinal);
    }
    public static int Run(string webConfig, string product, string tenant, bool yes, ProvisionEnvironment env, IProvisionConsole console)
    {
        var connection = CompanyIdentityCheck.AdjustConnectionString(Connection(webConfig));
        var builder = new SqlConnectionStringBuilder(connection);
        console.WriteLine("Company SQL Server: " + builder.DataSource + "; database: " + builder.InitialCatalog);
        console.WriteLine("Stop the kit site before running database. Company identity: " + Codes.ConsumerCode(product, tenant));
        CliPrompts.Confirm(console, yes);
        env.Sql(connection, Sql(product, tenant));
        var decision = CompanyIdentityCheck.Decide(env.Identity(connection, product), product, tenant);
        ProvisionRunner.ReportIdentity(decision, console, false); console.WriteLine("DONE company database"); return 0;
    }
}
