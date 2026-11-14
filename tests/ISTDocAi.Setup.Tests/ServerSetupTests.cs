using System.Text.Json;
using System.Text.Json.Nodes;
using ISTDocAi.Provisioning;
using ISTDocAi.DevSetup;
using NUnit.Framework;

namespace ISTDocAi.Setup.Tests;

[Category("Pure")]
public sealed class ServerSetupTests
{
    internal sealed class Input(params string[] lines) : IProvisionConsole
    {
        private readonly Queue<string> input = new(lines);
        public readonly List<string> Output = [];
        public string Secret = "";
        private int secretPosition;
        public string? ReadLine() => input.Count > 0 ? input.Dequeue() : throw new AssertionException("Unexpected question: " + string.Join("\n", Output));
        public ConsoleKeyInfo ReadKey()
        {
            if (secretPosition < Secret.Length) return new(Secret[secretPosition++], ConsoleKey.A, false, false, false);
            secretPosition = 0; return new('\r', ConsoleKey.Enter, false, false, false);
        }
        public void Write(string text) => Output.Add(text);
        public void WriteLine(string text) => Output.Add(text);
    }
    internal sealed class Fake : ServerEnvironment
    {
        public override string StateRoot => @"C:\fake-state";
        public override string ExecutableFolder => @"C:\package";
        public override bool Elevated => IsElevated;
        public readonly Dictionary<string, string> Files = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Resources = [];
        public readonly List<string> Calls = [];
        public readonly List<string[]> Saved = [];
        public bool IsElevated = true, Hosting = true, Login, Binding, FailPfx, MissingIis, FailCopy;
        public int HttpCode = 200;
        public bool ReadKey, ReadSecrets;
        public DateTimeOffset Expiry = DateTimeOffset.UtcNow.AddYears(1);
        public Fake()
        {
            Files[Path.Combine(ExecutableFolder, "app", "ISTDocAi.UI.Server.dll")] = "dll";
            Files[Path.Combine(ExecutableFolder, "app", "appsettings.json")] = "{\"Keep\":true}";
            Files[Path.Combine(ExecutableFolder, "server-settings.json")] = JsonSerializer.Serialize(Settings, ServerSetupRunner.Json);
        }
        public static ServerSettings Settings => new(Guid.Empty.ToString(), Guid.Empty.ToString(), new string('A', 40), DateTimeOffset.UtcNow.AddYears(1), "https://blob.example/", "https://di.example/", "1", DateTimeOffset.UtcNow);
        public override bool Exists(string path) => Files.ContainsKey(path);
        public bool DenyRead, FailLogin, FailSql;
        public override string Read(string path) => DenyRead ? throw new UnauthorizedAccessException() : Files[path];
        public override byte[] ReadBytes(string path) => System.Text.Encoding.UTF8.GetBytes(Read(path));
        public override void WriteBytes(string path, byte[] bytes) => Write(path, System.Text.Encoding.UTF8.GetString(bytes));
        public override IEnumerable<string> EnumerateFiles(string folder) => this.Files.Keys.Where(p => p.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase)).ToArray();
        public override void Write(string path, string content) { Calls.Add("write:" + path); Files[path] = content; }
        public override void Plain(string path) { }
        private sealed class NoLock : IDisposable { public void Dispose() { } }
        public override IDisposable Lock() => new NoLock();
        public override void Save(ServerJournal journal) { Saved.Add(journal.Started.ToArray()); Files[JournalPath] = JsonSerializer.Serialize(journal); }
        public override void DeleteJournal() { Calls.Add("delete-journal"); Files.Remove(JournalPath); }
        public override void CreateFolder(string path) { Calls.Add("folder:" + path); Files[path] = ""; }
        public override void CopyExecutable(string path) { Calls.Add("exe"); Files[path] = "exe"; }
        public override void CopyApp(string source, string target, bool update)
        {
            Calls.Add(update ? "update" : "copy");
            if (FailCopy) throw new CliEnvironment("simulated interruption");
            Files[target] = "";
            foreach (var item in Files.Where(f => f.Key.StartsWith(source + "\\", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                var name = item.Key[(source.Length + 1)..];
                if (update && name == "appsettings.Production.json") continue;
                Files[Path.Combine(target, name)] = item.Value;
            }
        }
        public override void DeleteFolder(string folder) { Calls.Add("delete-folder:" + folder); Files.Remove(folder); }
        public override bool ResourceExists(string type, string name) => Resources.Contains(type + ":" + name);
        public override bool BindingExists(ServerInstall i) => Binding;
        public override bool BindingMatches(ServerInstall i) => Binding;
        public override bool IisBindingExists(ServerInstall i) => false;
        public override string Iis(params string[] args)
        {
            if (args[0] != "list" || args.Contains("/xml")) Calls.Add(string.Join(" ", args));
            if (args[0] == "add" && args[1] == "apppool") Resources.Add("apppool:" + args[2][6..]);
            if (args[0] == "add" && args[1] == "site") Resources.Add("site:" + args[2][6..]);
            return "Started";
        }
        public override SetupProcess Process(string executable, params string[] args)
        {
            Calls.Add(executable + " " + string.Join(" ", args));
            if (executable == "netsh.exe") Binding = args.Contains("add");
            return new(0, "");
        }
        public readonly List<(string Pool, string Account)> PoolIdentities = [];
        public override void SetPoolIdentity(string pool, string account, string password)
        {
            PoolIdentities.Add((pool, account));
        }
        public override string[] MissingFeatures() => MissingIis ? ["IIS-ApplicationInit"] : [];
        public override void EnableFeatures(string[] features) => Calls.Add("dism:" + string.Join(',', features));
        public override bool HostingReady() => Hosting;
        public override IReadOnlyList<TlsCertificate> Certificates() => [new("CN=docai.example", DateTimeOffset.UtcNow.AddYears(1), new string('B', 40))];
        public override string ValidatePfx(string path, string password) => FailPfx ? throw new CliRefusal("PFX password is wrong or the file is not a valid PFX.") : Settings.Thumbprint;
        public override void ImportPfx(string path, string password) => Calls.Add("import");
        public bool CertPresent;
        public override bool CertificatePresent(string thumbprint) => CertPresent;
        public override bool KeyRead(string thumb, string account) => ReadKey;
        public override bool SecretsAcl(string path, string account) => ReadSecrets;
        public override DateTimeOffset CertificateExpiry(string thumb) => Expiry;
        public override void KeyRule(string thumb, string account, bool remove) => Calls.Add(remove ? "remove-key-read" : "key-read");
        public override void FolderAcl(string folder, string account, bool modify) => Calls.Add("acl:" + folder + ":" + modify);
        public override void IsolateEnvironment(string folder) => Calls.Add("isolate:" + folder);
        public bool? LoginRole = true;
        public override bool? LoginDbCreator(ServerInstall i) => LoginRole;
        public override bool LoginExists(ServerInstall i) => FailLogin ? throw KitDatabaseTests.Refusal(53) : Login;
        public override void Sql(string connection, string sql) { Calls.Add("sql"); if (FailSql) throw KitDatabaseTests.Refusal(18456); Login = true; }
        public override Task<int> Http(string url) => Task.FromResult(HttpCode);
        public override Task Delay() => Task.CompletedTask;
    }
    private static Input InstallInput(string sql = "1") => new("", "", "docai.example", "", "", @"C:\install", "", "sql.example", "", "no", @"C:\identity.pfx", sql);
    private static async Task Install(Fake env)
    {
        var c = InstallInput(); Assert.That(await ServerSetupRunner.Execute(["install", "--yes"], env, c), Is.Zero, string.Join('\n', c.Output));
    }
    [Test]
    public async Task Domain_identity_targets_only_selected_pool_without_password_in_arguments_output_or_journal()
    {
        var env = new Fake();
        var c = new Input("", "", "docai.example", "", "", @"C:\install", @"DOMAIN\service", "sql.example", "", "no", @"C:\identity.pfx", "1") { Secret = "secret<&value" };
        Assert.That(await ServerSetupRunner.Execute(["install", "--yes"], env, c), Is.Zero, string.Join('\n', c.Output));
        Assert.That(env.PoolIdentities, Is.EqualTo(new[] { ("ISTDocAi", @"DOMAIN\service") }));
        Assert.That(env.Calls.Any(call => call.Contains("applicationPools", StringComparison.Ordinal) &&
            (call.Contains("/in", StringComparison.Ordinal) || call.StartsWith("list config", StringComparison.Ordinal))), Is.False);
        Assert.That(string.Join('\n', env.Calls.Concat(c.Output).Concat(env.Files.Values)), Does.Not.Contain(c.Secret));
        Assert.That(string.Join('\n', env.Calls), Does.Not.Contain("/processModel.password:"));
        Assert.That(string.Join('\n', env.Calls), Does.Not.Contain("/processModel.identityType:SpecificUser"));
    }
    public sealed class IdentityProperty(object value)
    {
        public object Value { get; set; } = value;
    }
    public sealed class IdentityElement(string name)
    {
        public readonly Dictionary<string, IdentityProperty> Properties = new()
        {
            ["name"] = new(name), ["identityType"] = new(4), ["userName"] = new(""), ["password"] = new("")
        };
        public IdentityProperty GetPropertyByName(string key) => Properties[key];
        public IdentityElement GetElementByName(string key)
        {
            Assert.That(key, Is.EqualTo("processModel")); return this;
        }
    }
    public sealed class IdentityCollection(params string[] names)
    {
        public IdentityElement[] Elements { get; } = names.Select(name => new IdentityElement(name)).ToArray();
        public int Count => Elements.Length;
        public IdentityElement Item(int index) => Elements[index];
    }
    public sealed class IdentityManager(params string[] names)
    {
        public string CommitPath { get; set; } = "";
        public IdentityCollection Collection { get; } = new(names);
        public int Commits;
        public bool Fail;
        public IdentityManager GetAdminSection(string section, string path)
        {
            Assert.That(section, Is.EqualTo("system.applicationHost/applicationPools"));
            Assert.That(path, Is.EqualTo("MACHINE/WEBROOT/APPHOST")); return this;
        }
        public void CommitChanges()
        {
            if (Fail) throw new System.Runtime.InteropServices.COMException("secret<&value");
            Commits++;
        }
    }
    private sealed class IdentityTransport(params string[] names) : ServerEnvironment
    {
        public IdentityManager Manager { get; } = new(names);
        protected override object CreateIisManager() => Manager;
        public override SetupProcess Process(string executable, params string[] args) => throw new AssertionException("Identity must not launch a process.");
    }
    [TestCase(false)][TestCase(true)]
    public void Identity_transport_sends_only_target_pool_and_suppresses_failure_details(bool fail)
    {
        var env = new IdentityTransport("OtherPool", "chosenpool"); env.Manager.Fail = fail;
        const string password = "secret<&value";
        if (fail)
        {
            var error = Assert.Throws<CliEnvironment>(() => env.SetPoolIdentity("ChosenPool", @"DOMAIN\service", password));
            Assert.That(error!.ToString(), Does.Not.Contain(password));
        }
        else env.SetPoolIdentity("ChosenPool", @"DOMAIN\service", password);
        Assert.That(env.Manager.CommitPath, Is.EqualTo("MACHINE/WEBROOT/APPHOST"));
        Assert.That(env.Manager.Commits, Is.EqualTo(fail ? 0 : 1));
        var other = env.Manager.Collection.Elements[0];
        Assert.That(other.Properties["identityType"].Value, Is.EqualTo(4));
        Assert.That(other.Properties["userName"].Value, Is.EqualTo(""));
        Assert.That(other.Properties["password"].Value, Is.EqualTo(""));
        var model = env.Manager.Collection.Elements[1];
        Assert.That(model.Properties["identityType"].Value, Is.EqualTo(3));
        Assert.That(model.Properties["userName"].Value, Is.EqualTo(@"DOMAIN\service"));
        Assert.That(model.Properties["password"].Value, Is.EqualTo(password));
    }
    [TestCase(false)][TestCase(true)]
    public void Identity_transport_refuses_missing_or_duplicate_pool_without_commit(bool duplicate)
    {
        var env = duplicate ? new IdentityTransport("ChosenPool", "CHOSENPOOL") : new IdentityTransport("OtherPool");
        Assert.Throws<CliEnvironment>(() => env.SetPoolIdentity("ChosenPool", @"DOMAIN\service", "secret<&value"));
        Assert.That(env.Manager.Commits, Is.Zero);
        Assert.That(env.Manager.Collection.Elements.All(element => (int)element.Properties["identityType"].Value == 4), Is.True);
    }
    [Test]
    public async Task Application_pool_identity_does_not_use_specific_user_api()
    {
        var env = new Fake(); await Install(env);
        Assert.That(env.PoolIdentities, Is.Empty);
        Assert.That(string.Join(' ', env.Calls), Does.Contain("/processModel.identityType:ApplicationPoolIdentity"));
    }
    [TestCase(true, 0)][TestCase(null, 0)][TestCase(false, 1)]
    public async Task Dba_confirmation_verifies_when_visible_and_records_acceptance(bool? role, int code)
    {
        var env = new Fake { LoginRole = role };
        var c = InstallInput("2");
        Assert.That(await ServerSetupRunner.Execute(["install", "--yes"], env, c), Is.EqualTo(code));
        Assert.That(env.Calls, Does.Not.Contain("sql"));
        Assert.That(ServerSetupRunner.Load(env)!.LoginDbaConfirmed, Is.EqualTo(code == 0));
        if (role is null) Assert.That(string.Join('\n', c.Output), Does.Contain("could not be verified"));
    }
    [TestCase(false)][TestCase(true)]
    public async Task Sql_connection_failures_have_actionable_safe_message(bool duringRun)
    {
        var env = new Fake { FailLogin = !duringRun, FailSql = duringRun }; var c = InstallInput();
        Assert.That(await ServerSetupRunner.Execute(["install", "--yes"], env, c), Is.EqualTo(2));
        Assert.That(string.Join('\n', c.Output), Does.Contain("Cannot connect to SQL Server 'sql.example' with your Windows login.").And.Not.Contain("details suppressed").And.Not.Contain("ConnectionString"));
    }
    [TestCase("check")][TestCase("apply")]
    public async Task Private_journal_denied_has_administrator_message(string command)
    {
        var env = new Fake(); await Install(env); env.DenyRead = true; var c = new Input();
        Assert.That(await ServerSetupRunner.Execute([command], env, c), Is.EqualTo(2));
        Assert.That(c.Output, Does.Contain("Run from an administrator terminal."));
    }
    [Test]
    public async Task Check_requires_elevation()
    {
        var c = new Input();
        Assert.That(await ServerSetupRunner.Execute(["check"], new Fake { IsElevated = false }, c), Is.EqualTo(2));
        Assert.That(string.Join('\n', c.Output), Does.Contain("Run from an administrator terminal."));
    }
    [Test]
    public async Task Fresh_install_has_write_ahead_sequence_and_production_settings()
    {
        var env = new Fake(); await Install(env); var j = ServerSetupRunner.Load(env)!;
        Assert.That(j.Started, Is.EqualTo(ServerSetupModel.InstallSteps)); Assert.That(j.Done, Is.EqualTo(j.Started)); Assert.That(j.Complete, Is.True);
        Assert.That(env.Saved.Where(s => s.Length > 0).Select(s => s[^1]).Distinct(), Is.EqualTo(ServerSetupModel.InstallSteps));
        var json = JsonNode.Parse(env.Files[Path.Combine(j.Install.Folder, "appsettings.Production.json")])!;
        Assert.That(json["Keep"]!.GetValue<bool>(), Is.True);
        Assert.That(json["DocAi"]!["Identity"]!["StoreLocation"]!.GetValue<string>(), Is.EqualTo("LocalMachine"));
        Assert.That(json["DocAi"]!["Blob"]!["AccountUrl"]!.GetValue<string>(), Is.EqualTo(Fake.Settings.BlobEndpoint));
        Assert.That(json["DocAi"]!["Database"]!["MigrateOnStartup"]!.GetValue<bool>(), Is.True);
        var connection = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(json["ConnectionStrings"]!["DefaultConnection"]!.GetValue<string>());
        Assert.That(connection.IntegratedSecurity, Is.True); Assert.That(connection.TrustServerCertificate, Is.False); Assert.That(connection.Password, Is.Empty);
        Assert.That(string.Join('\n', env.Calls), Does.Contain("shutdownTimeLimit:00:03:30").And.Contain("idleTimeout:00:00:00").And.Contain("ASPNETCORE_ENVIRONMENT").And.Contain("preloadEnabled:true"));
    }
    [Test]
    public async Task Complete_rerun_has_no_mutations()
    {
        var env = new Fake(); await Install(env); env.Calls.Clear(); env.Saved.Clear();
        Assert.That(await ServerSetupRunner.Execute(["install", "--yes"], env, new Input()), Is.Zero);
        Assert.That(env.Calls, Is.Empty); Assert.That(env.Saved, Is.Empty);
    }
    [Test]
    public async Task Install_and_update_isolate_the_site_from_inherited_IIS_environment_variables()
    {
        var env = new Fake(); await Install(env);
        Assert.That(env.Calls, Has.Member(@"isolate:C:\install"));
        env.Calls.Clear();
        var c = new Input(""); Assert.That(await ServerSetupRunner.Execute(["update", "--yes"], env, c), Is.Zero, string.Join('\n', c.Output));
        Assert.That(env.Calls, Has.Member(@"isolate:C:\install"));
    }
    [Test]
    public void IsolateEnvironment_adds_one_clear_and_keeps_the_handler()
    {
        var folder = Path.Combine(Path.GetTempPath(), "IsolateTest_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "web.config");
            File.WriteAllText(path, """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration><location path="." inheritInChildApplications="false"><system.webServer>
                <handlers><add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" /></handlers>
                <aspNetCore processPath="dotnet" arguments=".\ISTDocAi.UI.Server.dll" hostingModel="inprocess" />
                </system.webServer></location></configuration>
                """);
            new ServerEnvironment().IsolateEnvironment(folder); new ServerEnvironment().IsolateEnvironment(folder);
            var document = System.Xml.Linq.XDocument.Load(path);
            var variables = document.Descendants("aspNetCore").Single().Element("environmentVariables")!;
            Assert.That(variables.Elements().Select(e => e.Name.LocalName), Is.EqualTo(new[] { "clear" }));
            Assert.That(document.Descendants("add").Single().Attribute("modules")!.Value, Is.EqualTo("AspNetCoreModuleV2"));
        }
        finally { Directory.Delete(folder, true); }
    }
    [Test]
    public async Task Check_reports_missing_secrets_file_as_not_created_yet()
    {
        var env = new Fake { ReadKey = true }; await Install(env);
        var c = new Input(); var code = await ServerSetupRunner.Execute(["check"], env, c);
        Assert.That(c.Output, Has.Some.Contains("secrets file: not created yet"));
        Assert.That(code, Is.Zero, string.Join('\n', c.Output));
    }
    [Test]
    public async Task Existing_identity_certificate_is_not_reimported_and_no_pfx_password_is_asked()
    {
        var env = new Fake { CertPresent = true, FailPfx = true };
        var c = InstallInput();
        Assert.That(await ServerSetupRunner.Execute(["install", "--yes"], env, c), Is.Zero, string.Join('\n', c.Output));
        Assert.That(env.Calls, Does.Not.Contain("import"));
        Assert.That(c.Output, Has.Some.Contains("will not be imported again"));
        Assert.That(c.Output, Has.None.Contains("PFX password"));
        Assert.That(JsonSerializer.Deserialize<ServerJournal>(env.Files[env.JournalPath])!.CertificatePreexisting, Is.True);
    }
    [Test]
    public async Task Reinstall_after_remove_reuses_tool_owned_state_folders()
    {
        var env = new Fake(); await Install(env);
        Assert.That(env.Files.Keys, Has.Member(Path.Combine(env.StateRoot, "secrets", ServerSetupRunner.OwnedMarker)));
        Assert.That(env.Files.Keys, Has.Member(Path.Combine(env.StateRoot, "logs", ServerSetupRunner.OwnedMarker)));
        env.Files[Path.Combine(env.StateRoot, "secrets")] = ""; env.Files[Path.Combine(env.StateRoot, "logs")] = "";
        var r = new Input(); Assert.That(await ServerSetupRunner.Execute(["remove", "--yes"], env, r), Is.Zero, string.Join('\n', r.Output));
        env.Resources.Clear(); env.Login = false; env.Binding = false;
        var c = InstallInput(); Assert.That(await ServerSetupRunner.Execute(["install", "--yes"], env, c), Is.Zero, string.Join('\n', c.Output));
    }
    [TestCase("secrets")][TestCase("logs")]
    public async Task Unowned_state_folders_are_refused(string folder)
    {
        var env = new Fake();
        env.Files[Path.Combine(env.StateRoot, folder)] = "";
        Assert.That(await ServerSetupRunner.Execute(["install", "--yes"], env, InstallInput()), Is.EqualTo(1));
        Assert.That(env.Calls, Is.Empty); Assert.That(env.Saved, Is.Empty);
    }
    [Test]
    public async Task Partial_install_resumes_remaining_steps()
    {
        var env = new Fake { FailCopy = true };
        Assert.That(await ServerSetupRunner.Execute(["install", "--yes"], env, InstallInput()), Is.EqualTo(2));
        env.FailCopy = false; env.Calls.Clear();
        Assert.That(await ServerSetupRunner.Execute(["install", "--yes"], env, new Input("1")), Is.Zero);
        Assert.That(env.Calls, Does.Not.Contain("import").And.Not.Contain("key-read"));
    }
    [TestCase("site")][TestCase("apppool")][TestCase("folder")][TestCase("login")][TestCase("binding")]
    public async Task Unowned_resources_are_refused(string type)
    {
        var env = new Fake();
        if (type == "folder") env.Files[@"C:\install"] = "";
        else if (type == "login") env.Login = true;
        else if (type == "binding") env.Binding = true;
        else env.Resources.Add(type + ":ISTDocAi");
        Assert.That(await ServerSetupRunner.Execute(["install", "--yes"], env, InstallInput()), Is.EqualTo(1));
        Assert.That(env.Calls, Is.Empty); Assert.That(env.Saved, Is.Empty);
    }
    [Test]
    public async Task Dba_handoff_stops_before_start_and_can_resume_after_external_login()
    {
        var env = new Fake(); Assert.That(await ServerSetupRunner.Execute(["install", "--yes"], env, InstallInput("3")), Is.EqualTo(2));
        Assert.That(env.Files.Keys, Has.Some.EndsWith("app-login.sql")); Assert.That(env.Calls, Has.None.StartsWith("start "));
        env.Login = true;
        Assert.That(await ServerSetupRunner.Execute(["install", "--yes"], env, new Input("2")), Is.Zero);
        Assert.That(ServerSetupRunner.Load(env)!.LoginCreated, Is.False);
    }
    [Test]
    public async Task Wrong_pfx_has_clear_message_and_no_journal()
    {
        var env = new Fake { FailPfx = true }; var c = InstallInput();
        Assert.That(await ServerSetupRunner.Execute(["install", "--yes"], env, c), Is.EqualTo(1));
        Assert.That(c.Output, Does.Contain("PFX password is wrong or the file is not a valid PFX.")); Assert.That(env.Saved, Is.Empty);
    }
    [Test]
    public async Task Missing_hosting_bundle_is_action_needed()
    {
        var env = new Fake { Hosting = false }; var c = new Input();
        Assert.That(await ServerSetupRunner.Execute(["install", "--yes"], env, c), Is.EqualTo(2));
        Assert.That(string.Join('\n', c.Output), Does.Contain("https://dotnet.microsoft.com/download/dotnet/10.0")); Assert.That(env.Saved, Is.Empty);
    }
    [Test]
    public async Task Missing_iis_requires_separate_confirmation_despite_yes()
    {
        var env = new Fake { MissingIis = true, Hosting = false };
        Assert.That(await ServerSetupRunner.Execute(["install", "--yes"], env, new Input("yes")), Is.EqualTo(2));
        Assert.That(env.Calls, Does.Contain("dism:IIS-ApplicationInit"));
    }
    [Test]
    public async Task Declining_iis_installation_does_not_enable_features()
    {
        var env = new Fake { MissingIis = true };
        Assert.That(await ServerSetupRunner.Execute(["install", "--yes"], env, new Input("no")), Is.EqualTo(2));
        Assert.That(env.Calls, Is.Empty);
    }
    [Test]
    public async Task Partial_remove_only_touches_started_resources()
    {
        var env = new Fake { FailCopy = true };
        Assert.That(await ServerSetupRunner.Execute(["install", "--yes"], env, InstallInput()), Is.EqualTo(2)); env.Calls.Clear();
        Assert.That(await ServerSetupRunner.Execute(["remove", "--yes"], env, new Input()), Is.Zero);
        Assert.That(env.Calls, Has.None.StartsWith("netsh.exe").And.None.EqualTo("sql").And.None.StartsWith("delete site"));
        Assert.That(env.Calls, Does.Contain("remove-key-read").And.Contain(@"delete-folder:C:\install"));
    }
    [Test]
    public async Task Failed_health_check_is_resumable_without_recreating_resources()
    {
        var env = new Fake { HttpCode = 0 }; var c = InstallInput();
        Assert.That(await ServerSetupRunner.Execute(["install", "--yes"], env, c), Is.EqualTo(2));
        Assert.That(string.Join('\n', c.Output), Does.Contain("trust, DNS"));
        env.HttpCode = 200; env.Calls.Clear();
        Assert.That(await ServerSetupRunner.Execute(["install", "--yes"], env, new Input()), Is.Zero); Assert.That(env.Calls, Is.Empty);
    }
    [TestCase("package")][TestCase("install")][TestCase("update")][TestCase("remove")][TestCase("check")][TestCase("apply")][TestCase("status")]
    public async Task Every_server_command_supports_nonelevated_help(string command)
    {
        var env = new Fake { IsElevated = false };
        Assert.That(await ServerSetupRunner.Execute([command, "--help", "--yes"], env, new Input()), Is.Zero); Assert.That(env.Calls, Is.Empty);
    }
    [TestCase("install")][TestCase("update")][TestCase("remove")]
    public async Task Server_mutations_require_elevation(string command)
    {
        var env = new Fake { IsElevated = false };
        Assert.That(await ServerSetupRunner.Execute([command, "--yes"], env, new Input()), Is.EqualTo(2)); Assert.That(env.Calls, Is.Empty);
    }
    [Test]
    public async Task Remove_keeps_certificate_secrets_and_database()
    {
        var env = new Fake(); await Install(env); env.Calls.Clear();
        Assert.That(await ServerSetupRunner.Execute(["remove", "--yes"], env, new Input()), Is.Zero);
        Assert.That(env.Calls, Does.Contain("remove-key-read").And.Contain(@"delete-folder:C:\install").And.Contain("delete-journal"));
        Assert.That(env.Calls, Has.None.Contains("delete-folder:C:\\fake-state"));
    }
    [Test]
    public async Task Update_preserves_production_configuration()
    {
        var env = new Fake(); await Install(env); var path = @"C:\install\appsettings.Production.json"; var before = env.Files[path];
        env.Files[@"C:\package\app\appsettings.Production.json"] = "{\"marker\":true}";
        Assert.That(await ServerSetupRunner.Execute(["update", "--yes"], env, new Input("")), Is.Zero);
        Assert.That(env.Files[path], Is.EqualTo(before));
    }
    [TestCase("appsettings.Development.json")][TestCase("appsettings.Local.json")]
    public async Task Package_refuses_development_configuration(string name)
    {
        var env = PackageEnvironment(); env.Files[@"C:\output\app\" + name] = "{}";
        Assert.That(await ServerSetupRunner.Execute(["package", "--yes"], env, PackageInput()), Is.EqualTo(1));
        Assert.That(env.Files.ContainsKey(@"C:\output\server-settings.json"), Is.False);
    }
    [TestCase("identity.pfx", "fake-pfx")][TestCase("appsettings.json", "{\"ApiKey\":\"never-ship\"}")]
    public async Task Package_refuses_secret_material(string name, string content)
    {
        var env = PackageEnvironment(); env.Files[@"C:\output\app\" + name] = content;
        var c = PackageInput();
        Assert.That(await ServerSetupRunner.Execute(["package", "--yes"], env, c), Is.EqualTo(1));
        Assert.That(string.Join('\n', c.Output), Does.Not.Contain("never-ship").And.Not.Contain("fake-pfx"));
    }
    private static Fake PackageEnvironment()
    {
        var env = new Fake(); env.Files[@"C:\source\ISTDocAi.sln"] = ""; env.Files[@"C:\source\ISTDocAi.UI\ISTDocAi.UI.Server"] = "";
        env.Files[@"C:\identity.json"] = JsonSerializer.Serialize(new { tenantId = Fake.Settings.TenantId, clientId = Fake.Settings.ClientId, thumbprint = Fake.Settings.Thumbprint, notAfter = Fake.Settings.NotAfter }); return env;
    }
    private static Input PackageInput() => new(@"C:\source", @"C:\output", @"C:\identity.json", "", "https://blob.example/", "https://di.example/");
    [Test]
    public async Task Package_outputs_only_selected_nonsecret_metadata()
    {
        var env = PackageEnvironment();
        Assert.That(await ServerSetupRunner.Execute(["package", "--yes"], env, PackageInput()), Is.Zero);
        var json = JsonNode.Parse(env.Files[@"C:\output\server-settings.json"])!.AsObject();
        Assert.That(json.Select(p => p.Key), Is.EquivalentTo(new[] { "tenantId", "clientId", "thumbprint", "notAfter", "blobEndpoint", "documentIntelligenceEndpoint", "version", "packagedAt" }));
    }
    [Test]
    public async Task Package_refuses_existing_output()
    {
        var env = PackageEnvironment(); env.Files[@"C:\output"] = "";
        Assert.That(await ServerSetupRunner.Execute(["package", "--yes"], env, new Input(@"C:\source", @"C:\output")), Is.EqualTo(1)); Assert.That(env.Calls, Is.Empty);
    }
    [TestCase(false, false, "OK development seed already applied")]
    [TestCase(true, false, "DONE development seed")]
    [TestCase(true, true, "DONE development seed (opening pages added)")]
    public void Seed_message_uses_sql_result(bool changed, bool opening, string expected) => Assert.That(DevSetupRunner.SeedMessage(changed, opening), Is.EqualTo(expected));
    [Test]
    public void Login_sql_has_only_windows_login_and_dbcreator()
    {
        var sql = ServerSetupModel.AppLoginSql(@"DOMAIN\account");
        Assert.That(sql, Does.Contain("FROM WINDOWS").And.Contain("ALTER SERVER ROLE [dbcreator]").And.Not.Contain("CREATE USER").And.Not.Contain("PASSWORD"));
        Assert.Throws<CliRefusal>(() => ServerSetupModel.AppLoginSql("account'];DROP"));
    }
    [Test]
    public async Task Update_and_remove_require_ownership()
    {
        var env = new Fake();
        Assert.That(await ServerSetupRunner.Execute(["update", "--yes"], env, new Input()), Is.EqualTo(1));
        Assert.That(await ServerSetupRunner.Execute(["remove", "--yes"], env, new Input()), Is.EqualTo(1)); Assert.That(env.Calls, Is.Empty);
    }
    [TestCase(365, 200, true, 0)][TestCase(10, 503, true, 2)][TestCase(-1, 200, true, 2)][TestCase(365, 200, false, 2)]
    public async Task Check_is_readonly_and_reports_health_expiry_and_acl(int days, int http, bool acl, int code)
    {
        var env = new Fake(); await Install(env); env.Calls.Clear(); env.Saved.Clear();
        env.Expiry = DateTimeOffset.UtcNow.AddDays(days); env.HttpCode = http; env.ReadKey = true; env.ReadSecrets = acl;
        env.Files[Path.Combine(env.StateRoot, "secrets", "webhook-secrets.json")] = "{}"; // apply already created it
        var c = new Input(); Assert.That(await ServerSetupRunner.Execute(["check", "--yes"], env, c), Is.EqualTo(code));
        Assert.That(env.Calls, Is.Empty); Assert.That(env.Saved, Is.Empty);
        if (days < 30) Assert.That(string.Join('\n', c.Output), Does.Contain("fewer than 30 days"));
    }
}
