using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Security.AccessControl;
using ISTDocAi.Provisioning;
using Microsoft.Data.SqlClient;

namespace ISTDocAi.DevSetup;

public sealed record SetupOptions(string? ISTDocAi, string? Kit, bool Yes, bool Rotate, bool SkipAzure, bool Help, string? ProtectedFile, string? SqlServer, string? BackupDirectory, string? ExportSql = null)
{
    public static SetupOptions Parse(string[] args)
    {
        var flags = new HashSet<string>(); var values = new Dictionary<string,string>();
        for (int i=0;i<args.Length;i++)
        {
            var arg=args[i];
            if (new[]{"--yes","--rotate","--skip-azure","--help"}.Contains(arg))
            { if (!flags.Add(arg)) throw new CliRefusal("Duplicate option."); }
            else if (new[]{"--istdocai","--kit","--protected-file","--sql-server","--backup-directory","--export-sql"}.Contains(arg) && i+1<args.Length && !args[i+1].StartsWith("--",StringComparison.Ordinal))
            { if (!values.TryAdd(arg,args[++i])) throw new CliRefusal("Duplicate option."); }
            else throw new CliRefusal("Unknown or incomplete option. Run --help.");
        }
        return new(values.GetValueOrDefault("--istdocai"),values.GetValueOrDefault("--kit"),flags.Contains("--yes"),flags.Contains("--rotate"),flags.Contains("--skip-azure"),flags.Contains("--help"),values.GetValueOrDefault("--protected-file"),values.GetValueOrDefault("--sql-server"),values.GetValueOrDefault("--backup-directory"),values.GetValueOrDefault("--export-sql"));
    }
}

public static class DevSetupRunner
{
    public const string Help = """
        ISTDocAi.DevSetup.exe [--istdocai FOLDER] [--kit FOLDER] [--yes] [--rotate] [--skip-azure] [--help]
        --istdocai and --kit skip the folder questions; pasted paths may include quotes.
        --export-sql FOLDER exports both embedded SQL files without elevation or setup changes.
        Close the kit solution in Visual Studio first. Run as administrator.
        --yes skips final confirmation only. --rotate explicitly replaces inconsistent credentials.
        Advanced disposable-test overrides: --protected-file PATH --sql-server SERVER --backup-directory FOLDER
        Exit: 0 success; 1 invalid/refused; 2 environment/action needed; 3 cancelled.
        """;
    public static async Task<int> Execute(string[] args, SetupEnvironment environment, IProvisionConsole console)
    {
        try
        {
            var options=SetupOptions.Parse(args);
            if(options.Help) { console.WriteLine(Help); return 0; }
            if(options.ExportSql is not null) { ExportSql(options.ExportSql); console.WriteLine("DONE SQL exported"); return 0; }
            if(!environment.Elevated) throw new CliEnvironment("Run DevSetup as administrator.");
            return await Run(options,environment,console);
        }
        catch(SqlException e) when(e.Number is >=50000 and <=51999) { console.WriteLine("REFUSED: "+e.Message); return 1; }
        catch(CliCancelled) { console.WriteLine("Cancelled."); return 3; }
        catch(CliRefusal e) { console.WriteLine(e.Message); return 1; }
        catch(CliEnvironment e) { console.WriteLine("ACTION NEEDED: "+e.Message); return 2; }
        catch(Exception) { console.WriteLine("ACTION NEEDED: Setup failed; details suppressed to protect secrets. Check configuration, permissions and SQL availability, then rerun. Use --rotate only after reviewing partial state."); return 2; }
        finally { ConsoleLifetime.Pause(environment.PauseAtExit,console); }
    }
    public static string SelectFolder(string? selected, bool kit, IProvisionConsole console)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var input = attempt == 0 && selected is not null ? selected : CliPrompts.Ask(console,
                kit ? "Kit folder (ForwardingUniFreight.Solution or Forwarding.MvcApp)" : "ISTDocAi folder (contains ISTDocAi.sln)");
            try
            {
                var folder = Path.GetFullPath(input.Trim().Trim('"'));
                if (kit)
                {
                    var project = Directory.Exists(Path.Combine(folder, "Forwarding.MvcApp")) ? Path.Combine(folder, "Forwarding.MvcApp") : folder;
                    if (IisDiscovery.Candidate(project, true)) return project;
                    console.WriteLine("Invalid Kit folder: Forwarding.MvcApp/Web.config must reference App_Data\\docai.secrets.config.");
                }
                else if (File.Exists(Path.Combine(folder, "ISTDocAi.sln")) &&
                    File.Exists(Path.Combine(folder, "ISTDocAi.UI", "ISTDocAi.UI.Server", "Properties", "launchSettings.json"))) return folder;
                else console.WriteLine("Invalid ISTDocAi folder: ISTDocAi.sln and server Properties/launchSettings.json are required.");
            }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
            { console.WriteLine("Invalid folder path; enter an accessible folder."); }
        }
        throw new CliEnvironment("Folder validation failed after 3 attempts.");
    }
    public static JsonObject ReadJson(string path) => File.Exists(path)
        ? JsonNode.Parse(File.ReadAllText(path),null,new JsonDocumentOptions { CommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true })?.AsObject() ?? throw new CliRefusal("Invalid JSON configuration.") : new();
    public static void Merge(JsonObject root, string[] path, JsonNode value)
    {
        foreach(var key in path[..^1])
        { if(root[key] is null) root[key]=new JsonObject(); root=root[key]!.AsObject(); }
        root[path[^1]]=value;
    }
    private static void SaveJson(string path, JsonObject json, string account)
    {
        var bytes=Encoding.UTF8.GetBytes(json.ToJsonString(new JsonSerializerOptions {WriteIndented=true})+Environment.NewLine);
        PrivateFiles.AtomicWrite(path,bytes,(FileSecurity)PrivateFiles.WebhookAcl(account,false));
    }
    public static Stream KitSettings() => typeof(DevSetupRunner).Assembly.GetManifestResourceStream("ISTDocAi.DevSetup.KitSettings.xml")??throw new InvalidOperationException();
    public static int KitPort(string xml)
    {
        var text=IisDiscovery.Xml(xml).Descendants().FirstOrDefault(e=>e.Name.LocalName=="IISExpressSSLPort")?.Value;
        return int.TryParse(text,out var port)&&port is >0 and <=65535?port:44324;
    }
    private static void EnsureIgnored(string kit,SetupEnvironment env)
    {
        try
        {
            var result=env.Process("git","-C",kit,"check-ignore","--","App_Data/docai.secrets.config");
            if(result.Code==0) return;
            // A fake folder is not a worktree. Fall back only when Git cannot inspect a repository.
            if(result.Code!=128) throw new CliRefusal("Kit secrets path is not ignored by Git.");
        }
        catch(System.ComponentModel.Win32Exception) { }
        var ignored=false;
        for(var folder=new DirectoryInfo(kit);folder is not null;folder=folder.Parent)
        {
            var file=Path.Combine(folder.FullName,".gitignore");
            if(!File.Exists(file)) continue;
            var relative=Path.GetRelativePath(folder.FullName,Path.Combine(kit,"App_Data","docai.secrets.config")).Replace('\\','/');
            foreach(var line in File.ReadLines(file).Select(l=>l.Trim()).Where(l=>l.Length>0&&!l.StartsWith('#')))
            {
                var negative=line.StartsWith('!'); var pattern=negative?line[1..]:line;
                // Deliberately accept only exact reviewed paths or the secret filename in fallback mode.
                if(pattern.TrimStart('/')==relative || pattern=="docai.secrets.config") ignored=!negative;
            }
            if(File.Exists(Path.Combine(folder.FullName,"ISTDocAi.sln"))) break;
        }
        if(!ignored) throw new CliRefusal("Cannot prove kit secrets path is ignored. Add an explicit .gitignore entry first.");
    }
    private static async Task Azure(string localPath,JsonObject local,SetupEnvironment env,IProvisionConsole console)
    {
        var identity=local["DocAi"]?["Identity"];
        if(identity?["Mode"]?.GetValue<string>()=="ClientCertificate" && identity["StoreLocation"]?.GetValue<string>()=="CurrentUser"
            && Guid.TryParse(identity["TenantId"]?.GetValue<string>(),out _) && Guid.TryParse(identity["ClientId"]?.GetValue<string>(),out _)
            && identity["CertificateThumbprint"]?.GetValue<string>() is {Length:40} thumb && thumb.All(Uri.IsHexDigit) && env.CertificateReady(thumb))
        { console.WriteLine("OK Azure certificate identity"); return; }
        var path=CliPrompts.Ask(console,"PFX path (provided by Anter)").Trim().Trim('"');
        var sidecar=Path.ChangeExtension(path,".json");
        if(!File.Exists(sidecar)) sidecar=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,"docai-dev-identity.json");
        var metadata=ReadJson(sidecar);
        var tenant=metadata["tenantId"]?.GetValue<string>()??CliPrompts.Ask(console,"TenantId");
        var client=metadata["clientId"]?.GetValue<string>()??CliPrompts.Ask(console,"ClientId");
        if(!Guid.TryParse(tenant,out _)||!Guid.TryParse(client,out _)) throw new CliRefusal("TenantId and ClientId must be GUIDs.");
        console.Write("PFX password: "); var password=CliPrompts.Hidden(console);
        var imported=env.ImportCertificate(path,password);
        Merge(local,["DocAi","Identity"],new JsonObject { ["Mode"]="ClientCertificate",["StoreLocation"]="CurrentUser",["TenantId"]=tenant,["ClientId"]=client,["CertificateThumbprint"]=imported });
        SaveJson(localPath,local,env.Account); console.WriteLine("DONE Azure certificate identity");
        await Task.CompletedTask;
    }
    public static byte[] SqlBytes(string name)
    {
        using var stream = typeof(DevSetupRunner).Assembly.GetManifestResourceStream("ISTDocAi.DevSetup." + name)
            ?? throw new InvalidOperationException("Embedded SQL unavailable.");
        using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray();
    }
    public static string SeedSql() => SqlText("ISTDocAi-DevSeed.sql");
    public static string SqlText(string name)
    {
        using var stream = new MemoryStream(SqlBytes(name));
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    public static void ExportSql(string folder)
    {
        folder = Path.GetFullPath(folder.Trim().Trim('"'));
        string[] names = ["ISTDocAi-DevSeed.sql", "CreateAIDocumentTables.sql"];
        var contents = names.Select(SqlBytes).ToArray();
        if (names.Any(name => File.Exists(Path.Combine(folder, name)) || Directory.Exists(Path.Combine(folder, name))))
            throw new CliRefusal("SQL export refuses to overwrite existing files.");
        Directory.CreateDirectory(folder);
        var created = new List<string>();
        try
        {
            for (var i = 0; i < names.Length; i++)
            {
                var path = Path.Combine(folder, names[i]);
                using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                created.Add(path); file.Write(contents[i]);
            }
        }
        catch
        {
            foreach (var path in created) File.Delete(path);
            throw;
        }
    }
    public static SqlCommand SeedCommand(SqlConnection connection,string hash,string name,string callback,bool rotate)
    {
        var sql=SeedSql(); var begin=sql.IndexOf("-- BEGIN SETTINGS",StringComparison.Ordinal); var end=sql.IndexOf("-- END SETTINGS",StringComparison.Ordinal);
        var command=connection.CreateCommand(); command.CommandTimeout=120;
        command.CommandText=sql[..begin]+"""
            DECLARE @ConsumerCode varchar(100)='FWD-UNF',@ProductCode varchar(100)='FWD',@TenantCode varchar(100)='UNF',@Name nvarchar(max)=N'Forwarding Local',
             @ApiKeyHash varchar(max)=@HashInput,@WebhookSecretName varchar(max)=@SecretInput,@CallbackUrl nvarchar(max)=@CallbackInput,
             @MaxPagesPerJob int=200,@DailyPageCap int=2000,@QuotaMode varchar(30)='Prepaid',@QuotaWarningPercent int=10,@OpeningPages bigint=100,@RotateKey bit=@RotateInput;
            """+sql[(end+"-- END SETTINGS".Length)..];
        command.Parameters.Add("@HashInput",System.Data.SqlDbType.VarChar,64).Value=hash;
        command.Parameters.Add("@SecretInput",System.Data.SqlDbType.VarChar,127).Value=name;
        command.Parameters.Add("@CallbackInput",System.Data.SqlDbType.NVarChar,400).Value=callback;
        command.Parameters.Add("@RotateInput",System.Data.SqlDbType.Bit).Value=rotate;
        return command;
    }
    public static async Task<int> Run(SetupOptions options,SetupEnvironment env,IProvisionConsole console)
    {
        var repo=SelectFolder(options.ISTDocAi,false,console); var kit=SelectFolder(options.Kit,true,console);
        console.WriteLine("Close the kit solution in Visual Studio before continuing.");
        EnsureIgnored(kit,env);
        CliPrompts.Confirm(console,options.Yes);
        var serverFolder=Path.Combine(repo,"ISTDocAi.UI","ISTDocAi.UI.Server");
        var localPath=Path.Combine(serverFolder,"appsettings.Local.json"); var local=ReadJson(localPath);
        var development=ReadJson(Path.Combine(serverFolder,"appsettings.Development.json"));
        var connectionText=local["ConnectionStrings"]?["DefaultConnection"]?.GetValue<string>()??development["ConnectionStrings"]?["DefaultConnection"]?.GetValue<string>()??throw new CliRefusal("DefaultConnection is missing.");
        var builder=new SqlConnectionStringBuilder(connectionText);
        var selected=options.SqlServer??CliPrompts.Ask(console,"SQL Server",builder.DataSource);
        if(selected!=builder.DataSource) { builder.DataSource=selected; Merge(local,["ConnectionStrings","DefaultConnection"],JsonValue.Create(builder.ConnectionString)!); SaveJson(localPath,local,env.Account); }
        if(options.SkipAzure) console.WriteLine("ACTION NEEDED: Azure identity skipped."); else await Azure(localPath,local,env,console);
        if(env.Process("dotnet","dev-certs","https","--check","--trust").Code!=0 && env.Process("dotnet","dev-certs","https","--trust").Code!=0)
            throw new CliEnvironment("Trust the HTTPS development certificate, then rerun.");
        console.WriteLine("OK HTTPS development certificate");
        var userPath=Path.Combine(kit,"Forwarding.MvcApp.csproj.user");
        if(!File.Exists(userPath))
        {
            using var content=KitSettings();
            using var file=new FileStream(userPath,FileMode.CreateNew,FileAccess.Write,FileShare.None); content.CopyTo(file);
            console.WriteLine("DONE kit Visual Studio settings");
        }
        var userXml=File.ReadAllText(userPath); var port=KitPort(userXml);
        if(port!=44324||!IisDiscovery.Xml(userXml).Descendants().Any(e=>e.Name.LocalName=="IISExpressSSLPort")) console.WriteLine("ACTION NEEDED: Kit SSL port differs or is missing; callback uses port "+port+".");
        var callback=$"https://localhost:{port}/api/DocAi/Webhook";
        using var connection=env.Connection(builder.ConnectionString);
        try { await connection.OpenAsync(); }
        catch(SqlException e) when(e.Number==4060) { return MissingDatabase(console); }
        catch(SqlException e) when(e.Number is <50000 or >51999) { throw new CliEnvironment($"Cannot connect to SQL Server '{builder.DataSource}'. Check the server name and that SQL Server is running, then rerun."); }
        using(var readiness=connection.CreateCommand())
        {
            readiness.CommandText="IF OBJECT_ID('dbo.__EFMigrationsHistory','U') IS NULL SELECT 0 ELSE EXEC(N'SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId=''20261103000000_ModelMappingProfiles''')";
            if(Convert.ToInt32(await readiness.ExecuteScalarAsync())==0) return MissingDatabase(console);
        }
        var launch=ReadJson(Path.Combine(serverFolder,"Properties","launchSettings.json"));
        var protectedPath=options.ProtectedFile??launch["profiles"]?["ISTDocAiLocal"]?["environmentVariables"]?["DocAi__Secrets__ProtectedFile__Path"]?.GetValue<string>()??throw new CliRefusal("Protected-file launch setting missing.");
        var secretPath=Path.Combine(kit,"App_Data","docai.secrets.config"); var kitSecrets=KitSecretsFile.Load(secretPath);
        string? existingHash=null,existingName=null;
        using(var query=connection.CreateCommand())
        {
            query.CommandText="SELECT CONVERT(varchar(64),ApiKeyHash1,2),WebhookSecretName FROM dbo.Consumers WHERE Code='FWD-UNF'";
            using var reader=await query.ExecuteReaderAsync();
            if(await reader.ReadAsync()) { existingHash=reader.GetString(0); existingName=reader.IsDBNull(1)?null:reader.GetString(1); }
        }
        var api=kitSecrets.Get("DocAi:ApiKey"); var webhook=kitSecrets.Get("DocAi:WebhookSecret1");
        var matches=api is not null && webhook is not null && existingHash==Credentials.HashApiKey(api) && existingName is not null && ProtectedSecretsFile.Matches(protectedPath,existingName,webhook);
        var pristine=existingHash is null&&!File.Exists(secretPath)&&!File.Exists(protectedPath);
        if(!matches&&!pristine&&!options.Rotate) throw new CliRefusal("Partial or inconsistent credentials. Review state and rerun with --rotate to replace them with private backups.");
        if(!matches||options.Rotate)
        { api=Credentials.NewApiKey(); webhook=Credentials.NewWebhookKey(); existingName=Credentials.SecretName(Credentials.NewSetupId()); }
        using(var seed=SeedCommand(connection,Credentials.HashApiKey(api!),existingName!,callback,options.Rotate))
        {
            using var seedResult=await seed.ExecuteReaderAsync();
            if (!await seedResult.ReadAsync()) throw new CliEnvironment("Development seed did not return its result.");
            console.WriteLine(SeedMessage(seedResult.GetBoolean(0),seedResult.GetBoolean(1)));
        }
        ProtectedSecretsFile.Install(protectedPath,existingName!,webhook!,env.Account);
        if(!matches||options.Rotate||kitSecrets.Get("DocAi:Enabled")!="true")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(secretPath)!);
            kitSecrets.SetDevelopmentCredentials(api!,webhook!);
            kitSecrets.Save(secretPath,KitSecretsFile.CreateAcl(env.Account),options.BackupDirectory);
            console.WriteLine("DONE kit credentials");
        } else console.WriteLine("OK credentials unchanged");
        var web=IisDiscovery.Xml(File.ReadAllText(Path.Combine(kit,"Web.config")));
        var companyText=(string?)web.Root?.Element("connectionStrings")?.Elements("add").SingleOrDefault(e=>string.Equals((string?)e.Attribute("name"),"ConnectionString",StringComparison.OrdinalIgnoreCase))?.Attribute("connectionString")??throw new CliRefusal("Kit company ConnectionString is missing.");
        using var company=env.Connection(companyText);
        try { await company.OpenAsync(); }
        catch(SqlException e) when(e.Number is <50000 or >51999) { throw new CliEnvironment("Cannot connect to the kit company database from Web.config. Check the server name and that SQL Server is running, then rerun."); }
        using(var command=company.CreateCommand())
        { command.CommandTimeout=120; command.CommandText=SqlText("CreateAIDocumentTables.sql"); await command.ExecuteNonQueryAsync(); }
        var result=CompanyIdentityCheck.Query(company.ConnectionString,"FWD");
        var decision=CompanyIdentityCheck.Decide(result,"FWD","UNF");
        if(!decision.Allowed) throw new CliRefusal(decision.Message);
        console.WriteLine("OK "+decision.Message);
        console.WriteLine("Restart ISTDocAi and the kit, then test Extract with AI");
        return options.SkipAzure?2:0;
    }
    public static string SeedMessage(bool changed, bool openingAdded) => openingAdded ? "DONE development seed (opening pages added)" : changed ? "DONE development seed" : "OK development seed already applied";
    private static int MissingDatabase(IProvisionConsole console)
    {
        console.WriteLine("ACTION NEEDED: Start ISTDocAi once from Visual Studio (profile ISTDocAiLocal) so it creates its database, then run DevSetup again"); return 2;
    }
}
