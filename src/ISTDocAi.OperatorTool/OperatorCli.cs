using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using ISTDocAi.Provisioning;

namespace ISTDocAi.OperatorTool;

public sealed class ActionNeededException(string message) : Exception(message);

public sealed record OperatorArguments(string Command, string Action, Dictionary<string, string> Values)
{
    public string Get(string name, string fallback = "") => Values.GetValueOrDefault(name, fallback);
    public bool Has(string name) => Values.ContainsKey(name);
    public string Required(string name) => Get(name).Length > 0 ? Get(name) : throw new ArgumentException("--" + name + " is required.");
    public long Long(string name, long fallback = 0) => !Has(name) ? fallback : long.TryParse(Get(name), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : throw new ArgumentException("Invalid --" + name);
    public int Int(string name, int fallback = 0) => checked((int)Long(name, fallback));
    public decimal Decimal(string name, decimal fallback) => !Has(name) ? fallback : decimal.TryParse(Get(name), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) && value is >= 0 and <= 1 ? value : throw new ArgumentException("--" + name + " must be between 0 and 1.");
    public static OperatorArguments Parse(string[] args)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"]) return new("help", "", []);
        if (args is ["--export-sql", var folder]) return new("export", "", new() { ["export-sql"] = folder });
        var command = args[0]; var start = command == "operators" ? 1 : 2;
        var action = start == 1 ? "" : args.Length > 1 ? args[1] : "";
        string[] common = ["sql-server", "database", "trust-server-certificate", "generate-sql"];
        string[] output = ["json", "csv", "pdf", "audience", "force", "company-name"];
        string[] allowed = command switch
        {
            "pages" when action is "status" or "topup" or "adjust" or "reverse" or "set-mode" or "set-warning" or "cycles" or "documents" => common.Concat(output).Concat(new[] { "consumer", "pages", "note", "reference", "request-id", "grant-id", "mode", "percent", "warning-percent", "allow-debt", "topup-pages", "top-up-pages", "confirm-blocked", "limit", "after-charge-id" }).ToArray(),
            "report" when action == "customer" => ["base-url", "kit-secrets", "grant-id", "documents", "limit", "json", "csv", "pdf", "force", "company-name"],
            "models" when action == "mapping" => common.Concat(new[] { "model-id", "document-type-code", "mapping-file" }).ToArray(),
            "models" when action == "register-bl" => common.Concat(new[] { "model-id", "test-field-accuracy", "minimum-accuracy", "min-field-confidence", "mapping-file" }).ToArray(),
            "models" when action == "register-set" => common.Concat(new[] { "bl-model-id", "bl-test-field-accuracy", "bl-minimum-accuracy", "bl-min-field-confidence", "bl-mapping-file", "manifest-model-id", "manifest-test-field-accuracy", "manifest-min-field-confidence", "manifest-mapping-file", "classifier-model-id", "classifier-test-field-accuracy", "classifier-min-confidence", "router-minimum-accuracy", "bill-class-name", "manifest-class-name", "other-class-name", "bill-shipping-line" }).ToArray(),
            "operators" => common.Concat(new[] { "operator-group", "modelteam-group", "quota-operator-group" }).ToArray(),
            _ => throw new ArgumentException("Unknown command. Run --help.")
        };
        string[] flags = ["trust-server-certificate", "json", "force", "allow-debt", "confirm-blocked", "documents"];
        var values = new Dictionary<string, string>();
        for (var i = start; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || !allowed.Contains(args[i][2..], StringComparer.Ordinal)) throw new ArgumentException("Unknown argument. Run --help.");
            var name = args[i][2..]; var value = "true";
            if (!flags.Contains(name))
            {
                if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal) || args[i].Length == 0) throw new ArgumentException("Missing --" + name + " value.");
                value = args[i];
            }
            if (!values.TryAdd(name, value)) throw new ArgumentException("Duplicate --" + name);
        }
        var result = new OperatorArguments(command, action, values);
        if (result.Has("audience") && result.Get("audience") is not ("ist" or "customer")) throw new ArgumentException("Audience must be ist or customer.");
        if (result.Has("force") && !result.Has("csv") && !result.Has("pdf")) throw new ArgumentException("Force requires an output file.");
        if (result.Has("generate-sql") && new[] { "json", "csv", "pdf", "force" }.Any(result.Has)) throw new ArgumentException("SQL generation cannot be combined with report output.");
        return result;
    }
    public PageRequest Page(string identity)
    {
        var action = Action switch { "topup" => "TopUp", "adjust" => "Adjust", "reverse" => "Reverse", "set-mode" => "SetMode", "set-warning" => "SetWarningPercent", "cycles" => "Cycles", "documents" => "Documents", _ => "Status" };
        var r = new PageRequest(action, Required("consumer"))
        {
            OperatorIdentity = identity, Pages = Long("pages"), Note = Get("note"), Reference = Get("reference"),
            RequestId = Has("request-id") ? Guid.TryParse(Get("request-id"), out var id) ? id : throw new ArgumentException("Invalid request id.") : Guid.NewGuid(),
            GrantId = Get("grant-id") == "prelude" ? 0 : Long("grant-id"), Mode = Get("mode"), Percent = Int("warning-percent", Int("percent", 10)),
            TopUpPages = Int("top-up-pages", Int("topup-pages")), AllowDebt = Has("allow-debt"), ConfirmBlocked = Has("confirm-blocked"),
            Limit = Int("limit", 100), AfterChargeId = Long("after-charge-id"), Customer = Get("audience", "customer") == "customer"
        };
        if (r.Action == "Documents" && !Has("grant-id")) throw new ArgumentException("Documents requires --grant-id (0 = prelude).");
        if (r.Action == "Cycles" && Has("grant-id") && r.GrantId <= 0) throw new ArgumentException("Cycles grant id must be positive; omit for all cycles.");
        if (Has("after-charge-id") && r.AfterChargeId <= 0) throw new ArgumentException("After-charge-id must be positive.");
        if (!r.Reading && new[] { "json", "csv", "pdf", "limit", "after-charge-id", "audience", "company-name" }.Any(Has)) throw new ArgumentException("Reporting options require cycles or documents.");
        // Refuse options that would otherwise be silently ignored by a different action.
        var specific = new Dictionary<string, string[]> { ["pages"] = ["TopUp", "Adjust"], ["grant-id"] = ["Reverse", "Cycles", "Documents"], ["mode"] = ["SetMode"], ["percent"] = ["SetWarningPercent"], ["warning-percent"] = ["SetWarningPercent"], ["topup-pages"] = ["SetMode"], ["top-up-pages"] = ["SetMode"], ["confirm-blocked"] = ["SetMode"], ["allow-debt"] = ["Adjust", "Reverse"] };
        foreach (var (key, actions) in specific) if (Has(key) && !actions.Contains(r.Action)) throw new ArgumentException("--" + key + " does not apply to this action.");
        PageSql.Validate(r); return r;
    }
    public string Models()
    {
        if (Action == "mapping") return ModelSql.Mapping(Required("model-id"), Required("document-type-code"), Has("mapping-file") ? File.ReadAllText(Get("mapping-file")) : "");
        if (Action == "register-bl") { Required("test-field-accuracy"); return ModelSql.Bl(Required("model-id"), Decimal("test-field-accuracy", 0), Decimal("minimum-accuracy", .95m), Decimal("min-field-confidence", .8m), Has("mapping-file") ? File.ReadAllText(Get("mapping-file")) : ""); }
        Required("bl-test-field-accuracy");
        return ModelSql.Set(new(Required("bl-model-id"), Decimal("bl-test-field-accuracy", 0))
        {
            BlMinimumAccuracy = Decimal("bl-minimum-accuracy", .95m), BlMinFieldConfidence = Decimal("bl-min-field-confidence", .8m), BlMappingFile = Get("bl-mapping-file"),
            ManifestModelId = Get("manifest-model-id"), ManifestTestFieldAccuracy = Has("manifest-test-field-accuracy") ? Decimal("manifest-test-field-accuracy", 0) : null,
            ManifestMinFieldConfidence = Decimal("manifest-min-field-confidence", .8m), ManifestMappingFile = Get("manifest-mapping-file"),
            ClassifierModelId = Get("classifier-model-id"), ClassifierTestFieldAccuracy = Has("classifier-test-field-accuracy") ? Decimal("classifier-test-field-accuracy", 0) : null,
            ClassifierMinConfidence = Decimal("classifier-min-confidence", .5m), RouterMinimumAccuracy = Decimal("router-minimum-accuracy", .6m),
            BillClassName = Get("bill-class-name", "bill-of-lading"), ManifestClassName = Get("manifest-class-name", "cargo-manifest"), OtherClassName = Get("other-class-name", "other"), BillShippingLine = Get("bill-shipping-line")
        });
    }
}

public static class OperatorCli
{
    public const string Help = """
        ISTDocAi.OperatorTool.exe — Windows authentication, asInvoker
        pages status|topup|adjust|reverse|set-mode|set-warning|cycles|documents --consumer CODE
          --pages N --note REASON [--reference TEXT] [--request-id GUID]
          --mode Prepaid|Unlimited [--top-up-pages N] [--confirm-blocked]
          --percent 1..50 --grant-id N|prelude [--allow-debt]
          [--limit 1..500] [--after-charge-id N]
          [--json | --csv FILE | --pdf FILE] [--audience customer|ist] [--force]
        report customer --base-url HTTPS_ORIGIN --kit-secrets XML
          [--grant-id N|prelude] [--documents] [--limit 1..500]
          [--json | --csv FILE | --pdf FILE] [--company-name NAME] [--force]
        models register-bl --model-id ID --test-field-accuracy N
          [--minimum-accuracy N] [--min-field-confidence N] [--mapping-file JSON]
        models register-set --bl-model-id ID --bl-test-field-accuracy N
          [--bl-minimum-accuracy N] [--bl-min-field-confidence N] [--bl-mapping-file JSON]
          [--manifest-model-id ID --manifest-test-field-accuracy N --manifest-mapping-file JSON]
          [--manifest-min-field-confidence N] [--router-minimum-accuracy N]
          [--classifier-model-id ID --classifier-test-field-accuracy N --classifier-min-confidence N]
          [--bill-class-name NAME --manifest-class-name NAME --other-class-name NAME --bill-shipping-line LINE]
        models mapping --model-id ID --document-type-code BL|MANIFEST [--mapping-file JSON]
        operators --operator-group DOMAIN\group [--modelteam-group DOMAIN\group] [--quota-operator-group DOMAIN\group]
        SQL commands: [--sql-server SERVER] [--database ISTDocAi] [--trust-server-certificate]
          [--generate-sql FOLDER] (no connection; existing files are refused)
        --export-sql FOLDER (unchanged operator templates; refuses overwrite)
        Enter yes only after reviewing preview/summary. Exit: 0 OK, 1 refused, 2 action needed, 3 cancelled.
        """;
    public static async Task<int> Run(string[] args, IProvisionConsole console)
    {
        if (args.FirstOrDefault() == "azure") return await AzureCli.Run(args, console);
        try { return await Execute(OperatorArguments.Parse(args), console); }
        catch (CliCancelled) { console.WriteLine("CANCELLED: No further action taken."); return 3; }
        catch (ActionNeededException e) { console.WriteLine("ACTION NEEDED: " + e.Message); return 2; }
        catch (SqlException e) { console.WriteLine(e.Number is >= 50000 and <= 51999 ? "REFUSED: " + e.Message : "REFUSED: SQL operation failed. Check permissions and deployment; details suppressed."); return 1; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or JsonException or OverflowException)
        {
            // File/XML/JSON/HTTP exceptions can contain secrets or untrusted response contents.
            console.WriteLine("REFUSED: " + (e is ArgumentException or InvalidOperationException ? e.Message : "Invalid input or file operation failed; details suppressed.")); return 1;
        }
    }
    private static async Task<int> Execute(OperatorArguments a, IProvisionConsole c)
    {
        if (a.Command == "help") { c.WriteLine(Help + Environment.NewLine + AzureCli.Help); return 0; }
        if (a.Command == "export")
        {
            WriteFiles(a.Required("export-sql"), new() { ["TopUp-Pages.sql"] = SqlResources.Bytes("TopUp-Pages.sql"), ["Reverse-TopUp.sql"] = SqlResources.Bytes("Reverse-TopUp.sql") }); return 0;
        }
        CheckOutput(a);
        if (a.Command == "report") { await Customer(a, c); return 0; }
        var identity = WindowsIdentity.GetCurrent().Name + "@" + Environment.MachineName;
        var page = a.Command == "pages" ? a.Page(identity) : null;
        // Validate and render the complete SQL before asking for a connection or opening one.
        var database = a.Get("database", "ISTDocAi");
        var batch = page is not null ? PageSql.Generate(page) : a.Command == "models" ? a.Models() : OperatorGrants.Generate(database, a.Required("operator-group"), a.Get("modelteam-group"), a.Get("quota-operator-group"));
        if (page?.Mutating == true) c.WriteLine($"RequestId: {page.RequestId} (retry with the same ID and identical parameters).");
        if (a.Has("generate-sql"))
        {
            var files = page is not null ? PageSql.Files(page) : new Dictionary<string, string> { [a.Command == "operators" ? "04-operators.sql" : a.Action == "register-set" ? "05-register-models.sql" : a.Action == "mapping" ? "05-register-model-mapping.sql" : "05-register-model.sql"] = batch };
            WriteFiles(a.Get("generate-sql"), files.ToDictionary(p => p.Key, p => Encoding.UTF8.GetBytes(p.Value))); c.WriteLine("SQL generated. Review preview/summary before IT execution."); return 0;
        }
        var server = a.Has("sql-server") ? a.Get("sql-server") : CliPrompts.Ask(c, "SQL Server");
        if (!a.Has("database")) database = CliPrompts.Ask(c, "Database", "ISTDocAi");
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database)) throw new ArgumentException("SQL Server and database required.");
        SqlRunner.ValidateNames(server, database);
        var trust = a.Has("trust-server-certificate") || CliPrompts.Ask(c, "TrustServerCertificate? yes/no", "no").Equals("yes", StringComparison.OrdinalIgnoreCase);
        if (a.Command == "operators") batch = OperatorGrants.Generate(database, a.Required("operator-group"), a.Get("modelteam-group"), a.Get("quota-operator-group"));
        await using var sql = new SqlRunner(server, database, trust);
        if (page?.Reading == true)
        {
            var report = await sql.Report(page); JsonNode? docs = null;
            if (a.Has("pdf") && (page.Action == "Documents" || page.GrantId > 0))
                docs = await Reports.Documents((take, cursor) => sql.Report(page with { Action = "Documents", Limit = take, AfterChargeId = cursor }), page.Limit, page.AfterChargeId, page.Action == "Documents" ? report : null);
            var company = a.Get("company-name");
            if (a.Has("pdf") && company.Length == 0)
            {
                var identityRows = await sql.Execute($"SELECT Name FROM dbo.Consumers WHERE Code='{page.Consumer}';");
                company = identityRows.SelectMany(x => x).SelectMany(x => x.Values).FirstOrDefault()?.ToString() ?? "";
            }
            Output(a, c, report, docs, page.Customer, page.Action == "Documents", page.GrantId > 0, company, page.Consumer); return 0;
        }
        if (page?.Mutating == true)
        {
            var preview = await sql.Execute(PageSql.Generate(page, true)); Print(c, preview);
            var cells = preview.SelectMany(s => s).SelectMany(r => r.Values).OfType<string>().ToArray();
            if (cells.Any(s => s.StartsWith("QUOTA_REFUSED=", StringComparison.Ordinal))) throw new ArgumentException("Preview refused; no change committed. See reason above.");
            var tokens = cells.Where(s => s.StartsWith("PREVIEW_TOKEN=", StringComparison.Ordinal)).Select(s => s[14..]).ToArray();
            if (tokens.Length != 1) throw new ArgumentException("Could not read preview token. No action committed.");
            if (page.ConfirmBlocked) Confirm(c, "WARNING: Prepaid with no available pages blocks the customer immediately.");
            Confirm(c, "Review Before -> After / Remaining / Status above. Preview was rolled back. Commit?");
            Print(c, await sql.Execute(PageSql.Generate(page, token: tokens[0])));
            c.WriteLine($"Committed RequestId {page.RequestId}. Replay=1 means an identical retry; no second change."); return 0;
        }
        if (page is null) { c.WriteLine(batch); Confirm(c, "Review model/operator SQL summary above. Apply to " + database + "?"); }
        Print(c, await sql.Execute(batch)); return 0;
    }
    private static async Task Customer(OperatorArguments a, IProvisionConsole c)
    {
        CustomerReports.ValidateOrigin(a.Required("base-url")); var grant = a.Get("grant-id"); var limit = a.Int("limit", 100); var documents = a.Has("documents");
        if (grant.Length > 0 && grant != "prelude" && (!long.TryParse(grant, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0) || documents && grant.Length == 0 || limit is < 1 or > 500) throw new ArgumentException("Invalid grant id or limit; documents requires a grant id.");
        using var client = new CustomerReports(a.Get("base-url"), a.Required("kit-secrets"));
        var report = await client.Fetch(grant, documents, limit); JsonNode? docs = null;
        if ((a.Has("pdf") || a.Has("csv")) && (documents || a.Has("pdf") && grant.Length > 0)) docs = await Reports.Documents((take, cursor) => client.Fetch(grant, true, take, cursor), limit, first: documents ? report : null);
        Output(a, c, report, docs, true, documents, grant.Length > 0, a.Get("company-name"), "");
    }
    private static void Output(OperatorArguments a, IProvisionConsole c, JsonNode report, JsonNode? docs, bool customer, bool documents, bool cycle, string company, string consumer)
    {
        if (!customer) Warning(c, Reports.InternalWarning + ". Estimate from Azure list prices; the Azure invoice is authoritative.");
        if (!customer && (report["totals"]?["unpricedRows"]?.GetValue<int>() > 0 || report["unpricedRows"]?.GetValue<int>() > 0)) Warning(c, "WARNING: Unpriced Azure ledger rows; reported cost excludes unknown amounts.");
        if (docs?["nextAfterChargeId"] is not null) Warning(c, "WARNING: Export capped at 10000 rows; more documents exist.");
        if (a.Has("pdf")) FileOutput(a.Get("pdf"), Reports.Pdf(report, docs, customer, documents ? "Documents" : cycle ? "Cycle" : "Cycles", company, consumer), a.Has("force"));
        if (a.Has("csv")) FileOutput(a.Get("csv"), Encoding.UTF8.GetBytes(Reports.Csv(documents ? docs ?? report : report, documents, customer)), a.Has("force"));
        if (a.Has("json") || !a.Has("pdf") && !a.Has("csv")) c.WriteLine((documents ? docs ?? report : report).ToJsonString(new JsonSerializerOptions { WriteIndented = !a.Has("json") }));
    }
    private static void Confirm(IProvisionConsole c, string message) { c.WriteLine(message); CliPrompts.Confirm(c, false); }
    private static void Warning(IProvisionConsole c, string message)
    {
        if (c is ProvisionConsole) Console.Error.WriteLine(message); else c.WriteLine(message);
    }
    private static void Print(IProvisionConsole c, object value) => c.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    public static void CheckOutput(OperatorArguments a)
    {
        if (a.Has("csv") && a.Has("pdf") && Path.GetFullPath(a.Get("csv")).Equals(Path.GetFullPath(a.Get("pdf")), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("CSV and PDF require different output paths.");
        foreach (var key in new[] { "csv", "pdf" }.Where(a.Has))
        {
            var path = Path.GetFullPath(a.Get(key));
            if (key == "pdf" && !Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Pdf path must end in .pdf.");
            if (!Directory.Exists(Path.GetDirectoryName(path)) || Directory.Exists(path) || File.Exists(path) && !a.Has("force")) throw new ArgumentException("Output directory must exist; existing files require --force.");
        }
    }
    private static void FileOutput(string path, byte[] bytes, bool force)
    {
        using var file = new FileStream(path, force ? FileMode.Create : FileMode.CreateNew, FileAccess.Write); file.Write(bytes);
    }
    public static void WriteFiles(string folder, Dictionary<string, byte[]> files)
    {
        var full = Path.GetFullPath(folder);
        if (files.Keys.Any(name => File.Exists(Path.Combine(full, name)) || Directory.Exists(Path.Combine(full, name)))) throw new ArgumentException("SQL output already exists. Reuse the reviewed files for a retry.");
        Directory.CreateDirectory(full);
        var created = new List<string>();
        try { foreach (var (name, bytes) in files) { var path = Path.Combine(full, name); using var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write); created.Add(path); f.Write(bytes); } }
        catch { foreach (var path in created) File.Delete(path); throw; }
    }
}

public static class OperatorLifetime
{
    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList([Out] uint[] processList, uint count);
    public static async Task<int> Run(string[] args)
    {
        var alone = OperatingSystem.IsWindows() && GetConsoleProcessList(new uint[2], 2) == 1;
        var console = new ProvisionConsole();
        try { return await OperatorCli.Run(args, console); }
        finally { ConsoleLifetime.Pause(alone, console); }
    }
}
