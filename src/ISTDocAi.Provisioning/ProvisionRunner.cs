using System.Security.Principal;
using System.Security.AccessControl;
using System.Text.Json;

namespace ISTDocAi.Provisioning;

public static class ProvisionRunner
{
    public static async Task<int> Run(string[] arguments, bool kit, IProvisionConsole console, ProvisionEnvironment? environment = null)
    {
        try
        {
            environment ??= new ProvisionEnvironment();
            if (kit && arguments is ["--export-sql", var folder]) { KitDatabase.Export(folder); console.WriteLine("DONE SQL exported. DBA: set ProductCode/TenantCode and run on the company database with the kit stopped."); return 0; }
            var args = CliArguments.Parse(arguments, kit);
            if (args.Help) { console.WriteLine(kit ? ProvisionHelp.Kit : ProvisionHelp.Server); return 0; }
            if (args.Command is not ("status" or "database") && (!OperatingSystem.IsWindows() || !new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)))
                throw new CliEnvironment("Administrator access required. Open an administrator terminal and run again.");
            if (kit && args.Command == "database")
            {
                var folderPath = args.Get("path") ?? CliPrompts.Ask(console, "Kit folder (contains Web.config)");
                var product = Codes.Part(Value(args, console, "product", "ProductCode", "FWD"), "ProductCode");
                var tenant = Codes.Part(Value(args, console, "tenant", "TenantCode"), "TenantCode");
                return KitDatabase.Run(environment.Read(Path.Combine(Path.GetFullPath(folderPath.Trim('"')), "Web.config")), product, tenant, args.Yes, environment, console);
            }
            var app = IisDiscovery.Select(args, kit, console);
            return kit ? await Kit(args, app, console) : Server(args, app, console, environment);
        }
        catch (Microsoft.Data.SqlClient.SqlException error) when (error.Number is >=50000 and <=51999) { console.WriteLine("REFUSED: " + error.Message); return 1; }
        catch (CliCancelled) { console.WriteLine("Cancelled. No further steps were run."); return 3; }
        catch (CliRefusal error) { console.WriteLine(error.Message); return 1; }
        catch (CliEnvironment error) { console.WriteLine(error.Message); return 2; }
        catch (UnauthorizedAccessException) { console.WriteLine("Run from an administrator terminal."); return 2; }
        catch (Exception error) when (IsOperatorError(error)) { console.WriteLine(error.Message); return 1; }
        catch (Exception) { console.WriteLine("Operation failed; details suppressed. Check file permissions, configuration and concurrent operators, then inspect status before retrying."); return 2; }
    }
    public static bool IsOperatorError(Exception error) =>
        error is InvalidOperationException or ArgumentException && error.TargetSite?.DeclaringType?.Assembly == typeof(Codes).Assembly;

    public static string SecretEntryName(string name)
    {
        try { return Codes.Text(name, "secret name", 127); }
        catch (ArgumentException) { return "<invalid entry>"; }
    }

    public static void PrintPairing(PairingData pairing, IProvisionConsole console)
    {
        console.WriteLine("+--- WARNING: SECRET PAIRING CODE ---+");
        console.WriteLine("Copy by hand to the DocAi server (for example RDP clipboard). Never e-mail or chat it.");
        console.WriteLine("Protect the pairing code like a password. It can be displayed again with pairing.");
        console.WriteLine(PairingCode.Encode(pairing));
        console.WriteLine("+--- END SECRET PAIRING CODE ---+");
    }

    public static string? PreparedDefault(KitSecretsFile file, string key) =>
        file.Get("DocAiProvision:State") == "Prepared" ? file.Get("DocAiProvision:" + key) : null;

    public static void ReportIdentity(IdentityDecision result, IProvisionConsole console, bool reportOnly)
    {
        console.WriteLine((!result.Allowed && reportOnly ? "PROBLEM: " : "") + result.Message);
        if (!result.Allowed && !reportOnly) throw new CliRefusal("Company identity refused. Correct the company database identity before retrying.");
    }

    public static PairingData DecodePastedPairing(string code) => PairingCode.Decode(code.Trim());

    private static string Value(CliArguments args, IProvisionConsole console, string key, string label, string? fallback = null) => args.Get(key) ?? CliPrompts.Ask(console, label, fallback);
    private static void Summary(IisApplication app, IProvisionConsole console)
    {
        console.WriteLine("App: " + app.Name); console.WriteLine("Folder: " + app.Folder); console.WriteLine("Pool account: " + app.Account);
    }
    private static void Identity(IisApplication app, string product, string tenant, IProvisionConsole console, bool reportOnly = false)
    {
        string webConfig;
        try { webConfig = File.ReadAllText(Path.Combine(app.Folder, "Web.config")); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            console.WriteLine("WARNING: Company configuration unreadable. Ask the company DBA to verify identity; the kit checks it at runtime.");
            return;
        }
        var result = CompanyIdentityCheck.Check(webConfig, product, tenant, CompanyIdentityCheck.Query);
        ReportIdentity(result, console, reportOnly);
    }
    private static void Acl(string path, IProvisionConsole console)
    {
        foreach (FileSystemAccessRule rule in new FileInfo(path).GetAccessControl().GetAccessRules(true, true, typeof(NTAccount)))
            console.WriteLine($"ACL: {rule.IdentityReference.Value}: {rule.AccessControlType} {rule.FileSystemRights}");
    }
    private static void KitStatus(KitSecretsFile file, string path, IisApplication app, IProvisionConsole console)
    {
        Summary(app, console);
        if (!File.Exists(path)) { console.WriteLine("not provisioned"); return; }
        foreach (var key in new[] { "DocAiProvision:State", "DocAiProvision:ConsumerCode", "DocAiProvision:BaseUrl", "DocAi:Enabled" }) console.WriteLine(key + ": " + file.Get(key));
        console.WriteLine("API key present: " + (string.IsNullOrEmpty(file.Get("DocAi:ApiKey")) ? "no" : "yes"));
        Acl(path, console);
        if (file.Get("DocAiProvision:ProductCode") is { } product && file.Get("DocAiProvision:TenantCode") is { } tenant) Identity(app, product, tenant, console, reportOnly: true);
        else console.WriteLine("Company identity unavailable: prepare with ProductCode and TenantCode first.");
    }
    private static async Task<int> Kit(CliArguments args, IisApplication app, IProvisionConsole console)
    {
        var path = Path.Combine(app.Folder, "App_Data", "docai.secrets.config");
        var file = KitSecretsFile.Load(path);
        var state = file.Get("DocAiProvision:State");
        if (args.Command == "status") { KitStatus(file, path, app, console); return 0; }
        if (args.Command == "pairing")
        {
            if (state is not ("Prepared" or "Active")) throw new CliRefusal("Pairing requires Prepared or Active state. Run prepare first.");
            var stored = file.Pairing();
            Summary(app, console);
            CliPrompts.Confirm(console, args.Yes);
            PrintPairing(stored, console);
            return 0;
        }
        string? Stored(string key) => PreparedDefault(file, key);
        if (args.Command == "prepare")
        {
            var product = Codes.Part(Value(args, console, "product", "ProductCode", Stored("ProductCode") ?? (File.Exists(Path.Combine(app.Folder, "bin", "Forwarding.MvcApp.dll")) ? "FWD" : null)), "ProductCode");
            var tenant = Codes.Part(Value(args, console, "tenant", "TenantCode", Stored("TenantCode")), "TenantCode");
            var code = Codes.ConsumerCode(product, tenant);
            var name = Codes.Name(Value(args, console, "name", "Name", Stored("Name") ?? code));
            var callback = Codes.CallbackUrl(Value(args, console, "callback-url", "Callback URL", Stored("CallbackUrl") ?? app.Callback));
            var url = Codes.BaseUrl(Value(args, console, "docai-url", "DocAi URL", Stored("BaseUrl")));
            Identity(app, product, tenant, console);
            PairingData pairing;
            try { pairing = file.Prepare(new(product, tenant, name, callback, url), DateTimeOffset.UtcNow); }
            catch (InvalidOperationException) { throw new CliRefusal("Prepare refused: existing credentials or metadata differ. For Prepared state use cancel first; legacy/Active key rotation is not supported yet."); }
            Summary(app, console);
            console.WriteLine($"Consumer: {code}\nName: {name}\nCallback URL: {callback}\nDocAi URL: {url}");
            CliPrompts.Confirm(console, args.Yes);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            file.Save(path, KitSecretsFile.CreateAcl(app.Account));
            PrintPairing(pairing, console);
            console.WriteLine("Next: run ISTDocAi.ServerSetup.exe apply on the DocAi server."); return 0;
        }
        if (args.Command == "activate" && state == "Active") { KitStatus(file, path, app, console); return 0; }
        if (state != "Prepared") throw new CliRefusal("This app is not Prepared. Run prepare first; Active key rotation is not supported yet.");
        var data = file.Pairing();
        if (args.Command == "cancel")
        {
            console.WriteLine("WARNING: If the DBA already ran registration SQL, DocAi keeps the old key. A new prepare for this code will be refused by SQL until phase-2 rotation or a DocAi admin removes the consumer.");
            Summary(app, console); CliPrompts.Confirm(console, args.Yes);
            file.Cancel(); file.Save(path, KitSecretsFile.CreateAcl(app.Account)); console.WriteLine("Prepared setup cancelled."); return 0;
        }
        var baseUrl = Codes.BaseUrl(file.Get("DocAiProvision:BaseUrl")!);
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
        var verification = await CatalogCheck.Verify(client, baseUrl, file.Get("DocAi:ApiKey")!, data.ConsumerCode);
        console.WriteLine(verification.Message); if (!verification.Accepted) return verification.ExitCode;
        Identity(app, data.ProductCode, data.TenantCode, console);
        Summary(app, console); console.WriteLine($"Consumer: {data.ConsumerCode}\nName: {data.Name}\nCallback URL: {data.CallbackUrl}\nDocAi URL: {baseUrl}");
        CliPrompts.Confirm(console, args.Yes);
        file.Activate(data.ConsumerCode); file.Save(path, KitSecretsFile.CreateAcl(app.Account));
        if (app.Pool.Length > 0)
        {
            if (!Recycle(app.Pool, console, pool => { _ = IisDiscovery.Run("recycle", "apppool", "/apppool.name:" + pool); })) return 2;
        }
        else console.WriteLine("Restart the app manually now.");
        console.WriteLine("Upload one B/L in Operations > Houses > Extract with AI and confirm the webhook arrives."); return 0;
    }
    public static bool Recycle(string pool, IProvisionConsole console, Action<string> recycle)
    {
        try { recycle(pool); return true; }
        catch (CliEnvironment)
        {
            console.WriteLine($"DocAi is activated in the settings file, but the application pool '{pool}' could not be recycled. Recycle it in IIS Manager now; until then the kit keeps the old settings.");
            return false;
        }
    }
    private static int Server(CliArguments args, IisApplication app, IProvisionConsole console, ProvisionEnvironment environment)
    {
        var primary = Path.Combine(app.Folder, "appsettings.json");
        var production = Path.Combine(app.Folder, "appsettings.Production.json");
        var config = ServerConfiguration.Resolve(File.ReadAllText(primary), File.Exists(production) ? File.ReadAllText(production) : null);
        console.WriteLine("Configuration read: " + primary);
        if (File.Exists(production)) console.WriteLine("Configuration read: " + production);
        console.WriteLine("Provider: " + config.Provider); console.WriteLine("Protected file: " + config.Path);
        console.WriteLine("Webhooks:Enabled: " + config.WebhooksEnabled);
        if (!config.WebhooksEnabled) console.WriteLine("WARNING: Callbacks will not be sent until Webhooks:Enabled is true and DocAi is restarted.");
        if (args.Command == "status")
        {
            var exists = File.Exists(config.Path); console.WriteLine("Protected file exists: " + (exists ? "yes" : "no"));
            if (exists)
            {
                using var json = JsonDocument.Parse(File.ReadAllBytes(config.Path));
                foreach (var entry in json.RootElement.EnumerateObject()) console.WriteLine("Secret name: " + SecretEntryName(entry.Name));
                Acl(config.Path, console);
            }
            return 0;
        }
        PairingData? pairing = null;
        for (int attempt = 0; attempt < 3 && pairing is null; attempt++)
        {
            console.Write("Paste pairing code (hidden; Escape cancels): ");
            try { pairing = DecodePastedPairing(CliPrompts.Hidden(console)); }
            catch (ArgumentException error) { console.WriteLine(error.Message + " Copy the complete pairing code and try again."); }
        }
        if (pairing is null) throw new CliRefusal("Pairing code refused after three attempts. Copy the complete code and retry.");
        Summary(app, console); console.WriteLine($"Consumer: {pairing.ConsumerCode}\nName: {pairing.Name}\nCallback URL: {pairing.CallbackUrl}\nSecret name: {pairing.WebhookSecretName}");
        if (!int.TryParse(Value(args, console, "pages", "Initial pages (1..1000000)"), out var pages) || pages is < 1 or > 1000000) throw new CliRefusal("Initial pages must be 1..1000000. Run again with a valid count.");
        var operatorName = Codes.Text(Value(args, console, "operator", "Operator", WindowsIdentity.GetCurrent().Name), "OperatorIdentity", 200);
        var note = Codes.Text(Value(args, console, "note", "Note", "Initial allowance at registration"), "Note", 400);
        var sql = RegisterConsumerSql.Generate(pairing, pages, operatorName, note);
        var output = Path.Combine(args.Get("out") ?? Path.Combine(AppContext.BaseDirectory, "out"), RegisterConsumerSql.FileName(pairing));
        // Preflight a conflicting script before installing a secret; writer also checks races.
        if (File.Exists(output) && !SqlOutputWriter.Identical(File.ReadAllBytes(output), sql)) throw new CliRefusal("Existing SQL file differs. Move the old file away deliberately, then retry.");
        console.WriteLine($"Initial pages: {pages}\nOperator: {operatorName}\nNote: {note}\nSQL output: {Path.GetFullPath(output)}");
        CliPrompts.Confirm(console, args.Yes);
        var installed = ProtectedSecretsFile.Install(config.Path, pairing.WebhookSecretName, pairing.WebhookSecret, app.Account);
        console.WriteLine(installed == SecretInstallResult.Installed ? "installed" : "already installed");
        console.WriteLine(SqlOutputWriter.Write(output, sql) ? "SQL written: " + output : "Identical SQL kept: " + output);
        var merged = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(File.Exists(production) ? production : primary));
        var connection = merged?["ConnectionStrings"]?["DefaultConnection"]?.GetValue<string>();
        if (connection is null && File.Exists(production)) connection = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(primary))?["ConnectionStrings"]?["DefaultConnection"]?.GetValue<string>();
        if (connection is not null)
        {
            if (ConsumerRegistration.RunNow(connection, sql, environment, console)) return 0;
        }
        console.WriteLine("Give this SQL file to the DocAi DBA. Run it only after DocAi has created and migrated its database (database.migration_completed event or /health/ready). Readiness also requires Azure/certificate health. It contains no secrets, only the key hash. After it runs, wait about a minute and run ISTDocAi.KitSetup.exe activate on the kit server.");
        return 0;
    }
}


