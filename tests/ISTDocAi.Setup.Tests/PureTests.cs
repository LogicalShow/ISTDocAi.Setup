using NUnit.Framework;
using Assert = ISTDocAi.Setup.Tests.PortAssert;
using ISTDocAi.Provisioning;
using ISTDocAi.Application.Common.Interfaces.DocAi.Persistence;
using ISTDocAi.Infrastructure.Services.DocAi.Intake;
using ISTDocAi.Infrastructure.Services.DocAi.Secrets;
using ISTDocAi.Infrastructure.Configuration.DocAi;
using ISTDocAi.Domain.Dtos.DocAi;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;


using Microsoft.Extensions.DependencyInjection;

namespace ISTDocAi.Setup.Tests.Provisioning;

internal static class TestData
{
    internal static readonly DateTimeOffset Created = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    internal static PrepareRequest Request => new("FWD", "UNF", "Forwarding", "https://kit.example/api/DocAi/Webhook", "https://docai.example/");
    internal static PairingData Pairing(string name = "Forwarding", string tenant = "UNF", string? hash = null, Guid? setup = null) => new()
    {
        SetupId = setup ?? Guid.Parse("c4809da3-612d-4b82-803d-301bfc6a1b22"), ProductCode = "FWD", TenantCode = tenant, ConsumerCode = "FWD-" + tenant,
        Name = name, ApiKeyHash = hash ?? new string('A', 64), CallbackUrl = Request.CallbackUrl,
        WebhookSecretName = Credentials.SecretName(setup ?? Guid.Parse("c4809da3-612d-4b82-803d-301bfc6a1b22")),
        WebhookSecret = Convert.ToBase64String(new byte[32]), CreatedUtc = Created
    };
    internal static string Mutate(string field, JsonNode? value)
    {
        var parts = PairingCode.Encode(Pairing()).Split('.');
        var json = JsonNode.Parse(Convert.FromBase64String(parts[1].Replace('-', '+').Replace('_', '/') + new string('=', (4 - parts[1].Length % 4) % 4)))!;
        json[field] = value;
        var bytes = Encoding.UTF8.GetBytes(json.ToJsonString());
        return parts[0] + "." + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') + "." + Convert.ToHexString(SHA256.HashData(bytes))[..8];
    }
}

public sealed class CredentialsTests : ISTDocAi.Setup.Tests.GuardedToolTests
{
    [Test]
    public async Task Api_key_matches_real_authenticator()
    {
        var key = Credentials.NewApiKey();
        var valid = System.Text.RegularExpressions.Regex.IsMatch(key, "\\A[A-Za-z0-9_-]{43}\\z");
        Assert.True(valid);
        var hash = Credentials.HashApiKey(key);
        Assert.True(hash == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))));
        var source = new ConsumerSource(Convert.FromHexString(hash));
        using var services = new ServiceCollection().AddSingleton<IConsumerSource>(source).BuildServiceProvider();
        var cache = new IntakeCache(services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
        Assert.NotNull(await cache.AuthenticateAsync(key, default));
        Assert.Null(await cache.AuthenticateAsync(key + "wrong", default));
    }

    [Test]
    public void Webhook_key_is_padded_base64()
    {
        var key = Credentials.NewWebhookKey();
        Assert.True(key.Length == 44 && key.EndsWith('='));
        Assert.True(Convert.FromBase64String(key).Length == 32);
    }

    private sealed class ConsumerSource(byte[] hash) : IConsumerSource
    {
        public Task<IReadOnlyList<Consumer>> GetConsumersAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Consumer>>(
            [new Consumer(1, "FWD-UNF", "UNF", hash, null, 10, 2000, true)]);
    }
}

public sealed class PairingTests : ISTDocAi.Setup.Tests.GuardedToolTests
{
    [Test]
    public void Round_trip_is_stable()
    {
        var code = PairingCode.Encode(TestData.Pairing());
        Assert.True(code == PairingCode.Encode(PairingCode.Decode(code)));
        Assert.DoesNotContain(TestData.Pairing().WebhookSecret, TestData.Pairing().ToString());
    }

    [TestCase("v", "2")]
    [TestCase("consumerCode", "OTHER")]
    [TestCase("productCode", "fwd")]
    [TestCase("tenantCode", "A-B")]
    [TestCase("name", "")]
    [TestCase("apiKeyHash", "abc")]
    [TestCase("callbackUrl", "http://kit.example/api/DocAi/Webhook")]
    [TestCase("webhookSecretName", "webhook-wrong")]
    [TestCase("webhookSecret", "invalid")]
    [TestCase("createdUtc", "bad")]
    [TestCase("setupId", "00000000-0000-0000-0000-000000000000")]
    public void Invalid_field_is_refused_without_secret(string field, string value)
    {
        JsonNode node = field == "v" ? JsonValue.Create(int.Parse(value))! : JsonValue.Create(value)!;
        var error = Assert.Throws<ArgumentException>(() => PairingCode.Decode(TestData.Mutate(field, node)));
        Assert.DoesNotContain(TestData.Pairing().WebhookSecret, error.ToString());
    }

    [Test]
    public void Typo_prefix_version_and_unknown_fields_are_refused()
    {
        var code = PairingCode.Encode(TestData.Pairing());
        foreach (var invalid in new[] { code[..^1] + (code[^1] == 'A' ? "B" : "A"), code.Replace("PAIR-1", "PAIR-2"), "WRONG." + code, TestData.Mutate("unknown", JsonValue.Create("x")) })
        {
            var error = Assert.Throws<ArgumentException>(() => PairingCode.Decode(invalid));
            Assert.DoesNotContain(TestData.Pairing().WebhookSecret, error.ToString());
        }
    }
}

public sealed class CodesTests : ISTDocAi.Setup.Tests.GuardedToolTests
{
    [TestCase("", "UNF")]
    [TestCase("fwd", "UNF")]
    [TestCase("F-WD", "UNF")]
    [TestCase("\uFF26\uFF37\uFF24", "UNF")]
    [TestCase("ABCDEFGHIJKLMNOPQRST", "ABCDEFGHIJ")]
    public void Invalid_codes(string product, string tenant) => Assert.Throws<ArgumentException>(() => Codes.ConsumerCode(product, tenant));

    [TestCase("http://kit.example/api/DocAi/Webhook")]
    [TestCase("https://user@kit.example/api/DocAi/Webhook")]
    [TestCase("https://@kit.example/api/DocAi/Webhook")]
    [TestCase("https://kit.example/api/DocAi/Webhook?q=1")]
    [TestCase("https://kit.example/api/DocAi/Webhook#x")]
    [TestCase("https://kit.example/wrong")]
    public void Invalid_callback(string url) => Assert.Throws<ArgumentException>(() => Codes.CallbackUrl(url));

    [TestCase("http://docai.example")]
    [TestCase("https://@docai.example")]
    [TestCase("https://user@docai.example")]
    [TestCase("https://docai.example?x=1")]
    [TestCase("https://docai.example#x")]
    public void Invalid_base_url(string value) => Assert.Throws<ArgumentException>(() => Codes.BaseUrl(value));

    [Test]
    public void Text_boundaries_and_base_url()
    {
        Assert.Equal("https://docai.example/root/", Codes.BaseUrl("https://docai.example/root"));
        Assert.Equal(200, Codes.Name(new string('X', 200)).Length);
        foreach (var value in new[] { "", " ", "line\nbreak", "\u007f", "$(value)", new string('X', 201) })
            Assert.Throws<ArgumentException>(() => Codes.Name(value));
        Assert.Equal("FWD-UNF", Codes.ConsumerCode("FWD", "UNF"));
    }
}

public sealed class KitTests : ISTDocAi.Setup.Tests.GuardedToolTests
{
    [Test]
    public void New_resume_activate_preserves_keys_and_unknown_settings()
    {
        var kit = KitSecretsFile.Parse("<appSettings><add key='Other' value='keep'/></appSettings>");
        var pairing = kit.Prepare(TestData.Request, TestData.Created);
        Assert.Equal("false", kit.Get("DocAi:Enabled"));
        var api = kit.Get("DocAi:ApiKey");
        var xml = kit.ToXml();
        var resumed = KitSecretsFile.Parse(xml);
        var replay = resumed.Prepare(TestData.Request, TestData.Created.AddDays(1));
        Assert.True(PairingCode.Encode(pairing) == PairingCode.Encode(replay));
        Assert.True(api == resumed.Get("DocAi:ApiKey"));
        Assert.Equal("appSettings", System.Xml.Linq.XDocument.Parse(xml).Root!.Name.LocalName);
        Assert.Throws<InvalidOperationException>(() => resumed.Activate("FWD-CPW"));
        resumed.Activate("FWD-UNF");
        Assert.Equal("true", resumed.Get("DocAi:Enabled"));
        Assert.Equal("Live", resumed.Get("DocAi:Mode"));
        Assert.Equal("On", resumed.Get("DocAi:RemotePolling"));
        Assert.Equal(TestData.Request.BaseUrl, resumed.Get("DocAi:BaseUrl"));
        Assert.Equal("keep", resumed.Get("Other"));
        Assert.Throws<InvalidOperationException>(() => resumed.Prepare(TestData.Request, TestData.Created));
    }

    [Test]
    public void Cancel_from_prepared_removes_credentials_and_all_provisioning_metadata()
    {
        var kit = KitSecretsFile.Parse("<appSettings/>");
        kit.Prepare(TestData.Request, TestData.Created);
        var xml = System.Xml.Linq.XDocument.Parse(kit.ToXml());
        xml.Root!.Add(new System.Xml.Linq.XElement("add", new System.Xml.Linq.XAttribute("key", "docaiprovision:Future"), new System.Xml.Linq.XAttribute("value", "remove")));
        xml.Root.Elements("add").Single(x => (string?)x.Attribute("key") == "DocAi:WebhookSecret2").SetAttributeValue("value", "second-secret");
        kit = KitSecretsFile.Parse(xml.ToString());
        kit.Cancel();
        Assert.Null(kit.Get("DocAi:ApiKey"));
        Assert.Null(kit.Get("DocAi:WebhookSecret1"));
        Assert.Null(kit.Get("DocAi:WebhookSecret2"));
        Assert.DoesNotContain(System.Xml.Linq.XDocument.Parse(kit.ToXml()).Root!.Elements("add"),
            x => ((string)x.Attribute("key")!).StartsWith("DocAiProvision:", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("false", kit.Get("DocAi:Enabled"));
    }

    [Test]
    public void Cancel_from_active_is_refused_without_changes()
    {
        var kit = KitSecretsFile.Parse("<appSettings/>");
        kit.Prepare(TestData.Request, TestData.Created); kit.Activate("FWD-UNF");
        var before = kit.ToXml();
        Assert.Throws<InvalidOperationException>(() => kit.Cancel());
        Assert.True(before == kit.ToXml());
    }

    [Test]
    public void Cancel_when_not_provisioned_is_refused_without_changes()
    {
        var kit = KitSecretsFile.Parse("<appSettings><add key='Other' value='keep'/></appSettings>");
        var before = kit.ToXml();
        Assert.Throws<InvalidOperationException>(() => kit.Cancel());
        Assert.Equal(before, kit.ToXml());
    }

    [Test]
    public void Cancel_preserves_every_other_setting()
    {
        var kit = KitSecretsFile.Parse("<appSettings><add key='Other' value='keep'/><add key='DocAi:BaseUrl' value='https://old.example/'/><add key='DocAi:Mode' value='Custom'/><add key='DocAi:RemotePolling' value='Off'/><add key='DocAiProvisionExtra' value='keep too'/></appSettings>");
        kit.Prepare(TestData.Request, TestData.Created);
        kit.Cancel();
        var settings = System.Xml.Linq.XDocument.Parse(kit.ToXml()).Root!.Elements("add")
            .ToDictionary(x => (string)x.Attribute("key")!, x => (string)x.Attribute("value")!);
        Assert.Equal(6, settings.Count);
        Assert.Equal("keep", settings["Other"]);
        Assert.Equal("https://old.example/", settings["DocAi:BaseUrl"]);
        Assert.Equal("Custom", settings["DocAi:Mode"]);
        Assert.Equal("Off", settings["DocAi:RemotePolling"]);
        Assert.Equal("keep too", settings["DocAiProvisionExtra"]);
        Assert.Equal("false", settings["DocAi:Enabled"]);
    }

    [Test]
    public void Prepare_after_cancel_creates_new_setup_and_credentials()
    {
        var kit = KitSecretsFile.Parse("<appSettings/>");
        var first = kit.Prepare(TestData.Request, TestData.Created);
        kit.Cancel();
        kit = KitSecretsFile.Parse(kit.ToXml());
        var second = kit.Prepare(TestData.Request, TestData.Created.AddDays(1));
        Assert.NotEqual(first.SetupId, second.SetupId);
        Assert.True(first.ApiKeyHash != second.ApiKeyHash);
        Assert.True(first.WebhookSecret != second.WebhookSecret);
        Assert.Equal("Prepared", kit.Get("DocAiProvision:State"));
        Assert.Equal("false", kit.Get("DocAi:Enabled"));
    }

    [Test]
    public void Mismatches_legacy_duplicates_and_invalid_root_refused()
    {
        var kit = KitSecretsFile.Parse("<appSettings/>"); kit.Prepare(TestData.Request, TestData.Created);
        foreach (var request in new[] { TestData.Request with { ProductCode = "OTHER" }, TestData.Request with { TenantCode = "CPW" }, TestData.Request with { Name = "Other" }, TestData.Request with { CallbackUrl = "https://other.example/api/DocAi/Webhook" }, TestData.Request with { BaseUrl = "https://other.example" } })
            Assert.Throws<InvalidOperationException>(() => kit.Prepare(request, TestData.Created));
        Assert.Throws<InvalidOperationException>(() => KitSecretsFile.Parse("<appSettings><add key='DocAi:ApiKey' value='fake'/></appSettings>").Prepare(TestData.Request, TestData.Created));
        Assert.Throws<InvalidOperationException>(() => KitSecretsFile.Parse("<appSettings><add key='x' value='1'/><add key='x' value='2'/></appSettings>"));
        Assert.Throws<InvalidOperationException>(() => KitSecretsFile.Parse("<configuration/>"));
        Assert.Throws<InvalidOperationException>(() => KitSecretsFile.Parse("<appSettings><add key='x' value='1'/><add key='X' value='2'/></appSettings>"));
    }
}

public sealed class SqlGeneratorTests : ISTDocAi.Setup.Tests.GuardedToolTests
{
    [Test]
    public void Secret_name_comparison_lowercases_guid_text_and_remains_binary()
    {
        var sql = RegisterConsumerSql.Generate(TestData.Pairing(), 500, "operator", "note");
        Assert.Contains("@SecretName nvarchar(max)=N'webhook-c4809da3612d4b82803d301bfc6a1b22'", sql);
        Assert.Contains("IF CONVERT(varbinary(max),@SecretName)<>CONVERT(varbinary(max),N'webhook-'+LOWER(REPLACE(CONVERT(nvarchar(36),@SetupId),N'-',N'')))", sql);
    }

    [Test]
    public void Escapes_literals_and_emits_hash_once_without_secret()
    {
        var pairing = TestData.Pairing("O'Brien");
        var sql = RegisterConsumerSql.Generate(pairing, 500, "DOMAIN\\operator", "Operator's allowance");
        Assert.Contains("N'O''Brien'", sql); Assert.Contains("N'Operator''s allowance'", sql);
        Assert.DoesNotContain("$(", sql);
        Assert.Equal(2, sql.Split(pairing.ApiKeyHash, StringSplitOptions.None).Length);
        Assert.Contains("@HashHex nvarchar(max)=N'" + pairing.ApiKeyHash + "'", sql);
        Assert.DoesNotContain(pairing.WebhookSecret, sql);
        Assert.Equal("register-consumer-FWD-UNF.sql", RegisterConsumerSql.FileName(pairing));
    }

    [Test]
    public void Invalid_pairing_and_text_lengths_are_refused()
    {
        Assert.Throws<ArgumentException>(() => RegisterConsumerSql.Generate(TestData.Pairing(hash: "bad"), 500, "operator", "note"));
        Assert.Throws<ArgumentException>(() => RegisterConsumerSql.Generate(TestData.Pairing(), 500, new string('X', 201), "note"));
        Assert.Throws<ArgumentException>(() => RegisterConsumerSql.Generate(TestData.Pairing(), 500, "operator", new string('X', 401)));
    }

    [Test]
    public void Checked_in_example_matches_generator()
    {
        var sql = RegisterConsumerSql.Generate(TestData.Pairing(), 500, @"DOMAIN\operator", "Initial allowance at registration");
        var example = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples", "register-consumer-FWD-UNF.sql"));
        Assert.Equal(sql.Replace("\r\n", "\n"), example.Replace("\r\n", "\n"));
    }

    [TestCase(0, "operator", "note")]
    [TestCase(1000001, "operator", "note")]
    [TestCase(500, "", "note")]
    [TestCase(500, "operator", "")]
    [TestCase(500, "operator", "bad\nnote")]
    [TestCase(500, "operator", "$(unsafe)")]
    public void Invalid_inputs(int pages, string actor, string note) => Assert.Throws<ArgumentException>(() => RegisterConsumerSql.Generate(TestData.Pairing(), pages, actor, note));
}




