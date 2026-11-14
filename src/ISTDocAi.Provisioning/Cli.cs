using System.Text;

namespace ISTDocAi.Provisioning;

public sealed class CliRefusal(string message) : Exception(message);
public sealed class CliEnvironment(string message) : Exception(message);
public sealed class CliCancelled : Exception;

public sealed record CliArguments(string Command, IReadOnlyDictionary<string, string> Values, bool Yes, bool Help)
{
    public string? Get(string name) => Values.GetValueOrDefault(name);
    public static CliArguments Parse(string[] args, bool kit)
    {
        var command = args.FirstOrDefault() ?? "--help";
        if (command is "--help" or "-h")
        {
            if (args.Length > 1) throw new CliRefusal("Unknown arguments. Run --help.");
            return new("", new Dictionary<string, string>(), false, true);
        }
        var commands = kit ? new[] { "prepare", "activate", "status", "cancel", "pairing", "database" } : ["apply", "status"];
        if (!commands.Contains(command)) throw new CliRefusal("Unknown command. Run --help.");
        var allowed = new List<string> { "site", "path", "pool-account" };
        if (command == "prepare") allowed.AddRange(["product", "tenant", "name", "callback-url", "docai-url"]);
        if (command == "database") allowed.AddRange(["product", "tenant"]);
        if (command == "apply") allowed.AddRange(["pages", "operator", "note", "out"]);
        var values = new Dictionary<string, string>(); bool yes = false, help = false;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--yes" && !yes) { yes = true; continue; }
            if (args[i] == "--help" && !help) { help = true; continue; }
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || !allowed.Contains(args[i][2..]) || i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new CliRefusal("Unknown or incomplete argument. Run --help.");
            var key = args[i][2..];
            if (!values.TryAdd(key, args[++i])) throw new CliRefusal("Duplicate argument. Run --help.");
        }
        if (command != "database" && (values.ContainsKey("path") != values.ContainsKey("pool-account") || values.ContainsKey("path") && values.ContainsKey("site")))
            throw new CliRefusal("Use --site OR --path together with --pool-account.");
        return new(command, values, yes, help);
    }
}

public interface IProvisionConsole
{
    string? ReadLine();
    ConsoleKeyInfo ReadKey();
    void Write(string text);
    void WriteLine(string text);
}
public sealed class ProvisionConsole : IProvisionConsole
{
    public string? ReadLine() => Console.ReadLine();
    public ConsoleKeyInfo ReadKey() => Console.ReadKey(true);
    public void Write(string text) => Console.Write(text);
    public void WriteLine(string text) => Console.WriteLine(text);
}
public static class CliPrompts
{
    public static string Ask(IProvisionConsole console, string label, string? fallback = null)
    {
        console.Write(label + (fallback is null ? ": " : $" [{fallback}]: "));
        var input = console.ReadLine() ?? throw new CliCancelled();
        return input.Length == 0 ? fallback ?? "" : input;
    }
    public static void Confirm(IProvisionConsole console, bool yes)
    {
        if (!yes && !string.Equals(Ask(console, "Continue? yes/no", "no"), "yes", StringComparison.OrdinalIgnoreCase)) throw new CliCancelled();
    }
    public static string Hidden(IProvisionConsole console)
    {
        var buffer = new StringBuilder();
        while (true)
        {
            var key = console.ReadKey();
            if (key.Key == ConsoleKey.Enter) { console.WriteLine(""); return buffer.ToString(); }
            if (key.Key == ConsoleKey.Escape) throw new CliCancelled();
            if (key.Key == ConsoleKey.Backspace) { if (buffer.Length > 0) buffer.Length--; }
            else if (!char.IsControl(key.KeyChar))
            {
                if (buffer.Length >= 17000) throw new CliRefusal("Pairing code is too long. Paste it again.");
                buffer.Append(key.KeyChar);
            }
        }
    }
}
public static class ProvisionHelp
{
    public const string Kit = """
        ISTDocAi.KitSetup.exe database [--path FOLDER] [--product P] [--tenant T] [--yes] [--help]
        ISTDocAi.KitSetup.exe --export-sql FOLDER
        ISTDocAi.KitSetup.exe prepare [--site NAME | --path FOLDER --pool-account ACCOUNT]
          [--product P] [--tenant T] [--name N] [--callback-url URL] [--docai-url URL] [--yes] [--help]
        ISTDocAi.KitSetup.exe activate|status|cancel|pairing [--site NAME | --path FOLDER --pool-account ACCOUNT] [--yes] [--help]
        Database/status/help need no elevation; other commands require an administrator terminal.
        --yes skips final confirmation only. --path requires manual app restart.
        Exit: 0 success; 1 invalid/refused; 2 environment; 3 cancelled.
        """;
    public const string Server = """
        ISTDocAi.ServerSetup.exe apply [--site NAME | --path FOLDER --pool-account ACCOUNT]
          [--pages N] [--operator X] [--note Y] [--out FOLDER] [--yes] [--help]
        ISTDocAi.ServerSetup.exe status [--site NAME | --path FOLDER --pool-account ACCOUNT] [--yes] [--help]
        Pairing code is requested with hidden input; never pass it on the command line.
        apply requires an administrator terminal; status/help need no elevation.
        --yes skips final confirmation only. --path requires manual app restart.
        Exit: 0 success; 1 invalid/refused; 2 environment; 3 cancelled.
        """;
}

