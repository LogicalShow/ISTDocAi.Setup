using Azure;
using Azure.Core;
using ISTDocAi.OperatorTool;
using ISTDocAi.Provisioning;
using NUnit.Framework;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;

namespace ISTDocAi.Setup.Tests;

[Category("Pure")]
public sealed class AzureTests
{
    private const string Client = "11111111-1111-1111-1111-111111111111";
    private const string ObjectId = "22222222-2222-2222-2222-222222222222";
    private sealed class Tokens : TokenCredential
    {
        public List<string> Scopes { get; } = [];
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) { Scopes.AddRange(requestContext.Scopes); return new("fake-token", DateTimeOffset.UtcNow.AddHours(1)); }
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
    private sealed class Handler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Method, string Url, string Body)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method.Method, request.RequestUri!.AbsoluteUri, body));
            Assert.That(request.Headers.Authorization?.Parameter, Is.EqualTo("fake-token"));
            return respond(request, body);
        }
    }
    private sealed class Files : AzureFiles
    {
        public JsonObject? Identity, Output;
        public string? PfxThumb, ExportName;
        public override void Validate(string folder, string name) { }
        public override void ExportPfx(string folder, string name, X509Certificate2 certificate, string password) { PfxThumb = certificate.Thumbprint; ExportName = name; }
        public override void Sidecar(string folder, string name, JsonObject identity) => Identity = identity;
        public override void ValidateOutput(string path) { }
        public override void Outputs(string path, JsonObject outputs) => Output = outputs;
    }
    private sealed class PasswordInput(params string[] passwords) : IProvisionConsole
    {
        private readonly Queue<string> secrets = new(passwords);
        private string? current;
        private int position;
        public string? ReadLine() => "no";
        public ConsoleKeyInfo ReadKey()
        {
            current ??= secrets.Dequeue();
            if (position < current.Length) return new(current[position++], ConsoleKey.A, false, false, false);
            current = null; position = 0; return new('\r', ConsoleKey.Enter, false, false, false);
        }
        public void Write(string text) { }
        public void WriteLine(string text) { }
    }
    private static HttpResponseMessage Reply(JsonNode? body = null, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(body?.ToJsonString() ?? "") };
    private static JsonObject App(JsonArray keys) => new() { ["id"] = ObjectId, ["appId"] = Client, ["displayName"] = "docai-dev-onprem", ["keyCredentials"] = keys };
    private static JsonObject Key(char thumb) => new() { ["keyId"] = Guid.NewGuid().ToString(), ["key"] = "cHVibGlj", ["customKeyIdentifier"] = Convert.ToBase64String(Convert.FromHexString(new string(thumb, 40))), ["type"] = "AsymmetricX509Cert", ["usage"] = "Verify" };
    private static AzureArguments Args(string action, bool yes = true) => new(action, new() { ["client-id"] = Client, ["output"] = @"C:\fake", ["yes"] = yes ? "true" : "" });

    [Test]
    public void Certificate_and_sidecar_match_existing_contract_without_store_import()
    {
        using var cert = AzureIdentity.Certificate("docai-dev-onprem");
        using var rsa = cert.GetRSAPrivateKey();
        using var publicOnly = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(AzureIdentity.PublicKey(cert)["key"]!.GetValue<string>()));
        Assert.Multiple(() =>
        {
            Assert.That(rsa!.KeySize, Is.EqualTo(3072)); Assert.That(publicOnly.HasPrivateKey, Is.False);
            Assert.That(cert.SignatureAlgorithm.Value, Is.EqualTo("1.2.840.113549.1.1.11"));
            Assert.That(cert.Extensions.OfType<X509BasicConstraintsExtension>().Single().CertificateAuthority, Is.False);
            Assert.That(cert.NotAfter, Is.InRange(DateTime.Now.AddMonths(12).AddMinutes(-1), DateTime.Now.AddMonths(12).AddMinutes(1)));
        });
        var json = AzureIdentity.Sidecar(Client, Client, ObjectId, cert);
        ServerSetupModel.ValidateIdentity(json["tenantId"]!.GetValue<string>(), json["clientId"]!.GetValue<string>(), json["thumbprint"]!.GetValue<string>(), DateTimeOffset.Parse(json["notAfter"]!.GetValue<string>()));
        Assert.That(json.Select(p => p.Key), Is.EquivalentTo(new[] { "tenantId", "clientId", "thumbprint", "notAfter", "servicePrincipalObjectId" }));
    }
    [TestCase("azure identity renew")]
    [TestCase("azure identity remove-old --client-id 11111111-1111-1111-1111-111111111111")]
    [TestCase("azure identity create --env staging")]
    [TestCase("azure identity create --password secret")]
    [TestCase("azure infra --yes --yes")]
    [TestCase("azure create")]
    public void Invalid_commands_are_rejected_before_authentication(string command) => Assert.Throws<CliRefusal>(() => AzureArguments.Parse(command.Split(' ')));

    [TestCase("http://management.azure.com/")]
    [TestCase("https://evil.example/")]
    [TestCase("https://graph.microsoft.com:444/")]
    [TestCase("https://user@graph.microsoft.com/")]
    public void Untrusted_continuation_urls_never_receive_tokens(string url) => Assert.Throws<CliRefusal>(() => AzureRest.ValidateUri(url));

    [Test]
    public async Task Pagination_uses_service_scopes_and_preserves_all_rows()
    {
        var tokens = new Tokens(); var n = 0;
        using var handler = new Handler((r, _) => r.RequestUri!.Host == "graph.microsoft.com" ? Reply(new JsonObject { ["value"] = new JsonArray(new JsonObject { ["id"] = ++n }), ["@odata.nextLink"] = n == 1 ? AzureRest.Graph + "applications?page=2" : null }) : Reply());
        using var http = new HttpClient(handler); var rest = new AzureRest(http, tokens);
        Assert.That((await rest.List(AzureRest.Graph + "applications")).Count, Is.EqualTo(2));
        await rest.Send(HttpMethod.Get, AzureRest.Arm + "subscriptions");
        Assert.That(tokens.Scopes, Is.EqualTo(new[] { "https://graph.microsoft.com/.default", "https://graph.microsoft.com/.default", "https://management.azure.com/.default" }));
    }
    [Test]
    public void Duplicate_name_refuses_without_mutations()
    {
        using var handler = new Handler((_, _) => Reply(new JsonObject { ["value"] = new JsonArray(new JsonObject { ["id"] = ObjectId }) }));
        using var http = new HttpClient(handler);
        Assert.ThrowsAsync<CliRefusal>(async () => await new AzureIdentity(new AzureRest(http, new Tokens())).RefuseDuplicate("same"));
        Assert.That(handler.Calls.All(c => c.Method == "GET"), Is.True);
    }
    [Test]
    public async Task Renew_appends_every_existing_public_key_and_exports_matching_identity()
    {
        var old = new JsonArray(Key('A'), Key('B')); var app = App(old); JsonObject? patch = null;
        using var handler = new Handler((r, b) =>
        {
            if (r.Method == HttpMethod.Patch) { patch = JsonNode.Parse(b)!.AsObject(); return Reply(); }
            return Reply(r.RequestUri!.AbsolutePath.Contains("servicePrincipals", StringComparison.Ordinal) ? new JsonObject { ["value"] = new JsonArray(new JsonObject { ["id"] = ObjectId }) } : app);
        });
        using var http = new HttpClient(handler); var files = new Files(); var console = new ServerSetupTests.Input { Secret = "hidden-secret" };
        await AzureCli.Execute(Args("renew"), console, new AzureRest(http, new Tokens()), files, Client);
        Assert.Multiple(() =>
        {
            Assert.That(patch!["keyCredentials"]!.AsArray(), Has.Count.EqualTo(3));
            Assert.That(JsonNode.DeepEquals(patch["keyCredentials"]![0], old[0]), Is.True);
            Assert.That(JsonNode.DeepEquals(patch["keyCredentials"]![1], old[1]), Is.True);
            Assert.That(files.Identity!["thumbprint"]!.GetValue<string>(), Is.EqualTo(files.PfxThumb));
            Assert.That(handler.Calls.Where(c => c.Url.Contains("applications(appId", StringComparison.Ordinal)).All(c => c.Url.Contains("keyCredentials", StringComparison.Ordinal)), Is.True);
            Assert.That(string.Join('\n', console.Output) + string.Join('\n', handler.Calls.Select(c => c.Body)), Does.Not.Contain(console.Secret));
        });
    }
    [Test]
    public async Task Create_posts_only_public_key_and_creates_principal()
    {
        using var handler = new Handler((r, _) => r.Method == HttpMethod.Post ? Reply(r.RequestUri!.AbsolutePath.EndsWith("applications", StringComparison.Ordinal) ? new JsonObject { ["id"] = ObjectId, ["appId"] = Client } : new JsonObject { ["id"] = ObjectId }) : Reply(new JsonObject { ["value"] = new JsonArray() }));
        using var http = new HttpClient(handler); var files = new Files(); var console = new ServerSetupTests.Input { Secret = "hidden-secret" };
        var args = Args("create"); args.Values.Remove("client-id");
        await AzureCli.Execute(args, console, new AzureRest(http, new Tokens()), files, Client);
        var post = JsonNode.Parse(handler.Calls.Single(c => c.Method == "POST" && c.Url.EndsWith("applications", StringComparison.Ordinal)).Body)!;
        using var cert = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(post["keyCredentials"]![0]!["key"]!.GetValue<string>()));
        Assert.That(cert.HasPrivateKey, Is.False);
        Assert.That(files.Identity!["servicePrincipalObjectId"]!.GetValue<string>(), Is.EqualTo(ObjectId));
        Assert.That(console.Output, Does.Contain("Send the PFX to IT; send its password through a different channel."));
    }
    [Test]
    public void Empty_password_refuses_before_any_remote_mutation()
    {
        using var handler = new Handler((_, _) => Reply(App(new JsonArray(Key('A')))));
        using var http = new HttpClient(handler); var files = new Files();
        Assert.ThrowsAsync<CliRefusal>(async () => await AzureCli.Execute(Args("renew"), new ServerSetupTests.Input(), new AzureRest(http, new Tokens()), files, Client));
        Assert.That(handler.Calls.All(c => c.Method == "GET"), Is.True); Assert.That(files.PfxThumb, Is.Null);
    }
    [Test]
    public void Mismatched_passwords_refuse_before_export_or_mutation()
    {
        using var handler = new Handler((_, _) => Reply(App(new JsonArray(Key('A')))));
        using var http = new HttpClient(handler); var files = new Files();
        Assert.ThrowsAsync<CliRefusal>(async () => await AzureCli.Execute(Args("renew"), new PasswordInput("first", "second"), new AzureRest(http, new Tokens()), files, Client));
        Assert.That(handler.Calls.All(c => c.Method == "GET"), Is.True); Assert.That(files.PfxThumb, Is.Null);
    }
    [Test]
    public async Task Remove_old_warns_names_thumbprint_and_patches_only_after_confirmation()
    {
        var app = App(new JsonArray(Key('A'), Key('B')));
        using var handler = new Handler((r, _) => Reply(r.Method == HttpMethod.Get ? app : null)); using var http = new HttpClient(handler);
        var args = Args("remove-old"); args.Values["thumbprint"] = new string('A', 40); args.Values.Remove("yes");
        var c = new ServerSetupTests.Input("no");
        Assert.ThrowsAsync<CliCancelled>(async () => await AzureCli.Execute(args, c, new AzureRest(http, new Tokens()), new Files(), Client));
        Assert.That(handler.Calls.All(c => c.Method == "GET"), Is.True);
        Assert.That(string.Join('\n', c.Output), Does.Contain(new string('A', 40)).And.Contain("server still uses"));
        await AzureCli.Execute(args, new ServerSetupTests.Input("yes"), new AzureRest(http, new Tokens()), new Files(), Client);
        var keys = JsonNode.Parse(handler.Calls.Single(x => x.Method == "PATCH").Body)!["keyCredentials"]!.AsArray();
        Assert.That(keys, Has.Count.EqualTo(1)); Assert.That(JsonNode.DeepEquals(keys[0], app["keyCredentials"]![1]), Is.True);
    }
    [Test]
    public void Accepted_poll_responses_cannot_restart_the_polling_limit()
    {
        using var handler = new Handler((_, _) =>
        {
            var response = Reply(new JsonObject { ["status"] = "Running" }, HttpStatusCode.Accepted);
            response.Headers.Location = new Uri(AzureRest.Arm + "operations/repeat"); return response;
        });
        using var http = new HttpClient(handler);
        Assert.ThrowsAsync<CliEnvironment>(async () => await new AzureRest(http, new Tokens(), _ => Task.CompletedTask).Poll(AzureRest.Arm + "operations/repeat", default));
        Assert.That(handler.Calls, Has.Count.EqualTo(360));
    }
    [Test]
    public void Remove_old_removes_only_named_key_and_refuses_last_or_unknown()
    {
        var app = App(new JsonArray(Key('A'), Key('B')));
        Assert.That(AzureIdentity.Without(app, new string('A', 40))[0]!["customKeyIdentifier"]!.GetValue<string>(), Is.EqualTo(app["keyCredentials"]![1]!["customKeyIdentifier"]!.GetValue<string>()));
        Assert.Throws<CliRefusal>(() => AzureIdentity.Without(App(new JsonArray(Key('A'))), new string('A', 40)));
        Assert.Throws<CliRefusal>(() => AzureIdentity.Without(app, new string('C', 40)));
        app["keyCredentials"]![0]!["key"] = null;
        Assert.Throws<CliRefusal>(() => AzureIdentity.Credentials(app));
    }
    [Test]
    public void Observed_concurrent_rotation_refuses_patch()
    {
        using var handler = new Handler((_, _) => Reply(App(new JsonArray(Key('B')))));
        using var http = new HttpClient(handler);
        Assert.ThrowsAsync<CliRefusal>(async () => await new AzureIdentity(new AzureRest(http, new Tokens())).Replace(App(new JsonArray(Key('A'))), new JsonArray(Key('C'))));
        Assert.That(handler.Calls.All(c => c.Method == "GET"), Is.True);
    }
    [Test]
    public void Failed_post_is_not_retried_and_does_not_expose_provider_body()
    {
        using var handler = new Handler((_, _) => Reply(new JsonObject { ["secret"] = "do-not-log" }, HttpStatusCode.TooManyRequests));
        using var http = new HttpClient(handler);
        var error = Assert.ThrowsAsync<CliEnvironment>(async () => await new AzureRest(http, new Tokens()).Send(HttpMethod.Post, AzureRest.Graph + "applications", new JsonObject()));
        Assert.That(error!.Message, Does.Not.Contain("do-not-log")); Assert.That(handler.Calls, Has.Count.EqualTo(1));
    }
    [TestCase("Failed")]
    [TestCase("Canceled")]
    [TestCase("Running")]
    public void Polling_is_bounded_and_fails_closed(string state)
    {
        using var handler = new Handler((_, _) => Reply(new JsonObject { ["status"] = state })); using var http = new HttpClient(handler);
        Assert.ThrowsAsync<CliEnvironment>(async () => await new AzureRest(http, new Tokens(), _ => Task.CompletedTask).Poll(AzureRest.Arm + "operations/test", default));
        Assert.That(handler.Calls.Count, Is.EqualTo(state == "Running" ? 360 : 1));
    }
    [Test]
    public void Pfx_export_refuses_repository_ancestor_before_creating_files()
    {
        var root = Path.Combine(RepositoryPaths.SetupRoot, "TestResults", "azure-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        try
        {
            Assert.Throws<InvalidOperationException>(() => new AzureFiles().Validate(Path.Combine(root, "nested"), "identity"));
            Assert.That(Directory.Exists(Path.Combine(root, "nested")), Is.False);
        }
        finally { Directory.Delete(root, true); }
    }
    [TestCase("success")]
    [TestCase("group-cancel")]
    [TestCase("deploy-cancel")]
    [TestCase("preview-fail")]
    [TestCase("deploy-fail")]
    public async Task Infra_runs_whatif_before_deployment_and_fails_closed(string scenario)
    {
        var outputs = new JsonObject { ["blobEndpoint"] = new JsonObject { ["value"] = "https://blob.example/" }, ["documentIntelligenceEndpoint"] = new JsonObject { ["value"] = "https://di.example/" }, ["keyVaultEndpoint"] = new JsonObject { ["value"] = "https://vault.example/" }, ["hosting"] = new JsonObject { ["value"] = new JsonObject() }, ["notAllowed"] = "discard" };
        using var handler = new Handler((r, _) =>
        {
            var url = r.RequestUri!.AbsoluteUri;
            if (url.Contains("subscriptions?", StringComparison.Ordinal)) return Reply(new JsonObject { ["value"] = new JsonArray(new JsonObject { ["subscriptionId"] = Client, ["displayName"] = "Fake" }) });
            if (url.Contains("/whatIf?", StringComparison.Ordinal))
            {
                if (scenario == "preview-fail") return Reply(new JsonObject(), HttpStatusCode.BadRequest);
                var response = Reply(new JsonObject(), HttpStatusCode.Accepted); response.Headers.Add("Azure-AsyncOperation", AzureRest.Arm + "operations/whatif"); return response;
            }
            if (url.EndsWith("operations/whatif", StringComparison.Ordinal)) return Reply(new JsonObject { ["status"] = "Succeeded", ["properties"] = new JsonObject { ["changes"] = new JsonArray(new JsonObject { ["changeType"] = "Create", ["resourceId"] = "fake/storage" }) } });
            if (url.Contains("/deployments/", StringComparison.Ordinal)) return scenario == "deploy-fail" ? Reply(new JsonObject(), HttpStatusCode.BadRequest) : Reply(new JsonObject { ["properties"] = new JsonObject { ["provisioningState"] = "Succeeded", ["outputs"] = outputs.DeepClone() } });
            if (scenario == "group-cancel") return Reply(null, HttpStatusCode.NotFound);
            return Reply(new JsonObject { ["location"] = "francecentral" });
        });
        using var http = new HttpClient(handler); var files = new Files();
        var input = new ServerSetupTests.Input("1", "", "", "dev", "", "", ObjectId, "di", "di-rg", "Admins", ObjectId, "", "no", "alerts@example.invalid", "150", "2026-11-01", "no", @"C:\fake\out.json", "no");
        var args = new AzureArguments("infra", scenario == "deploy-cancel" ? [] : new() { ["yes"] = "true" });
        Task Run() => AzureInfra.Execute(args, input, new AzureRest(http, new Tokens(), _ => Task.CompletedTask), files, default);
        if (scenario != "success")
        {
            if (scenario is "group-cancel" or "deploy-cancel") Assert.ThrowsAsync<CliCancelled>(Run);
            else Assert.ThrowsAsync<CliEnvironment>(Run);
            if (scenario != "deploy-fail") Assert.That(handler.Calls.All(c => c.Method != "PUT"), Is.True);
            Assert.That(files.Output, Is.Null); return;
        }
        await Run();
        var previewIndex = handler.Calls.FindIndex(c => c.Url.Contains("/whatIf?", StringComparison.Ordinal));
        var deploymentIndex = handler.Calls.FindIndex(c => c.Method == "PUT" && c.Url.Contains("/deployments/", StringComparison.Ordinal));
        Assert.That(deploymentIndex, Is.GreaterThan(previewIndex)); Assert.That(files.Output!.Select(p => p.Key), Is.EquivalentTo(new[] { "blobEndpoint", "documentIntelligenceEndpoint", "keyVaultEndpoint", "hosting" }));
        var deployment = JsonNode.Parse(handler.Calls[deploymentIndex].Body)!;
        Assert.That(deployment["properties"]!["parameters"]!["hostingMode"]!["value"]!.GetValue<string>(), Is.EqualTo("onPremises"));
        Assert.That(input.Output.Any(x => x.Contains("Create: fake/storage", StringComparison.Ordinal)), Is.True);
    }
}
