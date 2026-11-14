using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ISTDocAi.Provisioning;
using NUnit.Framework;

namespace ISTDocAi.Setup.Tests;

[Category("Pure")]
public sealed class IdentityRenewTests
{
    private const string Old = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";
    private static string New => ServerSetupTests.Fake.Settings.Thumbprint;
    private static ServerSetupTests.Fake Installed()
    {
        var env = new ServerSetupTests.Fake();
        var i = new ServerInstall(@"C:\package", "DocAi", "docai.example", 443, new string('B', 40), @"C:\install", @"IIS APPPOOL\DocAi", "sql", "ISTDocAi", false, Guid.Empty.ToString(), Guid.Empty.ToString(), Old, @"C:\old.pfx");
        env.Save(new ServerJournal { Install = i, Complete = true });
        env.Files[@"C:\new.json"] = new JsonObject { ["tenantId"] = i.TenantId, ["clientId"] = i.ClientId, ["thumbprint"] = New, ["notAfter"] = DateTimeOffset.UtcNow.AddYears(1).ToString("o") }.ToJsonString();
        env.Files[@"C:\install\appsettings.Production.json"] = Config(Old); return env;
    }
    private static string Config(string thumb) => "{\r\n  // keep this comment\r\n  \"Keep\": \"" + Old + "\", \"DocAi\" : { \"Identity\" : {\"CertificateThumbprint\" : \"" + thumb + "\",}, \"Other\":true},\r\n}\r\n";
    private static ServerSetupTests.Input Input(bool confirm = false) => confirm ? new(@"C:\new.pfx", "", "no") { Secret = "hidden-password" } : new(@"C:\new.pfx", "") { Secret = "hidden-password" };
    [Test]
    public async Task Renewal_imports_grants_recycles_verifies_and_records_old_without_deleting_it()
    {
        var env = Installed(); var c = Input();
        Assert.That(await ServerSetupRunner.Execute(["identity-renew", "--yes"], env, c), Is.Zero, string.Join('\n', c.Output));
        Assert.That(env.Files[@"C:\install\appsettings.Production.json"], Is.EqualTo(Config(New)));
        var journal = ServerSetupRunner.Load(env)!;
        Assert.Multiple(() =>
        {
            Assert.That(journal.Install.Thumbprint, Is.EqualTo(New)); Assert.That(journal.PreviousIdentityThumbprints, Is.EqualTo(new[] { Old })); Assert.That(journal.PendingIdentityThumbprint, Is.Null);
            Assert.That(env.Calls, Does.Contain("import")); Assert.That(env.Calls, Does.Contain("key-read")); Assert.That(env.Calls, Does.Contain("recycle apppool DocAi"));
            Assert.That(env.Calls.Any(x => x.Contains("delete", StringComparison.OrdinalIgnoreCase) || x == "remove-key-read"), Is.False);
            Assert.That(string.Join('\n', env.Files.Values.Concat(c.Output).Concat(env.Calls)), Does.Not.Contain(c.Secret));
        });
    }
    [TestCase("client")]
    [TestCase("tenant")]
    [TestCase("password")]
    [TestCase("thumbprint")]
    [TestCase("elevation")]
    [TestCase("cancel")]
    public async Task Validation_and_cancellation_leave_server_unchanged(string failure)
    {
        var env = Installed(); var before = env.Files[@"C:\install\appsettings.Production.json"];
        var json = JsonNode.Parse(env.Files[@"C:\new.json"])!;
        if (failure is "client" or "tenant") json[failure + "Id"] = Guid.NewGuid().ToString();
        if (failure == "thumbprint") json["thumbprint"] = new string('D', 40);
        env.Files[@"C:\new.json"] = json.ToJsonString(); env.FailPfx = failure == "password"; env.IsElevated = failure != "elevation";
        var c = Input(failure == "cancel");
        var result = await ServerSetupRunner.Execute(failure == "cancel" ? ["identity-renew"] : ["identity-renew", "--yes"], env, c);
        Assert.That(result, Is.EqualTo(failure == "elevation" ? 2 : failure == "cancel" ? 3 : 1));
        Assert.That(env.Files[@"C:\install\appsettings.Production.json"], Is.EqualTo(before));
        Assert.That(env.Calls, Is.Empty);
    }
    [Test]
    public async Task Failed_health_keeps_pending_journal_and_rerun_finishes_same_renewal()
    {
        var env = Installed(); env.HttpCode = 503;
        Assert.That(await ServerSetupRunner.Execute(["identity-renew", "--yes"], env, Input()), Is.EqualTo(2));
        Assert.That(ServerSetupRunner.Load(env)!.PendingIdentityThumbprint, Is.EqualTo(New));
        env.HttpCode = 200; env.CertPresent = true;
        Assert.That(await ServerSetupRunner.Execute(["identity-renew", "--yes"], env, Input()), Is.Zero);
        Assert.That(ServerSetupRunner.Load(env)!.PreviousIdentityThumbprints, Is.EqualTo(new[] { Old }));
        Assert.That(env.Calls.Count(c => c == "import"), Is.EqualTo(1));
    }
    [Test]
    public void Utf8_bom_and_every_unrelated_byte_are_preserved()
    {
        var before = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Config(Old))).ToArray();
        var expected = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Config(New))).ToArray();
        Assert.That(IdentityConfiguration.Replace(before, Old, New), Is.EqualTo(expected));
    }
    [Test]
    public void Production_file_replacement_preserves_bytes_and_access_rules_in_temp_files_only()
    {
        var path = Path.Combine(RepositoryPaths.SetupRoot, "TestResults", "identity-config-" + Guid.NewGuid().ToString("N") + ".json");
        var before = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Config(Old))).ToArray();
        File.WriteAllBytes(path, before);
        try
        {
            var acl = System.IO.FileSystemAclExtensions.GetAccessControl(new FileInfo(path)).GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.Access);
            var bytes = IdentityConfiguration.Replace(before, Old, New);
            new ServerEnvironment().WriteBytes(path, bytes);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
            Assert.That(System.IO.FileSystemAclExtensions.GetAccessControl(new FileInfo(path)).GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.Access), Is.EqualTo(acl));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".identity-*.tmp"), Is.Empty);
        }
        finally { File.Delete(path); }
    }
    [TestCase("{}")]
    [TestCase("{\"DocAi\":{\"Identity\":{\"CertificateThumbprint\":\"wrong\"}}}")]
    public void Missing_or_unexpected_configured_identity_refuses(string config) => Assert.Throws<CliRefusal>(() => IdentityConfiguration.Replace(Encoding.UTF8.GetBytes(config), Old, New));
}
