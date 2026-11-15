using NUnit.Framework;
using ISTDocAi.DevSetup;
using ISTDocAi.Provisioning;
using ISTDocAi.Infrastructure.Data.DocAi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using System.Security.Cryptography;

namespace ISTDocAi.Setup.Tests;

public sealed class DevSetupPureTests
{
    [TestCase("--unknown")][TestCase("--repo")][TestCase("--yes --yes")]
    public void Invalid_options_are_refused(string input) => Assert.Throws<CliRefusal>(()=>SetupOptions.Parse(input.Split(' ')));
    [Test]
    public void Help_describes_main_options_and_advanced_overrides()
    {
        Assert.That(SetupOptions.Parse(["--help"]).Help,Is.True);
        Assert.That(DevSetupRunner.Help,Does.Contain("--rotate").And.Contain("Advanced"));
    }
    [TestCase("44324",44324)][TestCase("44500",44500)][TestCase("",44324)][TestCase("99999",44324)]
    public void Kit_port_is_read_without_modifying_xml(string text,int expected) =>
        Assert.That(DevSetupRunner.KitPort($"<Project><IISExpressSSLPort>{text}</IISExpressSSLPort></Project>"),Is.EqualTo(expected));
    [Test]
    public void Merge_preserves_other_configuration()
    {
        var json=JsonNode.Parse("{\"Other\":42,\"DocAi\":{\"Keep\":true}}")!.AsObject();
        DevSetupRunner.Merge(json,["DocAi","Identity"],new JsonObject { ["Mode"]="ClientCertificate" });
        Assert.That(json["Other"]!.GetValue<int>(),Is.EqualTo(42));
        Assert.That(json["DocAi"]!["Keep"]!.GetValue<bool>(),Is.True);
    }
    [Test]
    public void Embedded_kit_settings_preserve_agreed_bytes()
    {
        using var stream=DevSetupRunner.KitSettings();
        using var bytes=new MemoryStream(); stream.CopyTo(bytes);
        var source=Path.Combine(RepositoryPaths.SetupRoot,"src","ISTDocAi.DevSetup","KitSettings.xml");
        Assert.That(bytes.ToArray(),Is.EqualTo(File.ReadAllBytes(source)));
        Assert.That(DevSetupRunner.KitPort(Encoding.UTF8.GetString(bytes.ToArray()).TrimStart('\uFEFF')),Is.EqualTo(44324));
    }
    [Test]
    public void Seed_passes_values_as_parameters_and_contains_no_raw_key()
    {
        using var connection=new SqlConnection();
        using var command=DevSetupRunner.SeedCommand(connection,new string('A',64),"webhook-test","https://localhost:44324/api/DocAi/Webhook",false);
        Assert.That(command.Parameters.Count,Is.EqualTo(4));
        Assert.That(command.CommandText,Does.Not.Contain(new string('A',64)).And.Not.Contain("webhook-test"));
    }
}

[Category("Sql")]
[NonParallelizable]
public sealed class DevSetupSqlTests : GuardedToolTests
{
    private string root="", master="", service="", company="", folder="";
    private string[] databases=[];
    [SetUp]
    public async Task Create()
    {
        root=RepositoryPaths.ISTDocAiRoot;
        // Under %TEMP%: the user owns the parent, so non-elevated test runs can replace files whose
        // private ACL grants the user Read only (real DevSetup runs elevated through its manifest).
        folder=Path.Combine(Path.GetTempPath(),"DevSetupTest_"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var builder=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("DOCAI_TEST_SQL"));
        // Refuse accidental execution on another server or with SQL credentials.
        Assert.That(builder.DataSource,Is.EqualTo("localhost\\MSSQLSERVER01").IgnoreCase);
        Assert.That(builder.IntegratedSecurity,Is.True);
        builder.InitialCatalog="master"; master=builder.ConnectionString;
        databases=["DevSetupTest_"+Guid.NewGuid().ToString("N"),"DevSetupTest_"+Guid.NewGuid().ToString("N")];
        foreach(var name in databases) await Execute(master,$"CREATE DATABASE [{name}]");
        builder.InitialCatalog=databases[0]; service=builder.ConnectionString;
        builder.InitialCatalog=databases[1]; company=builder.ConnectionString;
        using var db=new DocAiDbContext(new DbContextOptionsBuilder<DocAiDbContext>().UseSqlServer(service,sql=>sql.CommandTimeout(300)).Options);
        await db.Database.MigrateAsync();
        await Execute(company,"CREATE TABLE dbo.Customers(ID int PRIMARY KEY); CREATE TABLE dbo.Defaults(ID int PRIMARY KEY,CompanyName nvarchar(200),UnEditableCompanyName nvarchar(200)); INSERT dbo.Defaults VALUES(1,N'Disposable company',N'UNF');");
    }
    [TearDown]
    public async Task Cleanup()
    {
        try
        {
            SqlConnection.ClearAllPools();
            foreach(var name in databases)
            {
                if(!System.Text.RegularExpressions.Regex.IsMatch(name,"\\ADevSetupTest_[a-f0-9]{32}\\z")) throw new InvalidOperationException();
                await Execute(master,$"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END");
            }
        }
        finally
        {
            if(folder.Length>0&&Path.GetFullPath(folder).StartsWith(Path.Combine(Path.GetTempPath(),"DevSetupTest_"),StringComparison.OrdinalIgnoreCase)) Directory.Delete(folder,true);
        }
    }
    private static async Task Execute(string cs,string sql)
    { using var c=new SqlConnection(cs); await c.OpenAsync(); using var cmd=new SqlCommand(sql,c){CommandTimeout=300}; await cmd.ExecuteNonQueryAsync(); }
    [Test]
    public async Task Kit_status_constraint_upgrades_old_shape_and_replays()
    {
        var sql = KitDatabase.Sql("FWD", "UNF");
        await Execute(company, sql);
        await Execute(company, "ALTER TABLE dbo.AIDocumentExtraction DROP CONSTRAINT CK_AIDocumentExtraction_ExtractionStatus; ALTER TABLE dbo.AIDocumentExtraction WITH CHECK ADD CONSTRAINT CK_AIDocumentExtraction_ExtractionStatus CHECK (ExtractionStatus IN ('Pending', 'Submitted', 'Completed', 'Failed')); ");
        await Execute(company, sql);
        await Execute(company, sql);
        await Execute(company, "INSERT dbo.AIDocumentExtraction(ID, ExtractionStatus, RequestedDocumentTypeCode, StoredFileName, OriginalFileName, FileSha256) VALUES(NEWID(), 'Cancelled', 'AUTO', 'fixture.pdf', N'fixture.pdf', CONVERT(binary(32), 1));");
        using var c = new SqlConnection(company); await c.OpenAsync();
        using var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.AIDocumentExtraction WHERE ExtractionStatus = 'Cancelled'; SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_AIDocumentExtraction_ExtractionStatus' AND is_disabled = 0 AND is_not_trusted = 0;", c);
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True); Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(await reader.NextResultAsync(), Is.True); Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
    }

    private async Task<string> Snapshot()
    {
        using var c=new SqlConnection(service); await c.OpenAsync(); var result=new StringBuilder();
        foreach(var table in new[]{"Consumers","ModelRegistry","ClassifierClasses","ModelMappingBindings","MappingProfiles","MappingFields","PageAllowanceGrants","PageQuotaOperatorRequests"})
        { using var cmd=new SqlCommand($"SELECT * FROM dbo.[{table}] FOR JSON PATH,INCLUDE_NULL_VALUES",c); using var reader=await cmd.ExecuteReaderAsync(); while(await reader.ReadAsync()) result.Append(reader.GetString(0)); }
        return result.ToString();
    }
    private async Task Seed(string hash,string name,bool rotate=false)
    { using var c=new SqlConnection(service); await c.OpenAsync(); using var cmd=DevSetupRunner.SeedCommand(c,hash,name,"https://localhost:44324/api/DocAi/Webhook",rotate); await cmd.ExecuteNonQueryAsync(); }
    [Test]
    public async Task Seed_result_reports_opening_pages_changes_and_replay()
    {
        async Task<string> Result(string hash, string name, bool rotate = false)
        {
            using var connection = new SqlConnection(service); await connection.OpenAsync();
            using var command = DevSetupRunner.SeedCommand(connection, hash, name, "https://localhost:44324/api/DocAi/Webhook", rotate);
            using var reader = await command.ExecuteReaderAsync(); Assert.That(await reader.ReadAsync(), Is.True);
            return DevSetupRunner.SeedMessage(reader.GetBoolean(0), reader.GetBoolean(1));
        }
        Assert.That(await Result(new string('A',64), "webhook-test"), Is.EqualTo("DONE development seed (opening pages added)"));
        Assert.That(await Result(new string('A',64), "webhook-test"), Is.EqualTo("OK development seed already applied"));
        Assert.That(await Result(new string('B',64), "webhook-new", true), Is.EqualTo("DONE development seed"));
    }
    [Test]
    public async Task Seed_replays_without_row_changes_refuses_mismatch_and_rotates_without_topup()
    {
        await Seed(new string('A',64),"webhook-test"); var first=await Snapshot();
        await Seed(new string('A',64),"webhook-test"); Assert.That(await Snapshot(),Is.EqualTo(first));
        var error=Assert.ThrowsAsync<SqlException>(()=>Seed(new string('B',64),"webhook-rotated"))!;
        Assert.That(error.Number,Is.EqualTo(50018));
        Assert.That(error.Message,Does.Contain("Existing hash, secret name or callback differs"));
        Assert.That(await Snapshot(),Is.EqualTo(first));
        await Seed(new string('B',64),"webhook-rotated",true);
        using var c=new SqlConnection(service); await c.OpenAsync();
        using var cmd=new SqlCommand("SELECT COUNT(*) FROM dbo.PageAllowanceGrants",c);
        Assert.That(await cmd.ExecuteScalarAsync(),Is.EqualTo(1));
    }
    [Test]
    public async Task Seed_conflicting_model_rolls_back_consumer_and_topup()
    {
        await Execute(service,"INSERT dbo.ModelRegistry(ModelId,DocumentTypeCode,Kind,Version,Status) VALUES('bl-v3','BL','extract',9,'Testing')");
        var before=await Snapshot(); var error=Assert.ThrowsAsync<SqlException>(()=>Seed(new string('A',64),"webhook-test"))!;
        Assert.That(error.Number,Is.EqualTo(50018));
        Assert.That(error.Message,Does.Contain("Existing model differs"));
        Assert.That(await Snapshot(),Is.EqualTo(before));
    }
    [Test]
    [Category("Machine")]
    public async Task DevSetup_twice_on_disposable_state_changes_nothing_on_second_run()
    {
        var fakeRepo=Path.Combine(folder,"repo"); var kit=Path.Combine(folder,"kit","Forwarding.MvcApp");
        var server=Path.Combine(fakeRepo,"ISTDocAi.UI","ISTDocAi.UI.Server");
        Directory.CreateDirectory(Path.Combine(server,"Properties")); Directory.CreateDirectory(Path.Combine(kit,"DocAi","Sql"));
        File.WriteAllText(Path.Combine(fakeRepo,"ISTDocAi.sln"),"");
        File.WriteAllText(Path.Combine(server,"appsettings.Development.json"),new JsonObject { ["ConnectionStrings"]=new JsonObject{["DefaultConnection"]=service} }.ToJsonString());
        File.WriteAllText(Path.Combine(server,"appsettings.Local.json"),new JsonObject { ["Other"]="preserved",["DocAi"]=new JsonObject { ["Identity"]=new JsonObject { ["Mode"]="ClientCertificate",["StoreLocation"]="CurrentUser",["TenantId"]=Guid.NewGuid().ToString(),["ClientId"]=Guid.NewGuid().ToString(),["CertificateThumbprint"]=new string('A',40) } } }.ToJsonString());
        File.WriteAllText(Path.Combine(server,"Properties","launchSettings.json"),"{}");
        File.WriteAllText(Path.Combine(kit,".gitignore"),"App_Data/docai.secrets.config\n");
        File.WriteAllText(Path.Combine(kit,"Web.config"),new XDocument(new XElement("configuration",new XElement("appSettings",new XAttribute("file","App_Data\\docai.secrets.config")),new XElement("connectionStrings",new XElement("add",new XAttribute("name","ConnectionString"),new XAttribute("connectionString",company))))).ToString());
        var env=new FakeEnvironment(); var console=new FakeConsole();
        string[] args=["--istdocai",fakeRepo,"--kit",kit,"--yes","--protected-file",Path.Combine(folder,"protected","webhooks.json"),"--backup-directory",Path.Combine(folder,"backups")];
        Assert.That(await DevSetupRunner.Execute(args,env,console),Is.Zero,string.Join('\n',console.Lines));
        var before=await Snapshot(); var files=Files();
        Assert.That(await DevSetupRunner.Execute(args,env,console),Is.Zero,string.Join('\n',console.Lines));
        Assert.That(await Snapshot(),Is.EqualTo(before)); Assert.That(Files(),Is.EqualTo(files));
        Assert.That(console.Lines,Does.Contain("OK credentials unchanged"));
        TestContext.Progress.WriteLine("DevSetup disposable run 1: exit 0; run 2: exit 0; all file bytes and timestamps + database rows unchanged; one opening top-up.");
        string[] Files()=>Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Order().Select(p=>p+"|"+File.GetLastWriteTimeUtc(p).Ticks+"|"+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).ToArray();
    }
    private sealed class FakeEnvironment:SetupEnvironment
    {
        public override bool Elevated=>true;
        public override bool PauseAtExit=>false;
        public override bool CertificateReady(string thumbprint)=>true;
        public override string ImportCertificate(string path,string password)=>throw new AssertionException("No store changes allowed");
        public override ProcessResult Process(string executable,params string[] arguments)
        {
            if(executable=="dotnet") return new(0,[]);
            if(arguments.Contains("check-ignore")) return new(128,[]);
            throw new AssertionException("Unexpected process");
        }
    }
    private sealed class FakeConsole:IProvisionConsole
    {
        public List<string> Lines {get;}=[];
        public string? ReadLine()=>"";
        public ConsoleKeyInfo ReadKey()=>throw new AssertionException("No secret prompt expected");
        public void Write(string text) { }
        public void WriteLine(string text)=>Lines.Add(text);
    }
}
