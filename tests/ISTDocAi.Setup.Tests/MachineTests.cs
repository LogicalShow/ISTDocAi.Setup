using NUnit.Framework;
using Assert = ISTDocAi.Setup.Tests.PortAssert;
using ISTDocAi.Provisioning;
using ISTDocAi.Application.Common.Interfaces.DocAi.Persistence;
using ISTDocAi.Infrastructure.Services.DocAi.Intake;
using ISTDocAi.Infrastructure.Services.DocAi.Secrets;
using ISTDocAi.Infrastructure.Configuration.DocAi;
using ISTDocAi.Domain.Dtos.DocAi;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

using Microsoft.Extensions.Options;

namespace ISTDocAi.Setup.Tests.Provisioning;


[NonParallelizable]
[Category("Machine")]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class ProtectedFileTests : ISTDocAi.Setup.Tests.GuardedToolTests, IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "DocAiProvisioningTests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(folder, "webhook-secrets.json");
    private static string Account => WindowsIdentity.GetCurrent().Name;

    [Test]
    public async Task Install_read_replay_refuse_overwrite_and_preserve_entries()
    {
        var secret = Credentials.NewWebhookKey();
        Assert.Equal(SecretInstallResult.Installed, ProtectedSecretsFile.Install(FilePath, "first", secret, Account));
        var store = new ProtectedFileSecretStore(Options.Create(new SecretsOptions { ProtectedFile = new() { Path = FilePath } }), TimeProvider.System);
        var key = await store.GetSigningKeyAsync("first", default);
        try { Assert.True(Convert.ToBase64String(key) == secret); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(key); }
        var before = File.ReadAllBytes(FilePath);
        var timestamp = File.GetLastWriteTimeUtc(FilePath);
        Assert.Equal(SecretInstallResult.AlreadyInstalled, ProtectedSecretsFile.Install(FilePath, "first", secret, Account));
        Assert.True(before.SequenceEqual(File.ReadAllBytes(FilePath)));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(FilePath));
        Assert.Empty(Directory.GetFiles(folder, "*.bak"));
        var error = Assert.Throws<InvalidOperationException>(() => ProtectedSecretsFile.Install(FilePath, "first", Credentials.NewWebhookKey(), Account));
        Assert.False(error.ToString().Contains(secret, StringComparison.Ordinal));
        Assert.True(before.SequenceEqual(File.ReadAllBytes(FilePath)));
        Assert.Empty(Directory.GetFiles(folder, "*.bak"));
        var firstCipher = JsonDocument.Parse(before).RootElement.GetProperty("first").GetString();
        ProtectedSecretsFile.Install(FilePath, "second", Credentials.NewWebhookKey(), Account);
        using var updated = JsonDocument.Parse(File.ReadAllBytes(FilePath));
        Assert.True(firstCipher == updated.RootElement.GetProperty("first").GetString());
        Assert.Single(Directory.GetFiles(folder, "*.bak"));
        Assert.True(new FileInfo(FilePath).GetAccessControl().AreAccessRulesProtected);
        Assert.True(new DirectoryInfo(folder).GetAccessControl().AreAccessRulesProtected);
    }

    [Test]
    public void Repository_path_refused_before_writing()
    {
        Directory.CreateDirectory(Path.Combine(folder, ".git"));
        Assert.Throws<InvalidOperationException>(() => ProtectedSecretsFile.Install(FilePath, "first", Credentials.NewWebhookKey(), Account));
        Assert.False(File.Exists(FilePath));
    }

    [TestCase("[]")]
    [TestCase("{\"first\":12}")]
    [TestCase("{\"first\":\"one\",\"first\":\"two\"}")]
    public void Invalid_existing_maps_are_unchanged(string json)
    {
        Directory.CreateDirectory(folder); File.WriteAllText(FilePath, json);
        Assert.Throws<InvalidOperationException>(() => ProtectedSecretsFile.Install(FilePath, "new", Credentials.NewWebhookKey(), Account));
        Assert.Equal(json, File.ReadAllText(FilePath));
        Assert.Empty(Directory.GetFiles(folder, "*.bak"));
    }

    [Test]
    public void Kit_save_has_appsettings_root_and_caller_acl()
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "kit.config");
        var kit = KitSecretsFile.Parse("<appSettings/>"); kit.Prepare(TestData.Request, TestData.Created);
        kit.Save(path, KitSecretsFile.CreateAcl(Account));
        Assert.Equal("appSettings", System.Xml.Linq.XDocument.Load(path).Root!.Name.LocalName);
        Assert.True(new FileInfo(path).GetAccessControl().AreAccessRulesProtected);
        Assert.True(PairingCode.Encode(kit.Pairing()) == PairingCode.Encode(KitSecretsFile.Load(path).Pairing()));
    }

    [Test]
    public void Kit_backup_is_private_and_stale_writer_is_refused()
    {
        Directory.CreateDirectory(folder);
        var originalLocal = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        var local = Path.Combine(folder, "local");
        Environment.SetEnvironmentVariable("LOCALAPPDATA", local);
        try
        {
            var path = Path.Combine(folder, "kit.config");
            var kit = KitSecretsFile.Load(path); kit.Prepare(TestData.Request, TestData.Created);
            kit.Save(path, ElevatedKitAcl(Account));
            var stale = KitSecretsFile.Load(path);
            var current = KitSecretsFile.Load(path);
            current.Activate("FWD-UNF"); current.Save(path, ElevatedKitAcl(Account));
            var before = File.ReadAllBytes(path);
            Assert.Throws<InvalidOperationException>(() => stale.Save(path, ElevatedKitAcl(Account)));
            Assert.True(before.SequenceEqual(File.ReadAllBytes(path)));
            var backupFolder = Path.Combine(local, "DocAi", "backups");
            var backup = Assert.Single(Directory.GetFiles(backupFolder));
            Assert.EndsWith("-kit.config", backup);
            var acl = new FileInfo(backup).GetAccessControl();
            Assert.True(acl.AreAccessRulesProtected);
            var rules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
            var rule = Assert.Single(rules);
            Assert.Equal(WindowsIdentity.GetCurrent().User, rule.IdentityReference);
            Assert.Equal(FileSystemRights.FullControl, rule.FileSystemRights);
        }
        finally { Environment.SetEnvironmentVariable("LOCALAPPDATA", originalLocal); }
    }

    private static FileSecurity ElevatedKitAcl(string account)
    {
        var acl = KitSecretsFile.CreateAcl(account);
        acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, AccessControlType.Allow));
        return acl;
    }

    [Test]
    public void Kit_acl_has_only_system_administrators_and_reader_without_inheritance()
    {
        var acl = KitSecretsFile.CreateAcl(Account);
        Assert.True(acl.AreAccessRulesProtected);
        var rules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        Assert.Equal(3, rules.Length);
        foreach (var (sid, rights) in new[] {
            (new SecurityIdentifier("S-1-5-18"), FileSystemRights.FullControl),
            (new SecurityIdentifier("S-1-5-32-544"), FileSystemRights.FullControl),
            (WindowsIdentity.GetCurrent().User!, FileSystemRights.Read | FileSystemRights.Synchronize) })
        {
            var rule = Assert.Single(rules, x => x.IdentityReference.Equals(sid));
            Assert.Equal(rights, rule.FileSystemRights);
            Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
            Assert.False(rule.IsInherited);
            Assert.Equal(InheritanceFlags.None, rule.InheritanceFlags);
            Assert.Equal(PropagationFlags.None, rule.PropagationFlags);
        }
    }

    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
}


