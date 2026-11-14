using NUnit.Framework;
using Assert = ISTDocAi.Setup.Tests.PortAssert;
using ISTDocAi.Provisioning;
using ISTDocAi.Application.Common.Interfaces.DocAi.Persistence;
using ISTDocAi.Infrastructure.Services.DocAi.Intake;
using ISTDocAi.Infrastructure.Services.DocAi.Secrets;
using ISTDocAi.Infrastructure.Configuration.DocAi;
using ISTDocAi.Domain.Dtos.DocAi;
using System.Net;
using System.Text;
using System.Xml;
using Microsoft.Data.SqlClient;

namespace ISTDocAi.Setup.Tests.Provisioning;

public sealed class StageBCliTests : ISTDocAi.Setup.Tests.GuardedToolTests
{
    [TestCase("prepare", "--unknown")]
    [TestCase("prepare", "--pairing-code")]
    [TestCase("prepare", "--path")]
    public void Unknown_or_incomplete_arguments_refused(string command, string option) => Assert.Throws<CliRefusal>(() => CliArguments.Parse([command, option], true));
    [Test]
    public void Help_and_yes_are_explicit_and_yes_does_not_skip_validation()
    {
        Assert.True(CliArguments.Parse(["--help"], true).Help);
        Assert.True(CliArguments.Parse(["apply", "--help"], false).Help);
        Assert.True(CliArguments.Parse(["prepare", "--yes"], true).Yes);
        Assert.Throws<CliRefusal>(() => CliArguments.Parse(["prepare", "--yes", "--path", "C:\\test"], true));
        Assert.Throws<CliRefusal>(() => CliArguments.Parse(["status", "--pages", "10", "--yes"], false));
        Assert.Throws<CliRefusal>(() => CliArguments.Parse(["apply", "--pairing-code", "secret"], false));
        Assert.Throws<CliRefusal>(() => CliArguments.Parse(["apply", "--pages", "1", "--pages", "2"], false));
    }
    [Test]
    public void Hidden_pasted_characters_and_backspace_are_not_echoed()
    {
        var console = new FakeConsole("abc\bD\r");
        Assert.Equal("abD", CliPrompts.Hidden(console)); Assert.Equal(Environment.NewLine, console.Output.ToString());
    }
    [Test]
    public void Prompt_defaults_and_cancellation()
    {
        var console = new FakeConsole("") { Line = "" };
        Assert.Equal("default", CliPrompts.Ask(console, "Name", "default"));
        Assert.Throws<CliCancelled>(() => CliPrompts.Confirm(console, false));
        CliPrompts.Confirm(console, true);
    }
    internal sealed class FakeConsole(string keys) : IProvisionConsole
    {
        private int index;
        public string? Line { get; init; }
        public StringBuilder Output { get; } = new();
        public string? ReadLine() => Line;
        public ConsoleKeyInfo ReadKey()
        {
            var character = keys[index++];
            return new(character, character switch { '\r' => ConsoleKey.Enter, '\b' => ConsoleKey.Backspace, _ => ConsoleKey.A }, false, false, false);
        }
        public void Write(string text) => Output.Append(text);
        public void WriteLine(string text) => Output.AppendLine(text);
    }
}
public sealed class StageBDiscoveryTests : ISTDocAi.Setup.Tests.GuardedToolTests
{
    [TestCase("ApplicationPoolIdentity", "IIS AppPool\\Pool")]
    [TestCase("NetworkService", "NT AUTHORITY\\NETWORK SERVICE")]
    [TestCase("LocalService", "NT AUTHORITY\\LOCAL SERVICE")]
    [TestCase("LocalSystem", "NT AUTHORITY\\SYSTEM")]
    [TestCase("SpecificUser", "DOMAIN\\Service")]
    [TestCase("4", "IIS AppPool\\Pool")]
    [TestCase("3", "DOMAIN\\Service")]
    public void Xml_parses_identity_without_retaining_password(string identity, string expected)
    {
        var result = IisDiscovery.Parse("<appcmd><APP APP.NAME='Site/App' APPPOOL.NAME='Pool'/></appcmd>",
            "<appcmd><VDIR VDIR.NAME='Site/App/' physicalPath='%SystemRoot%\\Example'/></appcmd>",
            $"<appcmd><APPPOOL APPPOOL.NAME='Pool'><add><processModel identityType='{identity}' userName='DOMAIN\\Service' password='NEVER-RETAIN'/></add></APPPOOL></appcmd>",
            "<appcmd><SITE SITE.NAME='Site' bindings='http/*:80:,https/*:443:kit.example'/></appcmd>").Single();
        Assert.Equal(expected, IisDiscovery.RequireAccount(result).Account);
        Assert.Equal(Environment.ExpandEnvironmentVariables("%SystemRoot%\\Example"), result.Folder);
        Assert.Equal("https://kit.example/App/api/DocAi/Webhook", result.Callback);
        Assert.DoesNotContain("NEVER-RETAIN", result.ToString());
    }
    [TestCase("https/*:8443:kit.example", "/App", "https://kit.example:8443/App/api/DocAi/Webhook")]
    [TestCase("https/*:443:kit.example", "/", "https://kit.example/api/DocAi/Webhook")]
    [TestCase("http/*:80:kit.example,https/*:443:", "/App", null)]
    public void Callback_suggestions(string bindings, string app, string? expected) => Assert.Equal(expected, IisDiscovery.Callback(bindings, app));
    [TestCase("<configuration><appSettings file='APP_DATA/DOCAI.SECRETS.CONFIG'/></configuration>", true)]
    [TestCase("<configuration><appSettings FILE='App_Data\\docai.secrets.config'/></configuration>", true)]
    [TestCase("<configuration><appSettings file='other.config'/></configuration>", false)]
    public void Kit_reference_attribute_variants(string xml, bool expected) => Assert.Equal(expected, IisDiscovery.KitConfig(xml));
    [Test]
    public void Dtd_refused() => Assert.Throws<XmlException>(() => IisDiscovery.KitConfig("<!DOCTYPE configuration [<!ENTITY x 'bad'>]><configuration/>"));
    [Test, Category("Machine")]
    public void Candidate_checks_only_sample_temp_config()
    {
        var folder = Path.Combine(Path.GetTempPath(), "docai-candidate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Assert.False(IisDiscovery.Candidate(folder, true));
            File.WriteAllText(Path.Combine(folder, "Web.config"), "<configuration><appSettings file='App_Data\\docai.secrets.config'/></configuration>");
            Assert.True(IisDiscovery.Candidate(folder, true));
            File.WriteAllText(Path.Combine(folder, "Web.config"), "<!DOCTYPE configuration><configuration/>");
            Assert.False(IisDiscovery.Candidate(folder, true));
        }
        finally { Directory.Delete(folder, true); }
    }
}
public sealed class StageBConfigurationTests : ISTDocAi.Setup.Tests.GuardedToolTests
{
    [Test]
    public void Defaults_and_overlay()
    {
        var defaults = ServerConfiguration.Resolve("{}", null, "C:\\ProgramData");
        Assert.Equal("ProtectedFile", defaults.Provider);
        Assert.Equal("C:\\ProgramData\\DocAi\\secrets\\webhook-secrets.json", defaults.Path);
        Assert.False(defaults.WebhooksEnabled);
        var config = ServerConfiguration.Resolve("{\"DocAi\":{\"Secrets\":{\"ProtectedFile\":{\"Path\":\"C:\\\\old.json\"}}},\"Webhooks\":{\"Enabled\":true}}",
            "{\"DocAi\":{\"Secrets\":{\"ProtectedFile\":{\"Path\":\"C:\\\\new.json\"}}},\"Webhooks\":{\"Enabled\":false}}");
        Assert.Equal("C:\\new.json", config.Path); Assert.False(config.WebhooksEnabled);
    }
    [TestCase("{\"DocAi\":{\"Secrets\":{\"Provider\":\"KeyVault\"}}}")]
    [TestCase("{\"DocAi\":{\"Secrets\":{\"ProtectedFile\":{\"Path\":\"relative.json\"}}}}")]
    public void Unsupported_configuration_refused(string json) => Assert.Throws<CliRefusal>(() => ServerConfiguration.Resolve(json, null));
    [TestCase("Server=example;Database=company", "Optional")]
    [TestCase("Server=example;Encrypt=True", "Mandatory")]
    [TestCase("Server=example;Encrypt=Strict", "Strict")]
    [TestCase("Server=example;Encrypt=False", "Optional")]
    public void Encrypt_preserved_or_optional_and_timeout_short(string original, string encrypt)
    {
        var result = new SqlConnectionStringBuilder(CompanyIdentityCheck.AdjustConnectionString(original));
        Assert.Equal(SqlConnectionEncryptOption.Parse(encrypt), result.Encrypt); Assert.Equal(10, result.ConnectTimeout); Assert.False(result.IntegratedSecurity);
    }
    [TestCase("FWD-UNF", true)]
    [TestCase(null, false)]
    [TestCase("FWD-OTHER", false)]
    public void Company_identity_decisions_without_SQL(string? code, bool allowed)
    {
        var result = CompanyIdentityCheck.Check("<configuration><connectionStrings><add name='connectionstring' connectionString='Server=example;Password=DO-NOT-PRINT;User ID=sqluser'/></connectionStrings></configuration>",
            "FWD", "UNF", (connection, product) =>
            {
                Assert.Equal("FWD", product); Assert.Equal("DO-NOT-PRINT", new SqlConnectionStringBuilder(connection).Password);
                return new("company", "Company", code);
            });
        Assert.Equal(allowed, result.Allowed); Assert.DoesNotContain("DO-NOT-PRINT", result.Message);
    }
    [Test]
    public void Connection_failure_warns_without_exception_text()
    {
        var result = CompanyIdentityCheck.Check("<configuration><connectionStrings><add name='ConnectionString' connectionString='Server=example'/></connectionStrings></configuration>",
            "FWD", "UNF", (_, _) => throw new InvalidOperationException("DO-NOT-PRINT"));
        Assert.True(result.Allowed); Assert.Contains("WARNING", result.Message); Assert.DoesNotContain("DO-NOT-PRINT", result.Message);
    }
}
public sealed class StageBCatalogTests : ISTDocAi.Setup.Tests.GuardedToolTests
{
    [TestCase(200, "{\"consumerCode\":\"FWD-UNF\"}", true, 0)]
    [TestCase(200, "{\"consumerCode\":\"FWD-OTHER\"}", false, 1)]
    [TestCase(401, "DO-NOT-PRINT", false, 1)]
    [TestCase(403, "DO-NOT-PRINT", false, 1)]
    [TestCase(500, "DO-NOT-PRINT", false, 2)]
    [TestCase(200, "malformed DO-NOT-PRINT", false, 2)]
    [TestCase(200, "{}", false, 2)]
    public async Task Catalog_checks_status_and_exact_consumer(int status, string body, bool accepted, int exit)
    {
        using var client = new HttpClient(new Handler(status, body));
        var result = await CatalogCheck.Verify(client, "https://docai.example/", "DO-NOT-PRINT", "FWD-UNF");
        Assert.Equal(accepted, result.Accepted); Assert.Equal(exit, result.ExitCode); Assert.DoesNotContain("DO-NOT-PRINT", result.Message);
    }
    private sealed class Handler(int status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https://docai.example/api/v1/catalog", request.RequestUri!.AbsoluteUri);
            Assert.Equal("DO-NOT-PRINT", request.Headers.GetValues("X-Api-Key").Single());
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) });
        }
    }
}
public sealed class StageBSqlWriterTests : ISTDocAi.Setup.Tests.GuardedToolTests
{
    [Test]
    public void Encoding_has_BOM_and_exact_comparison()
    {
        var bytes = SqlOutputWriter.Encode("-- ????");
        Assert.Equal(new byte[] { 239, 187, 191 }, bytes[..3]);
        Assert.True(SqlOutputWriter.Identical(bytes, "-- ????")); Assert.False(SqlOutputWriter.Identical(bytes, "-- changed"));
    }
    [Test, Category("Machine")]
    public void Writer_creates_keeps_or_refuses()
    {
        var folder = Path.Combine(Path.GetTempPath(), "docai-sql-writer-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "register.sql");
        try
        {
            Assert.True(SqlOutputWriter.Write(path, "SELECT N'????';"));
            var timestamp = File.GetLastWriteTimeUtc(path);
            Assert.False(SqlOutputWriter.Write(path, "SELECT N'????';"));
            Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
            Assert.Throws<CliRefusal>(() => SqlOutputWriter.Write(path, "SELECT 2;"));
            Assert.True(SqlOutputWriter.Identical(File.ReadAllBytes(path), "SELECT N'????';"));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}


public sealed class StageBWriterDecisionTests : ISTDocAi.Setup.Tests.GuardedToolTests
{
    [Test]
    public void New_identical_different_and_BOM_using_memory_storage()
    {
        byte[]? stored = null; int writes = 0;
        byte[]? Read() => stored;
        void Create(byte[] bytes) { stored = bytes; writes++; }
        Assert.True(SqlOutputWriter.Write("SELECT N'????';", Read, Create));
        Assert.Equal(new byte[] { 239, 187, 191 }, stored![..3]);
        Assert.False(SqlOutputWriter.Write("SELECT N'????';", Read, Create));
        Assert.Throws<CliRefusal>(() => SqlOutputWriter.Write("SELECT 2;", Read, Create));
        Assert.Equal(1, writes);
    }
    [Test]
    public void Concurrent_creator_is_checked_without_overwrite()
    {
        byte[]? stored = null;
        Assert.False(SqlOutputWriter.Write("same", () => stored, _ => { stored = SqlOutputWriter.Encode("same"); throw new IOException(); }));
        stored = null;
        Assert.Throws<CliRefusal>(() => SqlOutputWriter.Write("same", () => stored, _ => { stored = SqlOutputWriter.Encode("different"); throw new IOException(); }));
    }
}

public sealed class StageBCatalogFailureTests : ISTDocAi.Setup.Tests.GuardedToolTests
{
    [Test]
    public async Task Connection_failure_prints_type_but_not_exception_details()
    {
        using var client = new HttpClient(new FailureHandler());
        var result = await CatalogCheck.Verify(client, "https://docai.example/", "DO-NOT-PRINT", "FWD-UNF");
        Assert.Equal(2, result.ExitCode); Assert.False(result.Accepted);
        Assert.Contains("HttpRequestException", result.Message); Assert.DoesNotContain("DO-NOT-PRINT", result.Message);
    }
    private sealed class FailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new HttpRequestException("DO-NOT-PRINT");
    }
}
