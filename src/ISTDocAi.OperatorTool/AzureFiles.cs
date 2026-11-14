using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;
using ISTDocAi.Provisioning;

namespace ISTDocAi.OperatorTool;

public class AzureFiles
{
    public virtual void Validate(string folder, string name)
    {
        PrivateFiles.ValidatePath(Path.Combine(folder, name + ".pfx"));
        PrivateFiles.ValidatePath(Path.Combine(folder, name + ".json"));
        if (File.Exists(Path.Combine(folder, name + ".pfx")) || File.Exists(Path.Combine(folder, name + ".json"))) throw new CliRefusal("Export files already exist. Choose a new name or folder.");
    }
    private static FileSecurity Acl()
    {
        var acl = new FileSecurity(); acl.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { WindowsIdentity.GetCurrent().User!.Value, "S-1-5-18", "S-1-5-32-544" }.Distinct())
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.FullControl, AccessControlType.Allow));
        return acl;
    }
    public virtual void ExportPfx(string folder, string name, X509Certificate2 certificate, string password)
    {
        Validate(folder, name);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name + ".pfx");
        var bytes = certificate.Export(X509ContentType.Pfx, password);
        try
        {
            using var stream = FileSystemAclExtensions.Create(new FileInfo(path), FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.None, Acl());
            stream.Write(bytes); stream.Flush(true);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public virtual void Sidecar(string folder, string name, JsonObject identity)
    {
        var path = Path.Combine(folder, name + ".json"); PrivateFiles.ValidatePath(path);
        using var stream = FileSystemAclExtensions.Create(new FileInfo(path), FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.None, Acl());
        stream.Write(Encoding.UTF8.GetBytes(identity.ToJsonString(new() { WriteIndented = true }))); stream.Flush(true);
    }
    public virtual JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    public virtual void ValidateOutput(string path)
    {
        if (!Path.IsPathFullyQualified(path) || File.Exists(path)) throw new CliRefusal("Choose an absolute outputs path that does not exist.");
        for (var d = new DirectoryInfo(Path.GetDirectoryName(path)!); d is not null; d = d.Parent)
            if (d.Exists && (d.Attributes & FileAttributes.ReparsePoint) != 0) throw new CliRefusal("Linked outputs folder refused.");
    }
    public virtual void Outputs(string path, JsonObject outputs)
    {
        ValidateOutput(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(Encoding.UTF8.GetBytes(outputs.ToJsonString(new() { WriteIndented = true }))); stream.Flush(true);
    }
}
