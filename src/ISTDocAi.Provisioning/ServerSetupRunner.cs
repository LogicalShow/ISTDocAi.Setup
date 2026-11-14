using System.Xml.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace ISTDocAi.Provisioning;

public static class ServerSetupRunner
{
    public const string OwnedMarker = ".istdocai-setup";
    public const string Help = """
        ISTDocAi.ServerSetup.exe package|install|update|remove|check|identity-renew [--yes] [--help]
        ISTDocAi.ServerSetup.exe apply|status [pairing options] [--yes] [--help]
        ISTDocAi.ServerSetup.exe --export-sql FOLDER
        All paths are requested at runtime. package, help and status need no elevation.
        install/update/remove/apply/check/identity-renew require an administrator terminal. --yes skips final confirmation only.
        install continues a partial journal; remove keeps database, imported certificate and secrets.
        Exit: 0 success; 1 invalid/refused; 2 environment/action needed; 3 cancelled.
        """;
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static ServerJournal? Load(ServerEnvironment env)
    {
        if (!env.Exists(env.JournalPath)) return null;
        var journal = JsonSerializer.Deserialize<ServerJournal>(env.Read(env.JournalPath)) ?? throw new CliRefusal("Invalid setup journal.");
        if (journal.Version != 1 || journal.Install is null || journal.Started.Any(s => !ServerSetupModel.InstallSteps.Contains(s))) throw new CliRefusal("Unsupported setup journal.");
        return journal;
    }
    public static async Task<int> Execute(string[] args, ServerEnvironment env, IProvisionConsole console)
    {
        try
        {
            if (args.Length == 0 || args is ["--help"] or ["-h"]) { console.WriteLine(Help); return 0; }
            if (args is ["--export-sql", var folder])
            {
                var path = Path.Combine(Path.GetFullPath(folder.Trim('"')), "app-login.sql");
                if (env.Exists(path)) throw new CliRefusal("SQL export refuses to overwrite existing files.");
                env.Write(path, ServerSetupModel.AppLoginSql("WINDOWS_DOMAIN\\APP_ACCOUNT"));
                console.WriteLine("SQL exported: replace WINDOWS_DOMAIN\\APP_ACCOUNT with the pool's Windows account; run on master with login/role permissions. DocAi creates and owns its database."); return 0;
            }
            if (args[0] is "apply" or "status")
            {
                if (args.Contains("--help")) { console.WriteLine(ProvisionHelp.Server + Environment.NewLine + Help); return 0; }
                if (!args.Contains("--site") && !args.Contains("--path"))
                {
                    ServerJournal? journal = null;
                    try { journal = Load(env); }
                    catch (UnauthorizedAccessException) when (args[0] == "status") { console.WriteLine("Private journal unavailable to this account; select the application or use --path with --pool-account."); }
                    if (journal is not null) args = [.. args, "--site", journal.Install.Site + "/"];
                }
                return await ProvisionRunner.Run(args, false, console);
            }
            if (!new[] { "package", "install", "update", "remove", "check", "identity-renew" }.Contains(args[0]) || args.Skip(1).Any(a => a is not ("--yes" or "--help")) || args.Skip(1).Distinct().Count() != args.Length - 1)
                throw new CliRefusal("Unknown or duplicate option. Run --help.");
            if (args.Contains("--help")) { console.WriteLine(Help); return 0; }
            var yes = args.Contains("--yes");
            if (args[0] == "package") return Package(env, console, yes);
            if (!env.Elevated) throw new CliEnvironment("Run from an administrator terminal.");
            if (args[0] == "check") return await Check(Load(env) ?? throw new CliRefusal("No installation journal found."), env, console);
            if (args[0] == "install" && Load(env) is { Complete: true, Removing: false }) { console.WriteLine("already installed"); return 0; }
            using var commandLock = env.Lock();
            var state = Load(env);
            if (args[0] == "install") return await Install(state, env, console, yes);
            if (state is null) throw new CliRefusal("Not installed by this tool.");
            if (args[0] == "identity-renew") return await IdentityRenew(state, env, console, yes);
            if (args[0] == "remove") return Remove(state, env, console, yes);
            return await Update(state, env, console, yes);
        }
        catch (SqlException e) when (e.Number is >= 50000 and <= 51999) { console.WriteLine("REFUSED: " + e.Message); return 1; }
        catch (CliRefusal e) { console.WriteLine(e.Message); return 1; }
        catch (CliCancelled) { console.WriteLine("Cancelled."); return 3; }
        catch (CliEnvironment e) { console.WriteLine("ACTION NEEDED: " + e.Message); return 2; }
        catch (UnauthorizedAccessException) { console.WriteLine("Run from an administrator terminal."); return 2; }
        catch (Exception) { console.WriteLine("ACTION NEEDED: Setup failed; details suppressed to protect secrets. Check permissions and configuration; inspect the journal before rerunning."); return 2; }
    }
    private static string AskPath(IProvisionConsole c, string label, string? fallback = null) => Path.GetFullPath(CliPrompts.Ask(c, label, fallback).Trim().Trim('"'));
    private static JsonObject Object(ServerEnvironment env, string path) => JsonNode.Parse(env.Read(path), null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();
    private static ServerSettings Settings(ServerEnvironment env, string folder)
    {
        if (!env.Exists(Path.Combine(folder, "app", "ISTDocAi.UI.Server.dll"))) throw new CliRefusal("Package app/ISTDocAi.UI.Server.dll is missing.");
        var settings = JsonSerializer.Deserialize<ServerSettings>(env.Read(Path.Combine(folder, "server-settings.json")), Json) ?? throw new CliRefusal("server-settings.json is missing or invalid.");
        ServerSetupModel.ValidateIdentity(settings.TenantId, settings.ClientId, settings.Thumbprint, settings.NotAfter);
        ServerSetupModel.Endpoint(settings.BlobEndpoint); ServerSetupModel.Endpoint(settings.DocumentIntelligenceEndpoint);
        RejectDevelopment(env, folder); return settings;
    }
    private static void RejectDevelopment(ServerEnvironment env, string folder)
    {
        foreach (var name in new[] { "appsettings.Development.json", "appsettings.Local.json" })
            if (env.Exists(Path.Combine(folder, "app", name))) throw new CliRefusal("Development/Local configuration must not be packaged.");
        foreach (var path in env.EnumerateFiles(Path.Combine(folder, "app")))
        {
            if (new[] { ".pfx", ".p12", ".key" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) throw new CliRefusal("Private key material must not be packaged.");
            if (!Path.GetFileName(path).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            using var document = JsonDocument.Parse(env.Read(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            void Check(JsonElement node)
            {
                if (node.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in node.EnumerateObject())
                    {
                        if (p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(p.Value.GetString()))
                        {
                            if (Regex.IsMatch(p.Name, "password|apikey|clientsecret|webhooksecret[12]$|sharedaccesssignature", RegexOptions.IgnoreCase) ||
                                Regex.IsMatch(p.Value.GetString()!, @"(?i)(password|pwd|accountkey|sharedaccesssignature)\s*=")) throw new CliRefusal("Secret-bearing application settings must not be packaged.");
                        }
                        Check(p.Value);
                    }
                }
                else if (node.ValueKind == JsonValueKind.Array) foreach (var item in node.EnumerateArray()) Check(item);
            }
            Check(document.RootElement);
        }
    }
    private static string PackageFolder(ServerEnvironment env, IProvisionConsole c) => AskPath(c, "Package folder", env.Exists(Path.Combine(env.ExecutableFolder, "app", "ISTDocAi.UI.Server.dll")) && env.Exists(Path.Combine(env.ExecutableFolder, "server-settings.json")) ? env.ExecutableFolder : null);
    private static int Package(ServerEnvironment env, IProvisionConsole c, bool yes)
    {
        var source = AskPath(c, "ISTDocAi folder");
        if (!env.Exists(Path.Combine(source, "ISTDocAi.sln")) || !env.Exists(Path.Combine(source, "ISTDocAi.UI", "ISTDocAi.UI.Server"))) throw new CliRefusal("ISTDocAi.sln and ISTDocAi.UI/ISTDocAi.UI.Server are required.");
        var output = AskPath(c, "Output folder (must not exist)");
        if (env.Exists(output)) throw new CliRefusal("Output folder already exists.");
        var identity = Object(env, AskPath(c, "Identity JSON path"));
        var tenant = identity["tenantId"]?.GetValue<string>() ?? ""; var client = identity["clientId"]?.GetValue<string>() ?? "";
        var thumb = identity["thumbprint"]?.GetValue<string>() ?? "";
        if (!DateTimeOffset.TryParse(identity["notAfter"]?.GetValue<string>(), out var expiry)) throw new CliRefusal("Identity notAfter is required.");
        ServerSetupModel.ValidateIdentity(tenant, client, thumb, expiry);
        var infraPath = CliPrompts.Ask(c, "Infra outputs JSON path (blank to enter endpoints)").Trim().Trim('"');
        var infra = infraPath.Length == 0 ? new JsonObject() : Object(env, Path.GetFullPath(infraPath));
        var blob = ServerSetupModel.Endpoint(infra["blobEndpoint"]?["value"]?.GetValue<string>() ?? CliPrompts.Ask(c, "Blob account URL"));
        var di = ServerSetupModel.Endpoint(infra["documentIntelligenceEndpoint"]?["value"]?.GetValue<string>() ?? CliPrompts.Ask(c, "Document Intelligence endpoint"));
        c.WriteLine("Publish framework-dependent Release app to " + output); CliPrompts.Confirm(c, yes);
        env.CreateFolder(output);
        if (env.Process("dotnet", "publish", Path.Combine(source, "ISTDocAi.UI", "ISTDocAi.UI.Server"), "-c", "Release", "--self-contained", "false", "-p:BuildClient=false", "-o", Path.Combine(output, "app")).Code != 0)
            throw new CliEnvironment("dotnet publish failed. Inspect build configuration; choose a new output folder for retry.");
        RejectDevelopment(env, output);
        var settings = new ServerSettings(tenant, client, thumb, expiry, blob, di, typeof(ServerSetupRunner).Assembly.GetName().Version?.ToString() ?? "1", DateTimeOffset.UtcNow);
        env.Write(Path.Combine(output, "server-settings.json"), JsonSerializer.Serialize(settings, Json));
        env.CopyExecutable(Path.Combine(output, "ISTDocAi.ServerSetup.exe"));
        c.WriteLine("DONE package (non-secret). Deliver the identity PFX and its password separately."); return 0;
    }
    private static void Prerequisites(ServerEnvironment env, IProvisionConsole c)
    {
        var missing = env.MissingFeatures();
        if (missing.Length > 0)
        {
            c.WriteLine("Missing IIS features: " + string.Join(", ", missing));
            if (!Agree(c, "Enable these IIS features with dism.exe?")) throw new CliEnvironment("Install the missing IIS features, then rerun.");
            env.EnableFeatures(missing);
        }
        if (!env.HostingReady()) throw new CliEnvironment("Install the .NET 10 Hosting Bundle (ASP.NET Core Module V2 and Microsoft.AspNetCore.App 10.x): https://dotnet.microsoft.com/download/dotnet/10.0 then rerun.");
    }
    public static bool Agree(IProvisionConsole c, string label) => CliPrompts.Ask(c, label + " yes/no", "no").Equals("yes", StringComparison.OrdinalIgnoreCase);
    private static ServerInstall Questions(ServerEnvironment env, IProvisionConsole c, string package, ServerSettings settings)
    {
        var site = CliPrompts.Ask(c, "Site and pool name", "ISTDocAi");
        if (!Regex.IsMatch(site, @"\A[A-Za-z][A-Za-z0-9_.-]{0,63}\z")) throw new CliRefusal("Invalid site name.");
        var host = CliPrompts.Ask(c, "HTTPS host name (blank for no SNI)", "");
        if (host.Length > 0 && Uri.CheckHostName(host) != UriHostNameType.Dns) throw new CliRefusal("Invalid DNS host name.");
        if (!int.TryParse(CliPrompts.Ask(c, "HTTPS port", "443"), out var port) || port is < 1 or > 65535) throw new CliRefusal("Invalid HTTPS port.");
        var certificates = env.Certificates();
        for (var n = 0; n < certificates.Count; n++) c.WriteLine($"{n + 1}. {certificates[n].Subject} | {certificates[n].Expires:u} | {certificates[n].Thumbprint}");
        if (certificates.Count == 0) throw new CliEnvironment("No valid LocalMachine/My HTTPS certificate with private key and Server Authentication EKU.");
        if (!int.TryParse(CliPrompts.Ask(c, "HTTPS certificate number", "1"), out var selected) || selected < 1 || selected > certificates.Count) throw new CliRefusal("Invalid certificate selection.");
        var folder = AskPath(c, "Install folder", @"C:\inetpub\ISTDocAi");
        c.WriteLine("A remote SQL Server needs a domain account for the application pool.");
        var account = CliPrompts.Ask(c, "Pool identity: ApplicationPoolIdentity or DOMAIN\\account", "ApplicationPoolIdentity");
        if (account == "ApplicationPoolIdentity") account = "IIS APPPOOL\\" + site;
        _ = ServerSetupModel.AppLoginSql(account);
        var server = CliPrompts.Ask(c, "SQL Server"); if (string.IsNullOrWhiteSpace(server)) throw new CliRefusal("SQL Server is required.");
        var database = CliPrompts.Ask(c, "Database name", "ISTDocAi");
        if (!Regex.IsMatch(database, @"\A[A-Za-z][A-Za-z0-9_]{0,127}\z")) throw new CliRefusal("Invalid database name.");
        var trust = Agree(c, "TrustServerCertificate (normally no)?");
        var pfx = AskPath(c, "Identity PFX path"); var sidecar = Path.ChangeExtension(pfx, ".json");
        var tenant = settings.TenantId; var client = settings.ClientId; var thumb = settings.Thumbprint;
        if (env.Exists(sidecar))
        {
            var identity = Object(env, sidecar);
            tenant = identity["tenantId"]?.GetValue<string>() ?? ""; client = identity["clientId"]?.GetValue<string>() ?? ""; thumb = identity["thumbprint"]?.GetValue<string>() ?? "";
            if (!DateTimeOffset.TryParse(identity["notAfter"]?.GetValue<string>(), out var expiry)) throw new CliRefusal("Identity notAfter is required.");
            ServerSetupModel.ValidateIdentity(tenant, client, thumb, expiry);
        }
        return new(package, site, host, port, certificates[selected - 1].Thumbprint, folder, account, server, database, trust, tenant, client, thumb, pfx);
    }
    private static string Password(IProvisionConsole c, string label) { c.Write(label + ": "); return CliPrompts.Hidden(c); }
    private static void Step(ServerJournal j, string step, ServerEnvironment env, Action action)
    {
        if (j.Done.Contains(step)) return;
        if (!j.Started.Contains(step)) { j.Started.Add(step); env.Save(j); }
        action(); j.Done.Add(step); env.Save(j);
    }
    private static void RefuseUnowned(ServerJournal j, ServerEnvironment env)
    {
        var i = j.Install;
        foreach (var type in new[] { "site", "apppool" })
            if (!j.Started.Contains(type == "site" ? "site" : "pool") && env.ResourceExists(type, i.Site)) throw new CliRefusal("Unowned " + type + " exists; refusing adoption.");
        if (!j.Started.Contains("files") && env.Exists(i.Folder)) throw new CliRefusal("Unowned install folder exists; refusing adoption.");
        if (!j.Started.Contains("binding") && env.BindingExists(i)) throw new CliRefusal("Unowned HTTP SSL binding exists; refusing adoption.");
        if (!j.Started.Contains("site") && env.IisBindingExists(i)) throw new CliRefusal("Unowned IIS binding exists; refusing adoption.");
        if (!j.Started.Contains("login") && SqlAction(i, () => env.LoginExists(i))) throw new CliRefusal("Unowned SQL login exists; refusing adoption.");
        // Existing state folders may belong to another installation; resetting their ACL would lock it out.
        // A marker written by this tool (inside the admin-only state root) lets a reinstall after `remove` reuse them.
        foreach (var name in new[] { "secrets", "logs" })
            if (!j.Started.Contains(name) && env.Exists(Path.Combine(env.StateRoot, name)) && !env.Exists(Path.Combine(env.StateRoot, name, OwnedMarker)))
                throw new CliRefusal("Unowned " + name + " folder exists under " + env.StateRoot + "; refusing adoption.");
    }
    private static async Task<int> Install(ServerJournal? j, ServerEnvironment env, IProvisionConsole c, bool yes)
    {
        if (j?.Removing == true) throw new CliRefusal("Removal is incomplete. Rerun remove.");
        if (j?.Complete == true) { c.WriteLine("already installed"); return 0; }
        Prerequisites(env, c);
        var package = j?.Install.Package ?? PackageFolder(env, c); var settings = Settings(env, package);
        j ??= new ServerJournal { Install = Questions(env, c, package, settings) }; var i = j.Install;
        env.Plain(i.Folder); env.Plain(env.StateRoot); RefuseUnowned(j, env);
        string pfxPassword = "", poolPassword = "";
        if (!j.Done.Contains("certificate") && env.CertificatePresent(i.Thumbprint))
        {
            // Re-importing would bind the certificate to a new key container and break apps that read the old key.
            j.CertificatePreexisting = true;
            if (!j.Started.Contains("certificate")) j.Started.Add("certificate");
            j.Done.Add("certificate");
            c.WriteLine("OK identity certificate already in LocalMachine\\My; it will not be imported again.");
        }
        if (!j.Done.Contains("certificate"))
        {
            pfxPassword = Password(c, "PFX password");
            if (!env.ValidatePfx(i.Pfx, pfxPassword).Equals(i.Thumbprint, StringComparison.OrdinalIgnoreCase)) throw new CliRefusal("PFX does not match the identity thumbprint.");
        }
        if (!j.Done.Contains("pool") && !i.Account.StartsWith("IIS APPPOOL\\", StringComparison.OrdinalIgnoreCase)) poolPassword = Password(c, "Domain account password");
        c.WriteLine($"Install site/pool {i.Site}, HTTPS {i.Host}:{i.Port}, folder {i.Folder}, SQL {i.SqlServer}/{i.Database}, account {i.Account}");
        CliPrompts.Confirm(c, yes); env.Save(j);
        Step(j, "certificate", env, () => env.ImportPfx(i.Pfx, pfxPassword));
        Step(j, "pool", env, () =>
        {
            if (!env.ResourceExists("apppool", i.Site)) env.Iis("add", "apppool", "/name:" + i.Site, "/autoStart:false");
            env.Iis("set", "apppool", i.Site, "/managedRuntimeVersion:", "/startMode:AlwaysRunning", "/processModel.idleTimeout:00:00:00", "/processModel.shutdownTimeLimit:00:03:30");
            if (i.Account.StartsWith("IIS APPPOOL\\", StringComparison.OrdinalIgnoreCase))
                env.Iis("set", "apppool", i.Site, "/processModel.identityType:ApplicationPoolIdentity");
            if (!i.Account.StartsWith("IIS APPPOOL\\", StringComparison.OrdinalIgnoreCase))
            {
                try { env.SetPoolIdentity(i.Site, i.Account, poolPassword); }
                finally { poolPassword = ""; }
            }
            var variables = env.Iis("list", "apppool", i.Site, "/config:*");
            if (!variables.Contains("ASPNETCORE_ENVIRONMENT", StringComparison.Ordinal)) env.Iis("set", "config", "-section:system.applicationHost/applicationPools", $"/+[name='{i.Site}'].environmentVariables.[name='ASPNETCORE_ENVIRONMENT',value='Production']", "/commit:apphost");
        });
        Step(j, "key-read", env, () =>
        {
            if (!env.KeyRead(i.Thumbprint, i.Account)) { j.KeyRuleAdded = true; env.Save(j); env.KeyRule(i.Thumbprint, i.Account, false); }
        });
        Step(j, "secrets", env, () => { var f = Path.Combine(env.StateRoot, "secrets"); env.FolderAcl(f, i.Account, false); env.Write(Path.Combine(f, OwnedMarker), "Created by ISTDocAi.ServerSetup. Keep this file.\r\n"); });
        Step(j, "logs", env, () => { var f = Path.Combine(env.StateRoot, "logs"); env.FolderAcl(f, i.Account, true); env.Write(Path.Combine(f, OwnedMarker), "Created by ISTDocAi.ServerSetup. Keep this file.\r\n"); });
        Step(j, "files", env, () => env.CopyApp(Path.Combine(package, "app"), i.Folder, false));
        Step(j, "configuration", env, () =>
        {
            env.Write(Path.Combine(i.Folder, "appsettings.Production.json"), ServerSetupModel.Configuration(Object(env, Path.Combine(package, "app", "appsettings.json")), i, env.StateRoot, settings).ToJsonString(Json));
            env.IsolateEnvironment(i.Folder);
        });
        Step(j, "folder-read", env, () => env.FolderAcl(i.Folder, i.Account, false));
        Step(j, "site", env, () =>
        {
            if (!env.ResourceExists("site", i.Site)) env.Iis("add", "site", "/name:" + i.Site, "/bindings:https/*:" + i.Port + ":" + i.Host, "/physicalPath:" + i.Folder, "/serverAutoStart:false");
            env.Iis("set", "app", i.Site + "/", "/applicationPool:" + i.Site, "/preloadEnabled:true");
        });
        Step(j, "binding", env, () =>
        {
            if (env.BindingMatches(i)) return;
            if (env.BindingExists(i)) throw new CliRefusal("SSL binding differs from journal. Refusing replacement.");
            if (i.Host.Length > 0) env.Iis("set", "site", i.Site, $"/bindings.[protocol='https',bindingInformation='*:{i.Port}:{i.Host}'].sslFlags:1");
            if (env.Process("netsh.exe", "http", "add", "sslcert", (i.Host.Length > 0 ? "hostnameport=" + i.Host : "ipport=0.0.0.0") + ":" + i.Port,
                "certhash=" + i.TlsThumbprint, "certstorename=MY", "appid={43ACF641-A501-4EE7-B710-196FEC4DA9A0}").Code != 0)
                throw new CliEnvironment("HTTPS binding failed. Inspect the journal and existing HTTP SSL binding before retrying.");
        });
        if (!j.Done.Contains("login"))
        {
            var sql = ServerSetupModel.AppLoginSql(i.Account);
            if (!j.Started.Contains("login")) { j.Started.Add("login"); env.Save(j); }
            var choice = CliPrompts.Ask(c, "App-login SQL: 1) run now with my Windows login (needs sysadmin or securityadmin+dbcreator); 2) DBA already ran it, continue; 3) write sql/app-login.sql for DBA and stop", "3");
            if (choice == "3")
            {
                env.Write(Path.Combine(package, "sql", "app-login.sql"), sql);
                throw new CliEnvironment("Give sql/app-login.sql to the DBA. Pool has not been started. Rerun install and choose 2 after the DBA executes it.");
            }
            if (choice is not ("1" or "2")) throw new CliRefusal("Choose 1, 2 or 3.");
            Step(j, "login", env, () =>
            {
                if (choice == "2")
                {
                    var verified = SqlAction(i, () => env.LoginDbCreator(i));
                    if (verified == false) throw new CliRefusal("App login is not a dbcreator member. Ask the DBA to run sql/app-login.sql.");
                    c.WriteLine(verified == true ? "DBA app login verified as dbcreator." : "DBA app login could not be verified with your SQL metadata permissions; continuing.");
                    j.LoginDbaConfirmed = true; env.Save(j);
                    return;
                }
                SqlAction(i, () =>
                {
                    if (!env.LoginExists(i)) { j.LoginCreated = true; env.Save(j); }
                    env.Sql(ServerSetupModel.Connection(i, "master"), sql);
                    return true;
                });
            });
        }
        Step(j, "start", env, () => { env.Iis("set", "apppool", i.Site, "/autoStart:true"); env.Iis("set", "site", i.Site, "/serverAutoStart:true"); env.Iis("start", "apppool", i.Site); env.Iis("start", "site", i.Site); });
        if (!j.Done.Contains("health"))
        {
            if (!j.Started.Contains("health")) { j.Started.Add("health"); env.Save(j); }
            if (!await Live(i, env)) throw new CliEnvironment("/health/live failed. Check HTTPS certificate trust, DNS and the server logs; normal TLS validation is required.");
            c.WriteLine("/health/live OK; /health/ready HTTP " + await env.Http(Url(i) + "/health/ready") + ". 503 is expected until consumer registration in apply.");
            j.Done.Add("health"); env.Save(j);
        }
        j.Complete = true; env.Save(j); c.WriteLine("DONE installed"); return 0;
    }
    private static T SqlAction<T>(ServerInstall i, Func<T> action)
    {
        try { return action(); }
        catch (SqlException) { throw new CliEnvironment($"Cannot connect to SQL Server '{i.SqlServer}' with your Windows login. Check the name, network/firewall and your SQL permissions, then rerun."); }
    }
    public static string Url(ServerInstall i) => "https://" + (i.Host.Length == 0 ? "localhost" : i.Host) + ":" + i.Port;
    private static async Task<bool> Live(ServerInstall i, ServerEnvironment env)
    {
        for (var attempt = 0; attempt < 15; attempt++) { if (await env.Http(Url(i) + "/health/live") == 200) return true; if (attempt < 14) await env.Delay(); }
        return false;
    }
    private static async Task<int> IdentityRenew(ServerJournal j, ServerEnvironment env, IProvisionConsole c, bool yes)
    {
        if (!j.Complete || j.Removing) throw new CliRefusal("Complete installation required before identity renewal.");
        var pfx = AskPath(c, "New identity PFX path"); env.Plain(pfx);
        var sidecar = AskPath(c, "Identity JSON path", Path.ChangeExtension(pfx, ".json")); env.Plain(sidecar);
        var identity = Object(env, sidecar);
        var tenant = identity["tenantId"]?.GetValue<string>() ?? ""; var client = identity["clientId"]?.GetValue<string>() ?? "";
        var thumb = identity["thumbprint"]?.GetValue<string>() ?? "";
        if (!DateTimeOffset.TryParse(identity["notAfter"]?.GetValue<string>(), out var expiry)) throw new CliRefusal("Identity notAfter is required.");
        ServerSetupModel.ValidateIdentity(tenant, client, thumb, expiry);
        if (!Guid.Parse(client).Equals(Guid.Parse(j.Install.ClientId)) || !Guid.Parse(tenant).Equals(Guid.Parse(j.Install.TenantId))) throw new CliRefusal("New identity tenant/client id must match the installation journal.");
        if (j.PendingIdentityThumbprint is not null && !j.PendingIdentityThumbprint.Equals(thumb, StringComparison.OrdinalIgnoreCase)) throw new CliRefusal("A different renewal is pending. Finish it before starting another.");
        c.Write("PFX password (hidden): "); var password = CliPrompts.Hidden(c);
        try
        {
            if (!env.ValidatePfx(pfx, password).Equals(thumb, StringComparison.OrdinalIgnoreCase)) throw new CliRefusal("PFX thumbprint does not match identity JSON.");
            var config = Path.Combine(j.Install.Folder, "appsettings.Production.json");
            var before = env.ReadBytes(config); var after = IdentityConfiguration.Replace(before, j.Install.Thumbprint, thumb);
            c.WriteLine("Renew client " + client + " on pool " + j.Install.Site + ": " + j.Install.Thumbprint + " -> " + thumb + ". Keep old certificate; recycle and verify health.");
            CliPrompts.Confirm(c, yes);
            if (!j.Install.Thumbprint.Equals(thumb, StringComparison.OrdinalIgnoreCase) && !j.PreviousIdentityThumbprints.Contains(j.Install.Thumbprint, StringComparer.OrdinalIgnoreCase)) j.PreviousIdentityThumbprints.Add(j.Install.Thumbprint);
            j.PendingIdentityThumbprint = thumb; env.Save(j);
            if (!env.CertificatePresent(thumb)) env.ImportPfx(pfx, password);
            env.KeyRule(thumb, j.Install.Account, false);
            if (!before.AsSpan().SequenceEqual(env.ReadBytes(config))) throw new CliRefusal("Configuration changed during renewal. Inspect and rerun.");
            env.WriteBytes(config, after);
            j.Install = j.Install with { Thumbprint = thumb, Pfx = pfx }; env.Save(j);
            env.Iis("recycle", "apppool", j.Install.Site);
            if (!await Live(j.Install, env)) throw new CliEnvironment("Renewal /health/live failed. Old certificate retained; inspect configuration and journal before retrying.");
            var ready = await env.Http(Url(j.Install) + "/health/ready");
            c.WriteLine("/health/live OK; /health/ready HTTP " + ready + ". Verify an extraction before removing the old Azure credential.");
            if (ready != 200) throw new CliEnvironment("Readiness is not healthy. Keep the old Azure credential and certificate; investigate before remove-old.");
            j.PendingIdentityThumbprint = null; env.Save(j); c.WriteLine("DONE identity renewed. Old certificate retained."); return 0;
        }
        finally { password = ""; }
    }
    private static async Task<int> Update(ServerJournal j, ServerEnvironment env, IProvisionConsole c, bool yes)
    {
        if (!j.Complete || j.Removing) throw new CliRefusal("Complete installation required before update.");
        var package = PackageFolder(env, c); _ = Settings(env, package);
        c.WriteLine("Replace app files for " + j.Install.Site + "; retain production configuration and App_Data."); CliPrompts.Confirm(c, yes);
        env.Iis("stop", "apppool", j.Install.Site);
        try { env.CopyApp(Path.Combine(package, "app"), j.Install.Folder, true); env.IsolateEnvironment(j.Install.Folder); }
        finally { env.Iis("start", "apppool", j.Install.Site); }
        if (!await Live(j.Install, env)) throw new CliEnvironment("Updated app health check failed. Check TLS trust/DNS and logs.");
        c.WriteLine("DONE updated"); return 0;
    }
    private static int Remove(ServerJournal j, ServerEnvironment env, IProvisionConsole c, bool yes)
    {
        c.WriteLine("Remove journal-owned steps: " + string.Join(", ", j.Started) + ". Keep database, imported certificate and secrets folder.");
        if (j.LoginCreated) c.WriteLine("If the app login owns the retained database, transfer that database to your Windows login before dropping the app login.");
        CliPrompts.Confirm(c, yes);
        j.Removing = true; env.Save(j); var i = j.Install;
        void RemoveStep(string name, Action action)
        {
            if (!j.Started.Contains(name) || j.Removed.Contains(name)) return;
            // Started is the durable write-ahead intent; Removed makes interrupted removal resumable.
            action(); j.Removed.Add(name); env.Save(j);
        }
        RemoveStep("site", () => { if (env.ResourceExists("site", i.Site)) env.Iis("delete", "site", i.Site); });
        RemoveStep("binding", () => { if (!env.BindingExists(i)) return; if (!env.BindingMatches(i)) throw new CliRefusal("SSL binding differs from journal; refusing removal."); var result = env.Process("netsh.exe", "http", "delete", "sslcert", (i.Host.Length > 0 ? "hostnameport=" + i.Host : "ipport=0.0.0.0") + ":" + i.Port); if (result.Code is not (0 or 2)) throw new CliEnvironment("Could not remove owned SSL binding."); });
        RemoveStep("pool", () => { if (env.ResourceExists("apppool", i.Site)) env.Iis("delete", "apppool", i.Site); });
        RemoveStep("key-read", () => { if (j.KeyRuleAdded) env.KeyRule(i.Thumbprint, i.Account, true); });
        RemoveStep("login", () =>
        {
            if (!j.LoginCreated) return;
            _ = ServerSetupModel.AppLoginSql(i.Account);
            if (!Regex.IsMatch(i.Database, @"\A[A-Za-z][A-Za-z0-9_]{0,127}\z")) throw new CliRefusal("Invalid journal database.");
            env.Sql(ServerSetupModel.Connection(i, "master"), $"""
                IF EXISTS (SELECT 1 FROM sys.databases WHERE name=N'{i.Database}' AND owner_sid=SUSER_SID(N'{i.Account}'))
                BEGIN
                    DECLARE @owner sysname=ORIGINAL_LOGIN();
                    DECLARE @sql nvarchar(max)=N'ALTER AUTHORIZATION ON DATABASE::[{i.Database}] TO '+QUOTENAME(@owner);
                    EXEC sys.sp_executesql @sql;
                END;
                IF SUSER_ID(N'{i.Account}') IS NOT NULL DROP LOGIN [{i.Account}];
                """);
        });
        RemoveStep("files", () => env.DeleteFolder(i.Folder));
        env.DeleteJournal(); c.WriteLine("DONE removed. Database, imported certificate, logs and secrets folder retained."); return 0;
    }
    private static async Task<int> Check(ServerJournal j, ServerEnvironment env, IProvisionConsole c)
    {
        var i = j.Install;
        var pool = env.Iis("list", "apppool", i.Site, "/text:state").Trim(); var site = env.Iis("list", "site", i.Site, "/text:state").Trim();
        c.WriteLine("Pool: " + pool); c.WriteLine("Site: " + site);
        var live = await env.Http(Url(i) + "/health/live"); var ready = await env.Http(Url(i) + "/health/ready");
        c.WriteLine($"/health/live HTTP {live}; /health/ready HTTP {ready}");
        var expiry = env.CertificateExpiry(i.Thumbprint); c.WriteLine("Identity certificate expiry: " + expiry.ToString("u"));
        if (expiry < DateTimeOffset.UtcNow.AddDays(30)) c.WriteLine("WARNING: Identity certificate expires in fewer than 30 days.");
        var secretsPath = Path.Combine(env.StateRoot, "secrets", "webhook-secrets.json");
        var secretsCreated = env.Exists(secretsPath);
        var key = env.KeyRead(i.Thumbprint, i.Account); var acl = secretsCreated && env.SecretsAcl(secretsPath, i.Account);
        c.WriteLine("Private-key Read: " + key + "; secrets file: " + (secretsCreated ? "ACL " + (acl ? "OK" : "WRONG") : "not created yet (apply creates it)"));
        if (live != 200) c.WriteLine("Check TLS trust/DNS and server logs. Normal TLS validation is required.");
        return pool == "Started" && site == "Started" && live == 200 && ready == 200 && key && (!secretsCreated || acl) && expiry > DateTimeOffset.UtcNow ? 0 : 2;
    }
}
