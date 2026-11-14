using OperatorReports = ISTDocAi.OperatorTool.Reports;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ISTDocAi.OperatorTool;
using ISTDocAi.Provisioning;
using Microsoft.Data.SqlClient;
using NUnit.Framework;
using UglyToad.PdfPig;

namespace ISTDocAi.Setup.Tests;

[Category("Pure")]
public sealed class OperatorPureTests
{
    internal static PageRequest Request(string action = "TopUp") => new(action, "TEST") { Pages = 10, Note = "operator's reason", OperatorIdentity = @"IST\operator@SERVER", RequestId = Guid.Parse("f2ab7cd1-1234-4567-8899-0123456789ab") };
    internal static JsonObject Mapping(string doc = "MANIFEST") => new()
    {
        ["DocumentTypeCode"] = doc, ["MappingShape"] = "ModelProfile", ["ChangeReference"] = "reviewed '$(unsafe)Ù…", ["ContainersField"] = "Containers",
        ["ContainerNumberField"] = "Number", ["SealNumberField"] = "Seal", ["BillsOfLadingField"] = "Bills",
        ["Rows"] = new JsonArray(new JsonObject { ["MapKind"] = "Field", ["CanonicalKey"] = "shipperName", ["AzureName"] = "ShipperName" })
    };
    [Test]
    public void Reference_statements_match_app_byte_for_byte() => Assert.That(SqlResources.Bytes("03_docai_statements.sql"), Is.EqualTo(File.ReadAllBytes(Path.Combine(RepositoryPaths.ISTDocAiRoot, "ISTDocAi.Infrastructure/Sql/03_docai_statements.sql"))));
    [Test]
    public void Fixed_request_SQL_bytes_match_reviewed_golden_hashes()
    {
        var files = PageSql.Files(Request());
        var preview = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(files[Request().RequestId + "-preview.sql"])));
        var apply = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(files[Request().RequestId + ".sql"])));
        Assert.That(preview + ":" + apply, Is.EqualTo("3524ADDE80CA1355B2F6D65FFEF46735657B1A68E06BEC3BC8FEB81E455F90E9:9B45EFDC04DA3BD28648BCE31C6AFC07A5FEB675EA93823445150EDA85D5D533"));
    }
    [TestCase("TopUp")][TestCase("Adjust")][TestCase("Reverse")][TestCase("SetMode")][TestCase("SetWarningPercent")]
    public void Mutations_are_stable_escaped_transactional_and_guard_preview(string action)
    {
        var r = Request(action) with { GrantId = 1, Mode = "Prepaid", TopUpPages = action == "SetMode" ? 5 : 0 };
        var files = PageSql.Files(r);
        Assert.That(files, Is.EqualTo(PageSql.Files(r)));
        Assert.That(files[r.RequestId + "-preview.sql"], Does.Contain("IF 1=1 ROLLBACK; ELSE COMMIT;").And.Contain("operator''s reason"));
        Assert.That(files[r.RequestId + ".sql"], Does.Contain("@ReviewedPreviewToken").And.Contain("@OperatorConfirmed").And.Contain("IF @token<>@ReviewedPreviewToken").And.Contain("AcquirePageQuotaLock").And.Contain("RequestId conflict"));
        Assert.That(files.Values.All(sql => !sql.Contains("$(", StringComparison.Ordinal) && !Regex.IsMatch(sql, @"\bPRINT\b")), Is.True);
    }
    public static IEnumerable<TestCaseData> BadPages()
    {
        yield return new(Request() with { Pages = 0 }); yield return new(Request() with { Pages = 1000001 }); yield return new(Request("Adjust") with { Pages = 0 });
        yield return new(Request("Adjust") with { Pages = -1000001 }); yield return new(Request("Reverse") with { GrantId = 0 });
        yield return new(Request("SetMode") with { Mode = "bad" }); yield return new(Request("SetMode") with { Mode = "" });
        yield return new(Request() with { Percent = 0 }); yield return new(Request() with { Percent = 51 });
        yield return new(Request() with { Note = "" }); yield return new(Request() with { Note = new string('x', 401) }); yield return new(Request() with { Reference = new string('x', 101) });
        yield return new(Request() with { Consumer = "bad name" }); yield return new(Request() with { Note = "bad\n" }); yield return new(Request() with { Note = "$(" });
        yield return new(Request() with { RequestId = Guid.Empty }); yield return new(Request() with { OperatorIdentity = "" });
        yield return new(Request() with { TopUpPages = 1 }); yield return new(Request("SetMode") with { Mode = "Unlimited", TopUpPages = 1 });
        yield return new(Request("Documents") with { Limit = 0 }); yield return new(Request("Documents") with { Limit = 501 }); yield return new(Request("Documents") with { AfterChargeId = -1 }); yield return new(Request("Documents") with { GrantId = -1 });
    }
    [TestCaseSource(nameof(BadPages))]
    public void Invalid_page_inputs_refused_before_IO(PageRequest r) => Assert.Throws<ArgumentException>(() => PageSql.Generate(r));
    [TestCase("Cycles", true, "S27")][TestCase("Documents", true, "S28")][TestCase("Cycles", false, "S29")][TestCase("Documents", false, "S30")]
    public void Reports_use_exact_reference_statements_and_no_mutation(string action, bool customer, string label)
    {
        var sql = PageSql.Generate(Request(action) with { Customer = customer, GrantId = 1 });
        Assert.That(sql, Does.EndWith(SqlResources.Statement(label)).And.Not.Contain("BEGIN TRANSACTION"));
        if (customer) Assert.That(sql, Does.Not.Match("UsageLedger|AzureMeterPrices|azureCostUsd"));
    }
    [Test]
    public void Mode_is_canonical_and_preview_token_validation_is_strict()
    {
        Assert.That(PageSql.Generate(Request("SetMode") with { Mode = "unlimited" }), Does.Contain("SET QuotaMode='Unlimited'"));
        Assert.Throws<ArgumentException>(() => PageSql.Generate(Request(), token: "bad"));
        var cs = new SqlConnectionStringBuilder(SqlRunner.ConnectionString("SQL01", "ISTDocAi", false));
        Assert.That(cs.IntegratedSecurity, Is.True); Assert.That(cs.Encrypt, Is.EqualTo(SqlConnectionEncryptOption.Mandatory)); Assert.That(cs.TrustServerCertificate, Is.False);
        var error = Assert.Throws<ArgumentException>(() => SqlRunner.ConnectionString("Server=SQL;Password=fixture-secret", "ISTDocAi", false));
        Assert.That(error!.Message, Does.Not.Contain("fixture-secret"));
    }
    [TestCase("http://example.org")][TestCase("https://user@example.org")][TestCase("https://@example.org")][TestCase("https://example.org/path")][TestCase("https://example.org/?q=1")][TestCase("https://example.org/#x")]
    public void Customer_origin_validation(string url) => Assert.Throws<ArgumentException>(() => CustomerReports.ValidateOrigin(url));
    [TestCase("pages topup --consumer TEST --pages 1 --note reason --unknown")]
    [TestCase("pages documents --consumer TEST")][TestCase("pages cycles --consumer TEST --grant-id 0")]
    [TestCase("pages topup --consumer TEST --pages 1 --note reason --pages 2")]
    [TestCase("pages status --consumer TEST --json")][TestCase("pages documents --consumer TEST --grant-id 0 --after-charge-id 0")]
    [TestCase("pages topup --consumer TEST --pages 1 --note reason --generate-sql out --pdf x.pdf")]
    [TestCase("models register-bl --model-id id --test-field-accuracy 1.1")]
    public void Invalid_CLI_options_are_refused(string command)
    {
        Assert.That(() => { var a = OperatorArguments.Parse(command.Split(' ')); if (a.Command == "pages") a.Page("op"); else a.Models(); }, Throws.InstanceOf<ArgumentException>());
    }
    [Test]
    public async Task Generate_SQL_needs_no_connection_and_never_overwrites()
    {
        var folder = Path.Combine(RepositoryPaths.SetupRoot, "TestResults", "operator-" + Guid.NewGuid().ToString("N"));
        try
        {
            var console = new ConsoleStub();
            string[] args = ["pages", "topup", "--consumer", "TEST", "--pages", "10", "--note", "reason", "--request-id", Request().RequestId.ToString(), "--generate-sql", folder];
            Assert.That(await OperatorCli.Run(args, console), Is.Zero);
            var original = Directory.GetFiles(folder).ToDictionary(p => Path.GetFileName(p)!, File.ReadAllBytes);
            Assert.That(await OperatorCli.Run(args, console), Is.EqualTo(1));
            foreach (var (name, bytes) in original) Assert.That(File.ReadAllBytes(Path.Combine(folder, name!)), Is.EqualTo(bytes));
            Assert.That(console.Output.ToString(), Does.Contain("RequestId:").And.Not.Contain("SQL Server:"));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Test]
    public void PDF_and_CSV_paths_refuse_overwrite_and_XML_errors_never_expose_keys()
    {
        var folder = Path.Combine(RepositoryPaths.SetupRoot, "TestResults", "outputs-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "statement.pdf"); File.WriteAllText(path, "existing");
            Assert.Throws<ArgumentException>(() => OperatorCli.CheckOutput(OperatorArguments.Parse(["pages", "cycles", "--consumer", "TEST", "--pdf", path])));
            Assert.DoesNotThrow(() => OperatorCli.CheckOutput(OperatorArguments.Parse(["pages", "cycles", "--consumer", "TEST", "--pdf", path, "--force"])));
            Assert.Throws<ArgumentException>(() => OperatorCli.CheckOutput(OperatorArguments.Parse(["pages", "cycles", "--consumer", "TEST", "--pdf", Path.Combine(folder, "bad.txt")])));
            var secret = Path.Combine(folder, "kit.xml"); File.WriteAllText(secret, "<appSettings><add key='DocAi:ApiKey' value='fixture-key'/></appSettings>");
            Assert.That(CustomerReports.ReadKey(secret), Is.EqualTo("fixture-key"));
            File.WriteAllText(secret, "<appSettings><add key='DocAi:ApiKey' value='fixture-key'/><add key='DocAi:ApiKey' value='fixture-key'/></appSettings>");
            Assert.That(Assert.Throws<ArgumentException>(() => CustomerReports.ReadKey(secret))!.Message, Does.Not.Contain("fixture-key"));
            File.WriteAllText(secret, "<!DOCTYPE x [<!ENTITY key SYSTEM 'file:///missing'>]><appSettings>&key;</appSettings>");
            Assert.Throws<ArgumentException>(() => CustomerReports.ReadKey(secret));
        }
        finally { Directory.Delete(folder, true); }
    }
    [Test]
    public void Operator_templates_export_unchanged_and_all_roles_are_least_privilege()
    {
        foreach (var name in new[] { "TopUp-Pages.sql", "Reverse-TopUp.sql" }) Assert.That(SqlResources.Bytes(name), Is.EqualTo(File.ReadAllBytes(Path.Combine(RepositoryPaths.SetupRoot, "sql/operator", name))));
        var sql = OperatorGrants.Generate("ISTDocAi", @"DOMAIN\operators", @"DOMAIN\models", @"DOMAIN\quota");
        Assert.That(sql, Does.Contain("docai_operator").And.Contain("docai_modelteam").And.Contain("docai_quota_operator").And.Not.Contain("db_owner").And.Not.Contain("dbcreator").And.Not.Contain("docai_runtime"));
        Assert.Throws<ArgumentException>(() => OperatorGrants.Generate("bad]", "group")); Assert.Throws<ArgumentException>(() => OperatorGrants.Generate("ISTDocAi", "bad'"));
        var manifest = XDocument.Load(Path.Combine(RepositoryPaths.SetupRoot, "src/ISTDocAi.OperatorTool/app.manifest"));
        Assert.That(manifest.Descendants().Single(e => e.Name.LocalName == "requestedExecutionLevel").Attribute("level")!.Value, Is.EqualTo("asInvoker"));
    }
    [Test]
    public void Mapping_reference_is_hex_SQLCMD_safe_and_compared_both_directions()
    {
        var sql = ModelSql.Binding("reviewed-manifest", "MANIFEST", Mapping().ToJsonString());
        Assert.That(sql, Does.Contain("CONVERT(nvarchar(256),0x").And.Contain("Conflicting mapping profile").And.Not.Contain("$(unsafe)").And.Not.Contain("UPDATE dbo.Mapping").And.Not.Contain("GO"));
        Assert.That(Regex.Matches(sql, " EXCEPT "), Has.Count.EqualTo(2));
        var empty = Mapping(); empty["Rows"] = new JsonArray(); Assert.DoesNotThrow(() => ModelSql.Binding("id", "MANIFEST", empty.ToJsonString()));
    }
    [TestCase("DocumentTypeCode", "BL")][TestCase("MappingShape", "LegacyFallback")][TestCase("ContainersField", "bad name")][TestCase("SealNumberField", null)][TestCase("ChangeReference", " ")][TestCase("MisspelledProperty", "wrong")]
    public void Invalid_mapping_properties_refused(string field, string? value)
    {
        var p = Mapping(); p[field] = value; Assert.Throws<ArgumentException>(() => ModelSql.Binding("id", "MANIFEST", p.ToJsonString()));
    }
    [TestCase("MapKind", "field")][TestCase("CanonicalKey", "1bad")][TestCase("AzureName", "'")]
    public void Invalid_mapping_rows_refused(string field, string value)
    {
        var p = Mapping(); p["Rows"]![0]![field] = value; Assert.Throws<ArgumentException>(() => ModelSql.Binding("id", "MANIFEST", p.ToJsonString()));
    }
    [Test]
    public void Mapping_duplicate_keys_and_large_arrays_refused()
    {
        var p = Mapping(); ((JsonArray)p["Rows"]!).Add(p["Rows"]![0]!.DeepClone()); Assert.Throws<ArgumentException>(() => ModelSql.Binding("id", "MANIFEST", p.ToJsonString()));
        p["Rows"] = new JsonArray(Enumerable.Range(0, 257).Select(_ => Mapping()["Rows"]![0]!.DeepClone()).ToArray()); Assert.Throws<ArgumentException>(() => ModelSql.Binding("id", "MANIFEST", p.ToJsonString()));
    }
    [TestCase("")][TestCase("bad id")][TestCase("bad'")][TestCase("bad;id")][TestCase("bad/id")]
    public void Invalid_model_ids_refused(string id) => Assert.Throws<ArgumentException>(() => ModelSql.Bl(id, .95m));
    [TestCase(-.1)][TestCase(1.1)]
    public void Invalid_model_bounds_refused(double value)
    {
        Assert.Throws<ArgumentException>(() => ModelSql.Bl("valid", (decimal)value)); Assert.Throws<ArgumentException>(() => ModelSql.Bl("valid", .95m, (decimal)value)); Assert.Throws<ArgumentException>(() => ModelSql.Bl("valid", .95m, .6m, (decimal)value));
    }
    [Test]
    public void Model_set_is_one_outer_transaction_and_keeps_BL_MANIFEST_AUTO_order()
    {
        var sql = ModelSql.Set(new("bl-op", .97m) { ManifestModelId = "manifest-op", ManifestTestFieldAccuracy = .92m, ClassifierModelId = "router-op", ClassifierTestFieldAccuracy = .91m });
        Assert.That(sql, Does.StartWith("-- Target: DocAi").And.Contain("docai_modelteam").And.Contain("already Active; no changes").And.Contain("SELECT ModelId, DocumentTypeCode").And.Not.Contain("\nGO\n").And.Not.Contain("$("));
        Assert.That(sql.All(c => c <= 127), Is.True);
        Assert.That(sql.IndexOf("Router models/classes registered", StringComparison.Ordinal), Is.LessThan(sql.IndexOf("bl-op activated", StringComparison.Ordinal)));
        Assert.That(sql.IndexOf("bl-op activated", StringComparison.Ordinal), Is.LessThan(sql.IndexOf("manifest-op activated", StringComparison.Ordinal)));
        Assert.That(sql.IndexOf("manifest-op activated", StringComparison.Ordinal), Is.LessThan(sql.IndexOf("router-op activated", StringComparison.Ordinal)));
        Assert.That(sql, Is.EqualTo(ModelSql.Set(new("bl-op", .97m) { ManifestModelId = "manifest-op", ManifestTestFieldAccuracy = .92m, ClassifierModelId = "router-op", ClassifierTestFieldAccuracy = .91m })));
        Assert.Throws<ArgumentException>(() => ModelSql.Set(new("bl-op", .97m) { ManifestModelId = "BL-OP", ManifestTestFieldAccuracy = .92m }));
        Assert.Throws<ArgumentException>(() => ModelSql.Set(new("bl-op", .97m) { ClassifierModelId = "router" }));
        Assert.Throws<ArgumentException>(() => ModelSql.Set(new("bl-op", .97m) { BillClassName = "OTHER" }));
    }
    internal sealed class ConsoleStub : IProvisionConsole
    {
        public StringBuilder Output { get; } = new();
        public string? ReadLine() => throw new InvalidOperationException("Unexpected prompt."); public ConsoleKeyInfo ReadKey() => throw new NotSupportedException();
        public void Write(string text) => Output.Append(text); public void WriteLine(string text) => Output.AppendLine(text);
    }
}

[Category("Pure")]
public sealed class OperatorReportTests
{
    private const string Financial = """{"totals":{"grantedPages":100,"azureCostUsd":123456789012.1234,"pricePer1000":30},"prelude":null,"cycles":[],"documents":[{"chargeId":1,"pages":2,"azureCostUsd":123456789012.1234,"azureClassifyPages":2,"runReason":"Azure USD cost price $ meter"}],"nextAfterChargeId":null} """;
    [Test]
    public void Customer_JSON_and_CSV_never_include_cost_fields_even_from_future_API_changes()
    {
        var report = OperatorReports.Parse(Financial, true);
        Assert.That(report.ToJsonString(), Does.Not.Match("(?i)usd|cost|price|azure|meter|\\$"));
        Assert.That(OperatorReports.Csv(report, true, true), Does.Not.Match("(?i)usd|cost|price|azure|meter|\\$"));
        Assert.That(OperatorReports.Csv(OperatorReports.Parse("{\"documents\":[]}", true), true, true), Does.Not.Match("(?i)usd|cost|price|azure|meter"));
    }
    [Test]
    public void IST_CSV_keeps_exact_decimal_tokens() => Assert.That(OperatorReports.Csv(OperatorReports.Parse(Financial, false), true, false), Does.Contain("123456789012.1234"));
    [TestCase(true)][TestCase(false)]
    public void PDF_audiences_and_internal_warning(bool customer)
    {
        var report = OperatorReports.Parse(Financial, customer);
        var bytes = OperatorReports.Pdf(report, report, customer, "Documents", "Company", "TEST");
        using var pdf = PdfDocument.Open(bytes); var text = string.Join(" ", pdf.GetPages().Select(p => p.Text));
        if (customer) Assert.That(text, Does.Not.Match("(?i)usd|cost|price|azure|meter|\\$"));
        else Assert.That(text, Does.Contain("donotsendtothecustomer").And.Contain("123456789012.1234"));
    }
    [Test]
    public async Task Document_exports_cap_at_10000_and_keep_cursor()
    {
        var count = 0;
        var report = await OperatorReports.Documents((take, cursor) =>
        {
            count++; return Task.FromResult<JsonNode>(new JsonObject { ["documents"] = new JsonArray(Enumerable.Range(1, take).Select(i => (JsonNode)new JsonObject { ["chargeId"] = cursor + i }).ToArray()), ["nextAfterChargeId"] = cursor + take, ["topUpGrantId"] = 1 });
        }, 500);
        Assert.That(count, Is.EqualTo(20)); Assert.That(((JsonArray)report["documents"]!).Count, Is.EqualTo(10000)); Assert.That(report["nextAfterChargeId"]!.GetValue<long>(), Is.EqualTo(10000));
    }
    [Test]
    public void Nonadvancing_cursor_is_refused() => Assert.ThrowsAsync<ArgumentException>(async () => await OperatorReports.Documents((_, _) => Task.FromResult<JsonNode>(new JsonObject { ["documents"] = new JsonArray(new JsonObject()), ["nextAfterChargeId"] = 1 }), 100, 1));
    [Test]
    public async Task HTTP_retries_429_and_503_honors_retry_after_and_uses_v2_envelope()
    {
        var handler = new StubHandler(); var waits = new List<TimeSpan>();
        using var client = new CustomerReports("https://example.org", "fixture-key", handler, t => { waits.Add(t); return Task.CompletedTask; });
        var result = await client.Fetch("prelude", true);
        Assert.That(handler.Calls, Is.EqualTo(4)); Assert.That(waits.Select(t => t.TotalSeconds), Is.EqualTo(new[] { 2d, 2d, 2d })); Assert.That(result["documents"], Is.InstanceOf<JsonArray>());
        Assert.That(handler.Paths.Single(), Is.EqualTo("/api/v2/usage/topups/prelude/documents?limit=100"));
    }
    [TestCase(302)][TestCase(401)][TestCase(500)]
    public void HTTP_errors_suppress_response_body_and_credentials(int status)
    {
        using var client = new CustomerReports("https://example.org", "fixture-key", new FailureHandler(status), _ => Task.CompletedTask);
        var error = Assert.ThrowsAsync<InvalidOperationException>(async () => await client.Fetch());
        Assert.That(error!.Message, Does.Not.Contain("fixture-key").And.Contain(status.ToString()));
    }
    private sealed class StubHandler : HttpMessageHandler
    {
        internal int Calls; internal HashSet<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.That(request.Headers.GetValues("X-Api-Key").Single(), Is.EqualTo("fixture-key")); Paths.Add(request.RequestUri!.PathAndQuery); Calls++;
            var response = new HttpResponseMessage(Calls < 4 ? Calls % 2 == 0 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.TooManyRequests : HttpStatusCode.OK);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
            response.Content = new StringContent("""{"IsSuccess":true,"Value":{"Documents":[],"NextAfterChargeId":null,"AzureCostUsd":99}}"""); return Task.FromResult(response);
        }
    }
    private sealed class FailureHandler(int status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("fixture-key") });
    }
}
