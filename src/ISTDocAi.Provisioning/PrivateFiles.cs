using System.Security.AccessControl;
using System.Security.Principal;

namespace ISTDocAi.Provisioning;

public static class PrivateFiles
{
    public static void ValidatePath(string path)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(path, "\\A[A-Za-z]:[\\\\/]")) throw new InvalidOperationException("Absolute local path required.");
        var full = Path.GetFullPath(path);
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(full)!); directory is not null; directory = directory.Parent)
        {
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked path refused.");
            if (new[] { ".git", ".hg", ".svn" }.Any(x => File.Exists(Path.Combine(directory.FullName, x)) || Directory.Exists(Path.Combine(directory.FullName, x))))
                throw new InvalidOperationException("Repository path refused.");
        }
        foreach (var file in new[] { full, full + ".lock" })
            if (File.Exists(file) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked file refused.");
    }

    internal static FileStream Create(string path, FileSecurity acl, FileMode mode = FileMode.CreateNew) =>
        FileSystemAclExtensions.Create(new FileInfo(path), mode, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.None, acl);

    internal static void AtomicWrite(string path, byte[] bytes, FileSecurity acl)
    {
        var staging = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, ".docai-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = Create(staging, acl)) { stream.Write(bytes); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(staging, path, null); else File.Move(staging, path);
            new FileInfo(path).SetAccessControl(acl);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }

    internal static FileSystemSecurity WebhookAcl(string account, bool directory, bool backup = false)
    {
        FileSystemSecurity acl = directory ? new DirectorySecurity() : new FileSecurity();
        acl.SetAccessRuleProtection(true, false);
        var current = WindowsIdentity.GetCurrent().User!;
        var service = (SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier));
        var rights = new Dictionary<string, FileSystemRights> { ["S-1-5-18"] = FileSystemRights.FullControl, ["S-1-5-32-544"] = FileSystemRights.FullControl };
        if (backup) rights[current.Value] = FileSystemRights.FullControl;
        else if (!rights.ContainsKey(service.Value)) rights[service.Value] = service == current ? FileSystemRights.FullControl : FileSystemRights.ReadAndExecute;
        foreach (var entry in rights)
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(entry.Key), entry.Value,
                directory ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
        return acl;
    }
}
