using Azure.Core;
using Azure.Identity;
using System.Text.Json.Nodes;
using ISTDocAi.Provisioning;

namespace ISTDocAi.OperatorTool;

public sealed record AzureArguments(string Action, Dictionary<string, string> Values)
{
    public string Get(string key, string fallback = "") => Values.GetValueOrDefault(key, fallback);
    public bool Yes => Values.ContainsKey("yes");
    public static AzureArguments Parse(string[] args)
    {
        var identity = args.Length > 1 && args[1] == "identity";
        var action = identity && args.Length > 2 ? args[2] : args.Length > 1 ? args[1] : "";
        if (action is not ("create" or "renew" or "remove-old" or "infra") || identity == (action == "infra")) throw new CliRefusal("Unknown Azure command. Run --help.");
        string[] allowed = action == "infra" ? ["tenant", "yes"] : action == "remove-old" ? ["tenant", "client-id", "thumbprint", "yes"] : ["tenant", "client-id", "name", "env", "output", "yes"];
        var values = new Dictionary<string, string>();
        for (var n = identity ? 3 : 2; n < args.Length; n++)
        {
            if (!args[n].StartsWith("--", StringComparison.Ordinal) || !allowed.Contains(args[n][2..])) throw new CliRefusal("Unknown Azure option.");
            var key = args[n][2..]; var value = "true";
            if (key != "yes")
            {
                if (++n >= args.Length || args[n].StartsWith("--", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(args[n])) throw new CliRefusal("Missing Azure option value.");
                value = args[n];
            }
            if (!values.TryAdd(key, value)) throw new CliRefusal("Duplicate Azure option.");
        }
        if (values.TryGetValue("client-id", out var client)) AzureIdentity.GuidValue(client);
        if (action is "renew" or "remove-old" && !values.ContainsKey("client-id")) throw new CliRefusal("--client-id GUID is required.");
        if (action == "remove-old" && !values.ContainsKey("thumbprint")) throw new CliRefusal("--thumbprint HEX is required.");
        if (values.TryGetValue("env", out var environment) && environment is not ("dev" or "prod")) throw new CliRefusal("Environment must be dev or prod.");
        return new(action, values);
    }
}

public static class AzureCli
{
    public const string Help = """
        azure identity create [--tenant GUID] [--env dev|prod] [--name NAME] [--client-id GUID] [--output FOLDER] [--yes]
        azure identity renew --client-id GUID [--tenant GUID] [--name NAME] [--output FOLDER] [--yes]
        azure identity remove-old --client-id GUID --thumbprint HEX [--tenant GUID] [--yes]
        azure infra [--tenant GUID] [--yes]
        Browser sign-in; app-registration and role-assignment permissions required. No Azure CLI required.
        --yes skips only the final confirmation; all input, password checks and resource-group confirmation remain.
        """;
    public static async Task<int> Run(string[] args, IProvisionConsole console)
    {
        try
        {
            var arguments = AzureArguments.Parse(args);
            var tenant = AzureIdentity.GuidValue(arguments.Get("tenant", "").Length > 0 ? arguments.Get("tenant") : CliPrompts.Ask(console, "Tenant GUID"));
            var credential = new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions { TenantId = tenant });
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(40));
            var account = await credential.AuthenticateAsync(new TokenRequestContext(["https://graph.microsoft.com/.default"]), deadline.Token);
            if (!string.Equals(account.TenantId, tenant, StringComparison.OrdinalIgnoreCase)) throw new CliRefusal("Signed-in tenant differs from requested tenant.");
            console.WriteLine("Signed in: " + account.Username + "; tenant: " + account.TenantId);
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(90) };
            await Execute(arguments, console, new AzureRest(http, credential), new AzureFiles(), tenant, deadline.Token);
            return 0;
        }
        catch (CliCancelled) { console.WriteLine("Cancelled. Inspect Azure state if an earlier step completed."); return 3; }
        catch (CliRefusal e) { console.WriteLine("REFUSED: " + e.Message); return 1; }
        catch (CliEnvironment e) { console.WriteLine("ACTION NEEDED: " + e.Message); return 2; }
        catch (Exception) { console.WriteLine("ACTION NEEDED: Azure workflow interrupted; details suppressed to protect secrets. Inspect Azure and exported files before retrying."); return 2; }
    }
    // Run authenticates and displays account/tenant before entering this fakeable workflow.
    public static async Task Execute(AzureArguments a, IProvisionConsole c, AzureRest rest, AzureFiles files, string tenant, CancellationToken ct = default)
    {
        if (a.Action == "infra") { await AzureInfra.Execute(a, c, rest, files, ct); return; }
        var identity = new AzureIdentity(rest); JsonObject? app = null;
        var client = a.Get("client-id");
        if (client.Length > 0) app = await identity.Application(client, ct);
        if (a.Action == "remove-old")
        {
            var keys = AzureIdentity.Without(app!, a.Get("thumbprint"));
            c.WriteLine("Remove credential " + a.Get("thumbprint") + " from client " + client + ". WARNING: do not remove the credential the server still uses. Verify the replacement on the server first.");
            CliPrompts.Confirm(c, a.Yes); await identity.Replace(app!, keys, ct); c.WriteLine("Credential removed."); return;
        }
        var name = a.Get("name", app?["displayName"]?.GetValue<string>() ?? "docai-" + a.Get("env", "dev") + "-onprem");
        using var certificate = AzureIdentity.Certificate(name);
        if (app is null) await identity.RefuseDuplicate(name, ct);
        var keysBefore = app is null ? null : AzureIdentity.Credentials(app);
        var folder = Path.GetFullPath(a.Get("output", "").Length > 0 ? a.Get("output") : CliPrompts.Ask(c, "Private PFX output folder", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DocAi", "backups")));
        var exportName = a.Action == "renew" || app is not null ? name + "-" + certificate.Thumbprint : name;
        files.Validate(folder, exportName);
        c.Write("PFX password (hidden): "); var password = CliPrompts.Hidden(c);
        c.Write("Repeat PFX password (hidden): "); var repeated = CliPrompts.Hidden(c);
        if (password.Length == 0 || password != repeated) throw new CliRefusal("PFX passwords must be nonempty and match.");
        repeated = "";
        c.WriteLine($"{(app is null ? "Create application and service principal" : "Append public certificate to existing application")} {name}; client {client}; tenant {tenant}; expires {certificate.NotAfter:u}. Private export: {Path.Combine(folder, exportName + ".pfx")}");
        CliPrompts.Confirm(c, a.Yes);
        // Export before any remote mutation so a failed export cannot strand an inaccessible remote credential.
        try { files.ExportPfx(folder, exportName, certificate, password); }
        finally { password = ""; }
        if (app is null)
        {
            app = await identity.Create(name, certificate, ct); client = AzureIdentity.GuidValue(app["appId"]!.GetValue<string>());
            c.WriteLine("Created client " + client + ". Retain the PFX; inspect this app if interrupted.");
        }
        else
        {
            keysBefore!.Add(AzureIdentity.PublicKey(certificate)); await identity.Replace(app, keysBefore, ct);
        }
        var principal = await identity.Principal(client, a.Action == "create", ct);
        files.Sidecar(folder, exportName, AzureIdentity.Sidecar(tenant, client, principal, certificate));
        c.WriteLine("Identity JSON: " + Path.Combine(folder, exportName + ".json"));
        c.WriteLine("Send the PFX to IT; send its password through a different channel.");
        if (a.Action == "renew") c.WriteLine("Next: ServerSetup identity-renew on the server; verify /health/ready and an extraction; then azure identity remove-old --client-id " + client + " --thumbprint OLD_HEX. Keep the old certificate until verified.");
    }
}
