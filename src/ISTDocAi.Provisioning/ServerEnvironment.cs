using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace ISTDocAi.Provisioning;

public sealed record SetupProcess(int Code, string Output);
public sealed record TlsCertificate(string Subject, DateTimeOffset Expires, string Thumbprint);
public class ServerEnvironment
{
    public virtual string StateRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ISTDocAi");
    public string JournalPath => Path.Combine(StateRoot, "setup", "state.json");
    public virtual string ExecutableFolder => AppContext.BaseDirectory;
    public virtual string ExecutablePath => Environment.ProcessPath ?? throw new CliEnvironment("Executable path unavailable.");
    public virtual bool Elevated => OperatingSystem.IsWindows() && new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    public virtual bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
    public virtual string Read(string path) => File.ReadAllText(path);
    public virtual byte[] ReadBytes(string path) => File.ReadAllBytes(path);
    public virtual void WriteBytes(string path, byte[] bytes)
    {
        Plain(path);
        var staging = path + ".identity-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var acl = new FileInfo(path).GetAccessControl();
            using (var stream = FileSystemAclExtensions.Create(new FileInfo(staging), FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.None, acl)) { stream.Write(bytes); stream.Flush(true); }
            File.Replace(staging, path, null);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
    public virtual void Write(string path, string content)
    {
        Plain(path); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllText(path, content, new UTF8Encoding(false));
    }
    public virtual void CreateFolder(string path) { Plain(path); Directory.CreateDirectory(path); }
    // Server-level IIS settings can push environment variables (connection strings, signing keys) into every
    // ASP.NET Core site, overriding appsettings.Production.json. <clear/> blocks the inherited ones for this site;
    // ASPNETCORE_ENVIRONMENT stays on the application pool.
    public virtual void IsolateEnvironment(string folder)
    {
        var path = Path.Combine(folder, "web.config"); Plain(path);
        var document = System.Xml.Linq.XDocument.Load(path, System.Xml.Linq.LoadOptions.PreserveWhitespace);
        var aspNetCore = document.Descendants("aspNetCore").SingleOrDefault() ?? throw new CliEnvironment("web.config has no aspNetCore element.");
        var variables = aspNetCore.Element("environmentVariables");
        if (variables is null) { variables = new System.Xml.Linq.XElement("environmentVariables"); aspNetCore.Add(variables); }
        if (variables.Elements().FirstOrDefault()?.Name.LocalName != "clear") variables.AddFirst(new System.Xml.Linq.XElement("clear"));
        document.Save(path);
    }
    public virtual void CopyExecutable(string path) => File.Copy(ExecutablePath, path, false);
    public virtual IEnumerable<string> EnumerateFiles(string folder) => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories);
    public virtual void CopyApp(string source, string target, bool update)
    {
        Plain(source); Plain(target); Directory.CreateDirectory(target);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            Plain(entry); var name = Path.GetFileName(entry); var destination = Path.Combine(target, name);
            if (update && (name.Equals("appsettings.Production.json", StringComparison.OrdinalIgnoreCase) || name.Equals("App_Data", StringComparison.OrdinalIgnoreCase))) continue;
            if (Directory.Exists(entry)) CopyApp(entry, destination, update); else { Plain(destination); File.Copy(entry, destination, true); }
        }
    }
    public virtual void Plain(string path)
    {
        var full = Path.GetFullPath(path);
        for (var p = full; p is not null; p = Path.GetDirectoryName(p))
            if (Exists(p) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new CliRefusal("Linked paths are refused.");
    }
    public virtual IDisposable Lock()
    {
        // File lock is held across awaits; a thread-affine Mutex is unsuitable here.
        var folder = Path.Combine(StateRoot, "setup"); Plain(folder);
        if (!Directory.Exists(folder)) new DirectoryInfo(folder).Create(AdminDirectoryAcl());
        try { return new FileStream(Path.Combine(folder, "command.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new CliRefusal("Another setup command is active. Run commands sequentially."); }
    }
    private static DirectorySecurity AdminDirectoryAcl()
    {
        var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { "S-1-5-18", "S-1-5-32-544" }) acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        return acl;
    }
    public virtual void Save(ServerJournal journal)
    {
        var acl = new FileSecurity(); acl.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { "S-1-5-18", "S-1-5-32-544" }) acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.FullControl, AccessControlType.Allow));
        PrivateFiles.AtomicWrite(JournalPath, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(journal)), acl);
    }
    public virtual SetupProcess Process(string executable, params string[] args)
    {
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new CliEnvironment("Required tool could not start.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(900000)) { process.Kill(true); throw new CliEnvironment("Required tool timed out."); }
        Task.WhenAll(output, error).GetAwaiter().GetResult();
        return new(process.ExitCode, output.Result); // Never return stderr: it may contain credentials.
    }
    protected virtual object CreateIisManager()
    {
        var type = Type.GetTypeFromProgID("Microsoft.ApplicationHost.WritableAdminManager")
            ?? throw new CliEnvironment("IIS configuration API unavailable.");
        return Activator.CreateInstance(type) ?? throw new CliEnvironment("IIS configuration API unavailable.");
    }
    public virtual void SetPoolIdentity(string pool, string account, string password)
    {
        var objects = new List<object>();
        dynamic Track(object value)
        {
            if (!objects.Any(item => ReferenceEquals(item, value))) objects.Add(value);
            return value;
        }
        try
        {
            dynamic manager = Track(CreateIisManager());
            manager.CommitPath = "MACHINE/WEBROOT/APPHOST";
            dynamic section = Track(manager.GetAdminSection("system.applicationHost/applicationPools", "MACHINE/WEBROOT/APPHOST"));
            dynamic pools = Track(section.Collection);
            object? selected = null;
            for (var index = 0; index < (int)pools.Count; index++)
            {
                dynamic element = Track(pools.Item(index));
                dynamic name = Track(element.GetPropertyByName("name"));
                if (!string.Equals((string)name.Value, pool, StringComparison.OrdinalIgnoreCase)) continue;
                if (selected is not null) throw new InvalidOperationException();
                selected = element;
            }
            if (selected is null) throw new InvalidOperationException();
            dynamic model = Track(((dynamic)selected).GetElementByName("processModel"));
            dynamic identity = Track(model.GetPropertyByName("identityType"));
            dynamic userName = Track(model.GetPropertyByName("userName"));
            dynamic secret = Track(model.GetPropertyByName("password"));
            identity.Value = 3; // SpecificUser; IIS encrypts password according to its schema.
            userName.Value = account;
            secret.Value = password;
            manager.CommitChanges();
        }
        catch (Exception) { throw new CliEnvironment("IIS identity configuration failed. Check IIS configuration and permissions."); }
        finally
        {
            password = "";
            for (var index = objects.Count - 1; index >= 0; index--)
            {
                try
                {
                    if (System.Runtime.InteropServices.Marshal.IsComObject(objects[index]))
                        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(objects[index]);
                }
                catch (Exception) { /* Never expose COM cleanup diagnostics containing credentials. */ }
            }
            objects.Clear();
        }
    }
    public virtual bool? LoginDbCreator(ServerInstall i)
    {
        using var connection = new SqlConnection(ServerSetupModel.Connection(i, "master")); connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN SUSER_ID(@name) IS NULL THEN CASE WHEN HAS_PERMS_BY_NAME(NULL, 'SERVER', 'VIEW ANY DEFINITION')=1 THEN 0 ELSE NULL END ELSE IS_SRVROLEMEMBER('dbcreator', @name) END";
        command.Parameters.AddWithValue("@name", i.Account);
        var result = command.ExecuteScalar();
        return result is null or DBNull ? null : Convert.ToInt32(result) == 1;
    }
    public virtual string Iis(params string[] args)
    {
        var result = Process(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "inetsrv", "appcmd.exe"), args);
        if (result.Code != 0) throw new CliEnvironment("IIS operation failed; check IIS configuration and permissions. Details suppressed.");
        return result.Output;
    }
    // appcmd exits non-zero for an unknown identifier, so list every object and compare names instead.
    public virtual bool ResourceExists(string type, string name) => Iis("list", type, "/text:name")
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Any(n => n.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
    public virtual bool BindingExists(ServerInstall i) => Process("netsh.exe", "http", "show", "sslcert", (i.Host.Length > 0 ? "hostnameport=" + i.Host : "ipport=0.0.0.0") + ":" + i.Port).Code == 0;
    public virtual bool BindingMatches(ServerInstall i)
    {
        var result = Process("netsh.exe", "http", "show", "sslcert", (i.Host.Length > 0 ? "hostnameport=" + i.Host : "ipport=0.0.0.0") + ":" + i.Port);
        return result.Code == 0 && result.Output.Contains(i.TlsThumbprint, StringComparison.OrdinalIgnoreCase) && result.Output.Contains("43acf641-a501-4ee7-b710-196fec4da9a0", StringComparison.OrdinalIgnoreCase);
    }
    public virtual bool IisBindingExists(ServerInstall i) => Iis("list", "site", "/text:bindings").Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Any(b => b.Trim().Equals($"https/*:{i.Port}:{i.Host}", StringComparison.OrdinalIgnoreCase));
    public virtual string[] MissingFeatures()
    {
        string[] features = ["IIS-WebServerRole", "IIS-WebServer", "IIS-ManagementConsole", "IIS-ApplicationInit"];
        return features.Where(f => !Process("dism.exe", "/Online", "/English", "/Get-FeatureInfo", "/FeatureName:" + f).Output.Contains("State : Enabled", StringComparison.Ordinal)).ToArray();
    }
    public virtual void EnableFeatures(string[] features)
    {
        var result = Process("dism.exe", ["/Online", "/Enable-Feature", "/All", "/NoRestart", .. features.Select(f => "/FeatureName:" + f)]);
        if (result.Code is not (0 or 3010)) throw new CliEnvironment("IIS feature installation failed.");
        if (result.Code == 3010) throw new CliEnvironment("Restart Windows to finish IIS feature installation, then rerun install.");
    }
    public virtual bool HostingReady()
    {
        var program = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return File.Exists(Path.Combine(program, "IIS", "Asp.Net Core Module", "V2", "aspnetcorev2.dll")) &&
            File.Exists(Path.Combine(program, "dotnet", "dotnet.exe")) &&
            Process(Path.Combine(program, "dotnet", "dotnet.exe"), "--list-runtimes").Output.Contains("Microsoft.AspNetCore.App 10.", StringComparison.Ordinal);
    }
    public virtual IReadOnlyList<TlsCertificate> Certificates()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine); store.Open(OpenFlags.ReadOnly);
        var result = new List<TlsCertificate>();
        foreach (var c in store.Certificates)
        {
            using (c) if (c.HasPrivateKey && c.NotBefore <= DateTime.Now && c.NotAfter > DateTime.Now &&
                c.Extensions.OfType<X509EnhancedKeyUsageExtension>().Any(e => e.EnhancedKeyUsages.Cast<Oid>().Any(o => o.Value == "1.3.6.1.5.5.7.3.1")))
                result.Add(new(c.Subject, c.NotAfter, c.Thumbprint));
        }
        return result;
    }
    public virtual string ValidatePfx(string path, string password)
    {
        try
        {
            using var cert = X509CertificateLoader.LoadPkcs12FromFile(path, password, X509KeyStorageFlags.EphemeralKeySet);
            if (!cert.HasPrivateKey || cert.NotBefore > DateTime.Now || cert.NotAfter <= DateTime.Now) throw new CryptographicException();
            using var rsa = cert.GetRSAPrivateKey(); if (rsa is null) throw new CryptographicException();
            return cert.Thumbprint;
        }
        catch (Exception e) when (e is CryptographicException or IOException or ArgumentException) { throw new CliRefusal("PFX password is wrong or the file is not a valid PFX."); }
    }
    public virtual bool CertificatePresent(string thumbprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine); store.Open(OpenFlags.ReadOnly);
        var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
        try { return found.Cast<X509Certificate2>().Any(c => c.HasPrivateKey); }
        finally { foreach (var c in found) c.Dispose(); }
    }
    public virtual void ImportPfx(string path, string password)
    {
        using var cert = X509CertificateLoader.LoadPkcs12FromFile(path, password, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine); store.Open(OpenFlags.ReadWrite); store.Add(cert);
    }
    private static X509Certificate2 Certificate(string thumbprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine); store.Open(OpenFlags.ReadOnly);
        return store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false).Cast<X509Certificate2>().SingleOrDefault() ?? throw new CliEnvironment("Identity certificate missing.");
    }
    private static string KeyPath(string thumbprint)
    {
        using var cert = Certificate(thumbprint); using var rsa = cert.GetRSAPrivateKey();
        return rsa switch
        {
            RSACng cng => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Crypto", "Keys", cng.Key.UniqueName!),
            RSACryptoServiceProvider csp => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Crypto", "RSA", "MachineKeys", csp.CspKeyContainerInfo.UniqueKeyContainerName),
            _ => throw new CliEnvironment("Unsupported identity private key provider.")
        };
    }
    public virtual bool KeyRead(string thumb, string account) => HasRead(KeyPath(thumb), account);
    private static bool HasRead(string path, string account)
    {
        var sid = (SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier));
        var rules = new FileInfo(path).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().Where(r => r.IdentityReference == sid).ToArray();
        return rules.Any(r => r.AccessControlType == AccessControlType.Allow && (r.FileSystemRights & FileSystemRights.ReadData) != 0) && !rules.Any(r => r.AccessControlType == AccessControlType.Deny);
    }
    public virtual void KeyRule(string thumb, string account, bool remove)
    {
        var file = new FileInfo(KeyPath(thumb)); var acl = file.GetAccessControl(); var rule = new FileSystemAccessRule(account, FileSystemRights.Read, AccessControlType.Allow);
        if (remove) acl.RemoveAccessRuleSpecific(rule); else acl.AddAccessRule(rule); file.SetAccessControl(acl);
    }
    public virtual DateTimeOffset CertificateExpiry(string thumb) { using var cert = Certificate(thumb); return cert.NotAfter; }
    public virtual bool SecretsAcl(string path, string account)
    {
        if (!File.Exists(path) || !HasRead(path, account)) return false;
        var acl = new FileInfo(path).GetAccessControl(); var service = (SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier));
        return acl.AreAccessRulesProtected && acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .All(r => r.AccessControlType != AccessControlType.Allow || r.IdentityReference.Value is "S-1-5-18" or "S-1-5-32-544" ||
                (r.IdentityReference == service && (r.FileSystemRights & ~(FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize)) == 0));
    }
    public virtual void FolderAcl(string folder, string account, bool modify)
    {
        Plain(folder); Directory.CreateDirectory(folder);
        var acl = (DirectorySecurity)PrivateFiles.WebhookAcl(account, true);
        if (modify) acl.SetAccessRule(new FileSystemAccessRule(account, FileSystemRights.Modify, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(folder).SetAccessControl(acl);
    }
    public virtual bool LoginExists(ServerInstall i)
    {
        using var connection = new SqlConnection(ServerSetupModel.Connection(i, "master")); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM sys.server_principals WHERE name=@name"; command.Parameters.AddWithValue("@name", i.Account);
        return Convert.ToInt32(command.ExecuteScalar()) != 0;
    }
    public virtual void Sql(string connectionString, string sql)
    {
        using var connection = new SqlConnection(connectionString); connection.Open();
        foreach (var batch in Regex.Split(sql.TrimStart('\uFEFF'), @"^\s*GO\s*\r?$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(b => !string.IsNullOrWhiteSpace(b)))
        { using var command = connection.CreateCommand(); command.CommandText = batch; command.CommandTimeout = 300; command.ExecuteNonQuery(); }
    }
    public virtual async Task<int> Http(string url)
    {
        try
        {
            // Readiness checks Azure and SQL; it needed ~10.5 s on the local IIS rehearsal.
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) };
            using var response = await client.GetAsync(url); return (int)response.StatusCode;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return 0; }
    }
    public virtual Task Delay() => Task.Delay(2000);
    public virtual void DeleteFolder(string folder)
    {
        Plain(folder);
        if (!Directory.Exists(folder)) return;
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories)) Plain(entry);
        Directory.Delete(folder, true);
    }
    public virtual void DeleteJournal() => File.Delete(JournalPath);
}
