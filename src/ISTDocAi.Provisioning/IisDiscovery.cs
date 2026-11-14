using System.Xml;
using System.Xml.Linq;
using System.Diagnostics;

namespace ISTDocAi.Provisioning;

public sealed record IisApplication(string Name, string Folder, string Pool, string Account, string? Callback)
{
    public Func<string>? AccountResolver { private get; init; }
    public string Account { get; init; } = Account;
    public IisApplication ResolveAccount() => AccountResolver is null ? this : this with { Account = AccountResolver(), AccountResolver = null };
}
public static class IisDiscovery
{
    public static XDocument Xml(string text)
    {
        using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }
    public static bool KitConfig(string xml) => Xml(xml).Root?.Element("appSettings")?.Attributes()
        .Any(a => a.Name.LocalName.Equals("file", StringComparison.OrdinalIgnoreCase) && a.Value.Replace('/', '\\').Equals("App_Data\\docai.secrets.config", StringComparison.OrdinalIgnoreCase)) == true;
    public static bool Candidate(string folder, bool kit) => Candidate(folder, kit, Directory.Exists, File.Exists, File.ReadAllText);
    public static bool Candidate(string folder, bool kit, Func<string, bool> directoryExists, Func<string, bool> fileExists, Func<string, string> read)
    {
        if (string.IsNullOrWhiteSpace(folder)) return false;
        try
        {
            if (!directoryExists(folder)) return false;
            return kit ? fileExists(Path.Combine(folder, "Web.config")) && KitConfig(read(Path.Combine(folder, "Web.config")))
                : fileExists(Path.Combine(folder, "DocAi.Api.dll"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or XmlException or ArgumentException or System.Security.SecurityException) { return false; }
    }
    public static IisApplication RequireAccount(IisApplication app)
    {
        app = app.ResolveAccount();
        return string.IsNullOrEmpty(app.Account) ? throw new CliRefusal("Unknown pool identity. Use --path and --pool-account.") : app;
    }
    public static string Identity(string type, string pool, string? user) => type switch
    {
        "ApplicationPoolIdentity" or "4" => "IIS AppPool\\" + pool,
        "NetworkService" or "2" => "NT AUTHORITY\\NETWORK SERVICE",
        "LocalService" or "1" => "NT AUTHORITY\\LOCAL SERVICE",
        "LocalSystem" or "0" => "NT AUTHORITY\\SYSTEM",
        "SpecificUser" or "3" when !string.IsNullOrWhiteSpace(user) => user,
        _ => throw new CliRefusal("Unknown pool identity. Use --path and --pool-account.")
    };
    public static string? Callback(string bindings, string appPath)
    {
        foreach (var binding in bindings.Split(','))
        {
            if (!binding.StartsWith("https/", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = binding[6..].Split(':');
            if (parts.Length < 3 || string.IsNullOrWhiteSpace(parts[^1]) || !int.TryParse(parts[^2], out var port) || port is < 1 or > 65535) continue;
            return Codes.CallbackUrl($"https://{parts[^1]}{(port == 443 ? "" : ":" + port)}{appPath.TrimEnd('/')}/api/DocAi/Webhook");
        }
        return null;
    }
    public static IReadOnlyList<IisApplication> Parse(string apps, string vdirs, string pools, string sites)
    {
        // Passwords are never copied into the returned model.
        var poolMap = Xml(pools).Descendants("APPPOOL").GroupBy(e => (string?)e.Attribute("APPPOOL.NAME") ?? "", StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g =>
        {
            var model = g.First().Descendants("processModel").FirstOrDefault();
            var type = (string?)model?.Attribute("identityType") ?? "ApplicationPoolIdentity";
            var user = (string?)model?.Attribute("userName");
            var poolName = g.Key;
            return new Func<string>(() => Identity(type, poolName, user));
        }, StringComparer.OrdinalIgnoreCase);
        var dirs = Xml(vdirs).Descendants("VDIR").GroupBy(e => (string?)e.Attribute("VDIR.NAME") ?? "", StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => Environment.ExpandEnvironmentVariables((string?)g.First().Attribute("physicalPath") ?? ""), StringComparer.OrdinalIgnoreCase);
        var siteMap = Xml(sites).Descendants("SITE").GroupBy(e => (string?)e.Attribute("SITE.NAME") ?? "", StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => (string?)g.First().Attribute("bindings") ?? "", StringComparer.OrdinalIgnoreCase);
        return Xml(apps).Descendants("APP").GroupBy(e => (string?)e.Attribute("APP.NAME") ?? "", StringComparer.OrdinalIgnoreCase).Select(g => g.First()).Select(e =>
        {
            var name = (string)e.Attribute("APP.NAME")!;
            var pool = (string)e.Attribute("APPPOOL.NAME")!;
            var slash = name.IndexOf('/');
            var site = slash < 0 ? name : name[..slash];
            var appPath = slash < 0 ? "/" : name[slash..];
            return new IisApplication(name, dirs.GetValueOrDefault(name.TrimEnd('/') + "/") ?? "", pool, "", Callback(siteMap.GetValueOrDefault(site) ?? "", appPath)) { AccountResolver = poolMap.GetValueOrDefault(pool) };
        }).ToArray();
    }
    public static string Run(params string[] arguments)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "inetsrv", "appcmd.exe");
        if (!File.Exists(path)) throw new CliEnvironment("IIS appcmd is missing. Install IIS management tools or use --path with --pool-account.");
        var start = new ProcessStartInfo(path) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new CliEnvironment("Cannot start appcmd. Check IIS installation.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000)) { process.Kill(); throw new CliEnvironment("IIS command timed out. Check IIS and retry."); }
        Task.WaitAll(stdout, stderr);
        if (process.ExitCode != 0) throw new CliEnvironment("IIS command failed. Check IIS configuration and permissions.");
        return stdout.Result;
    }
    public static IisApplication Select(CliArguments args, bool kit, IProvisionConsole console)
    {
        if (args.Get("path") is { } folder)
        {
            if (!Path.IsPathFullyQualified(folder)) throw new CliRefusal("Use an absolute application folder.");
            if (!Candidate(folder, kit)) throw new CliRefusal("Wrong application folder. Select the installed application.");
            console.WriteLine("Path override: restart the app manually after changes.");
            return new("Path override", folder, "", Codes.Text(args.Get("pool-account"), "pool account", 256), null);
        }
        var apps = Run("list", "app", "/xml"); var dirs = Run("list", "vdir", "/xml"); var pools = Run("list", "apppool", "/xml", "/config:*");
        string sites;
        try { sites = Run("list", "site", "/xml"); } catch (CliEnvironment) { sites = "<appcmd/>"; }
        var candidates = Parse(apps, dirs, pools, sites).Where(a => Candidate(a.Folder, kit)).ToArray();
        if (args.Get("site") is { } name) return RequireAccount(candidates.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? throw new CliRefusal("Site is not a matching application. Check --site."));
        if (candidates.Length == 0) throw new CliRefusal("No matching applications. Check IIS or use --path with --pool-account.");
        if (candidates.Length == 1)
        {
            console.WriteLine("Proposed app: " + candidates[0].Name);
            CliPrompts.Confirm(console, false);
            return RequireAccount(candidates[0]);
        }
        for (int i = 0; i < candidates.Length; i++) console.WriteLine($"{i + 1}. {candidates[i].Name} ({candidates[i].Folder})");
        if (!int.TryParse(CliPrompts.Ask(console, "App number"), out var number) || number < 1 || number > candidates.Length) throw new CliRefusal("Invalid app number. Run again and select a listed app.");
        return RequireAccount(candidates[number - 1]);
    }
}
