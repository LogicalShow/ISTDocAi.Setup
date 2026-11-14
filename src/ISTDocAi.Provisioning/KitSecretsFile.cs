using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ISTDocAi.Provisioning;

public sealed record PrepareRequest(string ProductCode, string TenantCode, string Name, string CallbackUrl, string BaseUrl);

public sealed class KitSecretsFile
{
    private readonly XDocument document;
    private byte[]? savedFingerprint;
    private KitSecretsFile(XDocument document)
    {
        this.document = document;
        if (document.Root?.Name != "appSettings" || document.Root.Elements().Any(x => x.Name != "add" || x.Attribute("key") is null || x.Attribute("value") is null))
            throw new InvalidOperationException("Invalid kit appSettings XML.");
        if (document.Root.Elements("add").GroupBy(x => (string)x.Attribute("key")!, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
            throw new InvalidOperationException("Duplicate kit secret keys.");
    }

    public static KitSecretsFile Parse(string xml)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            return new(XDocument.Load(reader));
        }
        catch (XmlException) { throw new InvalidOperationException("Invalid kit appSettings XML."); }
    }

    public static KitSecretsFile Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return Parse("<appSettings />");
            var xml = File.ReadAllText(path);
            var result = Parse(xml);
            result.savedFingerprint = Fingerprint(xml);
            return result;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new InvalidOperationException("Could not load kit secrets; contents suppressed."); }
    }

    public string? Get(string key) => (string?)document.Root!.Elements("add").SingleOrDefault(x => string.Equals((string?)x.Attribute("key"), key, StringComparison.OrdinalIgnoreCase))?.Attribute("value");
    private void Set(string key, string value)
    {
        var node = document.Root!.Elements("add").SingleOrDefault(x => string.Equals((string?)x.Attribute("key"), key, StringComparison.OrdinalIgnoreCase));
        if (node is null) document.Root.Add(new XElement("add", new XAttribute("key", key), new XAttribute("value", value)));
        else node.SetAttributeValue("value", value);
    }

    public PairingData Prepare(PrepareRequest request, DateTimeOffset createdUtc)
    {
        var code = Codes.ConsumerCode(request.ProductCode, request.TenantCode);
        Codes.Name(request.Name); Codes.CallbackUrl(request.CallbackUrl);
        var baseUrl = Codes.BaseUrl(request.BaseUrl);
        var expected = new Dictionary<string, string>
        {
            ["ProductCode"] = request.ProductCode, ["TenantCode"] = request.TenantCode, ["ConsumerCode"] = code,
            ["Name"] = request.Name, ["CallbackUrl"] = request.CallbackUrl, ["BaseUrl"] = baseUrl
        };
        if (Get("DocAiProvision:SetupId") is null)
        {
            if (Get("DocAi:ApiKey") is not null || !string.IsNullOrEmpty(Get("DocAi:WebhookSecret1")) || !string.IsNullOrEmpty(Get("DocAi:WebhookSecret2")) || document.Root!.Elements("add").Any(x => ((string)x.Attribute("key")!).StartsWith("DocAiProvision:", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Legacy or incomplete kit provisioning metadata; prepare refused.");
            if (createdUtc == default || createdUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Invalid CreatedUtc.");
            Set("DocAiProvision:SetupId", Credentials.NewSetupId().ToString("D"));
            Set("DocAiProvision:CreatedUtc", createdUtc.ToString("O"));
            foreach (var entry in expected) Set("DocAiProvision:" + entry.Key, entry.Value);
            Set("DocAi:ApiKey", Credentials.NewApiKey());
            Set("DocAi:WebhookSecret1", Credentials.NewWebhookKey());
            Set("DocAi:WebhookSecret2", "");
            Set("DocAi:Enabled", "false");
            Set("DocAiProvision:State", "Prepared");
        }
        else
        {
            if (Get("DocAiProvision:State") != "Prepared") throw new InvalidOperationException("Kit state is not Prepared; prepare refused.");
            if (expected.Any(x => Get("DocAiProvision:" + x.Key) != x.Value)) throw new InvalidOperationException("Prepared kit metadata mismatch.");
            if (Get("DocAi:Enabled") != "false") throw new InvalidOperationException("Prepared kit Enabled mismatch.");
        }
        return Pairing();
    }

    public PairingData Pairing()
    {
        if (!Guid.TryParse(Get("DocAiProvision:SetupId"), out var setup) ||
            !DateTimeOffset.TryParseExact(Get("DocAiProvision:CreatedUtc"), "O", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var created) ||
            Get("DocAi:ApiKey") is not { } key || !System.Text.RegularExpressions.Regex.IsMatch(key, "\\A[A-Za-z0-9_-]{43}\\z"))
            throw new InvalidOperationException("Incomplete kit provisioning metadata.");
        var data = new PairingData
        {
            SetupId = setup, ProductCode = Get("DocAiProvision:ProductCode")!, TenantCode = Get("DocAiProvision:TenantCode")!,
            ConsumerCode = Get("DocAiProvision:ConsumerCode")!, Name = Get("DocAiProvision:Name")!, CallbackUrl = Get("DocAiProvision:CallbackUrl")!,
            ApiKeyHash = Credentials.HashApiKey(key), WebhookSecretName = Credentials.SecretName(setup), WebhookSecret = Get("DocAi:WebhookSecret1")!, CreatedUtc = created
        };
        PairingCode.Validate(data);
        return data;
    }

    public void Activate(string verifiedConsumerCode)
    {
        if (Get("DocAiProvision:State") != "Prepared") throw new InvalidOperationException("Kit state is not Prepared; activate refused.");
        if (Pairing().ConsumerCode != verifiedConsumerCode) throw new InvalidOperationException("Verified consumer code mismatch.");
        Set("DocAi:BaseUrl", Codes.BaseUrl(Get("DocAiProvision:BaseUrl")!));
        Set("DocAi:Enabled", "true"); Set("DocAi:Mode", "Live"); Set("DocAi:RemotePolling", "On"); Set("DocAiProvision:State", "Active");
    }

    public void Cancel()
    {
        if (Get("DocAiProvision:State") != "Prepared") throw new InvalidOperationException("Kit state is not Prepared; cancel refused.");
        document.Root!.Elements("add").Where(x =>
        {
            var key = (string)x.Attribute("key")!;
            return key.StartsWith("DocAiProvision:", StringComparison.OrdinalIgnoreCase) ||
                new[] { "DocAi:ApiKey", "DocAi:WebhookSecret1", "DocAi:WebhookSecret2" }.Contains(key, StringComparer.OrdinalIgnoreCase);
        }).Remove();
        Set("DocAi:Enabled", "false");
    }

    public void SetDevelopmentCredentials(string apiKey, string webhookSecret)
    {
        Set("DocAi:ApiKey", apiKey); Set("DocAi:WebhookSecret1", webhookSecret);
        Set("DocAi:Enabled", "true");
    }

    public string ToXml() => document.ToString();
    public void Save(string path, FileSecurity acl, string? backupDirectory = null)
    {
        try
        {
            if (File.Exists(path + ".lock") && (File.GetAttributes(path + ".lock") & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Linked kit lock file refused.");
            using var gate = PrivateFiles.Create(path + ".lock", acl, FileMode.OpenOrCreate);
            if (File.Exists(path))
            {
                if (savedFingerprint is null || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(savedFingerprint, Fingerprint(File.ReadAllText(path))))
                    throw new InvalidOperationException("Kit secrets changed since load; reload before saving.");
                BackupPrivateFile(path, backupDirectory);
            }
            else if (savedFingerprint is not null) throw new InvalidOperationException("Kit secrets removed since load; reload before saving.");
            var xml = ToXml();
            var bytes = Encoding.UTF8.GetBytes(xml);
            try { PrivateFiles.AtomicWrite(path, bytes, acl); }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
            savedFingerprint = Fingerprint(xml);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { throw new InvalidOperationException("Could not save kit secrets; contents suppressed."); }
    }

    private static byte[] Fingerprint(string xml) => System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(xml));

    public static FileSecurity CreateAcl(string readAccount)
    {
        var acl = new FileSecurity(); acl.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { "S-1-5-18", "S-1-5-32-544" })
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.FullControl, AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(new NTAccount(readAccount), FileSystemRights.Read, AccessControlType.Allow));
        return acl;
    }

    private static void BackupPrivateFile(string path, string? backupDirectory)
    {
        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (local is null || !Path.IsPathFullyQualified(local)) throw new InvalidOperationException("LOCALAPPDATA must be an absolute local folder for private backups.");
        var folder = Path.GetFullPath(backupDirectory ?? Path.Combine(local, "DocAi", "backups"));
        PrivateFiles.ValidatePath(Path.Combine(folder, "backup"));
        var current = WindowsIdentity.GetCurrent().User!;
        var directoryAcl = new DirectorySecurity(); directoryAcl.SetAccessRuleProtection(true, false);
        directoryAcl.AddAccessRule(new FileSystemAccessRule(current, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        var directory = new DirectoryInfo(folder);
        if (!directory.Exists) directory.Create(directoryAcl);
        directory.SetAccessControl(directoryAcl);
        var fileAcl = new FileSecurity(); fileAcl.SetAccessRuleProtection(true, false);
        fileAcl.AddAccessRule(new FileSystemAccessRule(current, FileSystemRights.FullControl, AccessControlType.Allow));
        var backup = Path.Combine(folder, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(path));
        using var target = PrivateFiles.Create(backup, fileAcl);
        using var source = File.OpenRead(path);
        source.CopyTo(target); target.Flush(true);
    }
}



