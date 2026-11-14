using System.Net;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ISTDocAi.ReportPdf;

namespace ISTDocAi.OperatorTool;

public static class Reports
{
    public const string InternalWarning = "IST internal — do not send to the customer";
    private static bool Sensitive(string name) => Regex.IsMatch(name, "usd|cost|price|azure|meter", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    // An allow-list at each DTO object boundary prevents future API fields from leaking into customer exports.
    private static readonly HashSet<string> CustomerFields = new(StringComparer.Ordinal)
    {
        "orderingPolicy", "coverage", "totals", "prelude", "cycles", "hasMoreCycles", "documents", "nextAfterChargeId", "topUpGrantId", "startAtUtc", "nextTopUpGrantId", "endAtUtc", "topUpPages", "isReversed", "recordedAvailableAfterTopUp", "openingLedgerBalance", "adjustmentPages", "reversalPages", "chargedPages", "chargedRuns", "closingLedgerBalance", "grantedPages", "ledgerBalance", "currentReservedPages", "currentAvailablePages", "chargeId", "jobId", "runNo", "pages", "chargedAtUtc", "resolvedDocumentTypeCode", "runReason", "consumerCode", "consumerName"
    };
    public static JsonNode Normalize(JsonNode node, bool customer)
    {
        if (node is JsonObject obj)
        {
            var result = new JsonObject();
            foreach (var (name, value) in obj)
            {
                var camel = char.ToLowerInvariant(name[0]) + name[1..];
                if (customer && (!CustomerFields.Contains(camel) || Sensitive(camel))) continue;
                if (result.ContainsKey(camel)) throw new ArgumentException("Duplicate reporting field.");
                result[camel] = value is null ? null : Normalize(value, customer);
            }
            return result;
        }
        if (node is JsonArray array) return new JsonArray(array.Select(x => x is null ? null : Normalize(x, customer)).ToArray());
        if (customer && node is JsonValue val && val.TryGetValue<string>(out var text)) return JsonValue.Create(Regex.Replace(text, @"\$|USD|Azure|cost|price|meter", "[omitted]", RegexOptions.IgnoreCase))!;
        return node.DeepClone();
    }
    public static JsonNode Parse(string text, bool customer) => Normalize(JsonNode.Parse(text) ?? throw new ArgumentException("Empty report."), customer);
    public static async Task<JsonNode> Documents(Func<int, long, Task<JsonNode>> fetch, int limit, long cursor = 0, JsonNode? first = null)
    {
        var rows = new JsonArray(); JsonNode page;
        do
        {
            page = first ?? await fetch(Math.Min(limit, 10000 - rows.Count), cursor); first = null;
            if (page["documents"] is not JsonArray current) throw new ArgumentException("Invalid document report.");
            foreach (var row in current) rows.Add(row?.DeepClone());
            if (rows.Count > 10000) throw new ArgumentException("Document response exceeded export cap.");
            if (page["nextAfterChargeId"] is null || rows.Count == 10000) break;
            if (!long.TryParse(page["nextAfterChargeId"]!.ToJsonString(), NumberStyles.None, CultureInfo.InvariantCulture, out var next)) throw new ArgumentException("Invalid document cursor; export stopped.");
            if (next <= cursor || current.Count == 0) throw new ArgumentException("Invalid document cursor; export stopped.");
            cursor = next;
        } while (true);
        var result = new JsonObject { ["documents"] = rows, ["nextAfterChargeId"] = page["nextAfterChargeId"]?.DeepClone(), ["topUpGrantId"] = page["topUpGrantId"]?.DeepClone() };
        if (page["unpricedRows"] is { } unpriced) result["unpricedRows"] = unpriced.DeepClone();
        return result;
    }
    public static string Csv(JsonNode report, bool documents, bool customer)
    {
        report = Normalize(report, customer);
        var rows = new List<JsonObject>();
        if (documents && report["documents"] is JsonArray docs) rows.AddRange(docs.OfType<JsonObject>());
        else if (!documents)
        {
            if (report["prelude"] is JsonObject prelude) rows.Add(prelude);
            if (report["cycles"] is JsonArray cycles) rows.AddRange(cycles.OfType<JsonObject>());
        }
        string[] empty = documents ? ["chargeId", "jobId", "runNo", "pages", "chargedAtUtc", "resolvedDocumentTypeCode", "runReason"] : ["topUpGrantId", "startAtUtc", "nextTopUpGrantId", "endAtUtc", "topUpPages", "isReversed", "recordedAvailableAfterTopUp", "openingLedgerBalance", "adjustmentPages", "reversalPages", "chargedPages", "chargedRuns", "closingLedgerBalance"];
        var headers = rows.Count == 0 ? empty.Concat(customer ? [] : new[] { "azureClassifyPages", "azureExtractPages", "azureCostUsd" }).ToArray() : rows.SelectMany(x => x.Select(p => p.Key)).Distinct(StringComparer.Ordinal).ToArray();
        // Quoting alone does not protect spreadsheet consumers from formula execution.
        static string Quote(string value) => "\"" + ((value.Length > 0 && "=+@\t\r".Contains(value[0])) ? "'" : "") + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        static string Cell(JsonNode? value) => value is JsonValue v && v.TryGetValue<string>(out var text) ? text : value?.ToJsonString() ?? "";
        return string.Join(',', headers.Select(Quote)) + "\r\n" + string.Concat(rows.Select(row => string.Join(',', headers.Select(h => Quote(Cell(row[h])))) + "\r\n"));
    }
    public static byte[] Pdf(JsonNode report, JsonNode? documents, bool customer, string kind, string company, string consumer)
    {
        foreach (var font in new[] { "arial.ttf", "arialbd.ttf" })
            if (!File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), font))) throw new ActionNeededException("Arial fonts missing. Install Windows Arial (arial.ttf and arialbd.ttf) on the rendering server.");
        var input = new JsonObject { ["report"] = Normalize(report, customer), ["documentReport"] = documents is null ? null : Normalize(documents, customer), ["companyName"] = company, ["consumerCode"] = consumer, ["truncated"] = documents?["nextAfterChargeId"] is not null };
        using var buffer = new MemoryStream(); ReportRenderer.Render(input.ToJsonString(), customer ? "Customer" : "Ist", kind, buffer); return buffer.ToArray();
    }
}

public sealed class CustomerReports : IDisposable
{
    private readonly HttpClient client;
    private readonly Uri origin;
    private readonly Func<TimeSpan, Task> delay;
    public CustomerReports(string baseUrl, string secretsPath) : this(baseUrl, ReadKey(secretsPath), new HttpClientHandler { AllowAutoRedirect = false }, t => Task.Delay(t)) { }
    // Injectable transport keeps tests entirely offline. Production always uses trusted TLS and refuses redirects.
    public CustomerReports(string baseUrl, string apiKey, HttpMessageHandler handler, Func<TimeSpan, Task> delay)
    {
        origin = ValidateOrigin(baseUrl); this.delay = delay;
        client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        try { client.DefaultRequestHeaders.Add("X-Api-Key", apiKey); client.DefaultRequestHeaders.Add("X-Culture", "en"); }
        catch { client.Dispose(); throw new ArgumentException("Invalid kit API key format; contents suppressed."); }
    }
    public static Uri ValidateOrigin(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || value.Contains('@') || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsolutePath != "/") throw new ArgumentException("BaseUrl must be an HTTPS origin with no path, credentials, query or fragment.");
        return uri;
    }
    public static string ReadKey(string path)
    {
        try
        {
            using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var xml = XDocument.Load(reader);
            if (xml.Root?.Name != "appSettings") throw new ArgumentException();
            var key = xml.Root.Elements("add").Single(n => (string?)n.Attribute("key") == "DocAi:ApiKey").Attribute("value")?.Value;
            if (string.IsNullOrWhiteSpace(key) || key.Contains('\r') || key.Contains('\n')) throw new ArgumentException();
            return key;
        }
        catch { throw new ArgumentException("Cannot read DocAi:ApiKey from kit secrets XML; contents suppressed."); }
    }
    public async Task<JsonNode> Fetch(string grant = "", bool documents = false, int limit = 100, long cursor = 0)
    {
        if (grant.Length > 0 && grant != "prelude" && (!Regex.IsMatch(grant, @"\A[0-9]+\z") || !long.TryParse(grant, out var n) || n <= 0) || documents && grant.Length == 0 || limit is < 1 or > 500 || cursor < 0) throw new ArgumentException("Invalid customer reporting parameters.");
        var path = "/api/v2/usage/topups" + (grant.Length == 0 ? "" : "/" + grant) + (documents ? "/documents?limit=" + limit + (cursor == 0 ? "" : "&afterChargeId=" + cursor) : "");
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage response;
            try { response = await client.GetAsync(new Uri(origin, path)); }
            catch { throw new InvalidOperationException("Usage request failed; check connectivity and trusted HTTPS certificate. Details suppressed."); }
            using (response)
            {
                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable && attempt < 3)
                {
                    var wait = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(1));
                    if (wait > TimeSpan.Zero) await delay(wait);
                    continue;
                }
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Usage HTTP status {(int)response.StatusCode}; response details suppressed.");
                try
                {
                    var envelope = JsonNode.Parse(await response.Content.ReadAsStringAsync());
                    if (envelope?["IsSuccess"]?.GetValue<bool>() != true || envelope["Value"] is not { } value) throw new ArgumentException();
                    return Reports.Normalize(value, true);
                }
                catch { throw new InvalidOperationException("Usage response could not be read; details suppressed."); }
            }
        }
    }
    public void Dispose() => client.Dispose();
}
