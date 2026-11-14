using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ISTDocAi.Provisioning;

public enum SecretInstallResult { Installed, AlreadyInstalled }

public static class ProtectedSecretsFile
{
    private const string Prefix = "dpapi:LocalMachine:v1:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DocAi.WebhookSecrets.v1:LocalMachine");

    public static SecretInstallResult Install(string path, string secretName, string secret, string serviceAccount)
    {
        byte[]? clear = null;
        try
        {
            PrivateFiles.ValidatePath(path);
            Codes.Text(secretName, "secret name", 127);
            Codes.Text(secret, "secret", 10000);
            path = Path.GetFullPath(path);
            var directory = new DirectoryInfo(Path.GetDirectoryName(path)!);
            var directoryAcl = (DirectorySecurity)PrivateFiles.WebhookAcl(serviceAccount, true);
            var acl = (FileSecurity)PrivateFiles.WebhookAcl(serviceAccount, false);
            if (!directory.Exists) directory.Create(directoryAcl);
            directory.SetAccessControl(directoryAcl);
            using var gate = PrivateFiles.Create(path + ".lock", acl, FileMode.OpenOrCreate);
            new FileInfo(path + ".lock").SetAccessControl(acl);
            if (File.Exists(path)) new FileInfo(path).SetAccessControl(acl);
            var values = File.Exists(path) ? ReadMap(path) : new Dictionary<string, string>(StringComparer.Ordinal);
            if (values.TryGetValue(secretName, out var encrypted))
            {
                clear = Decrypt(encrypted);
                var requested = Encoding.UTF8.GetBytes(secret);
                try
                {
                    if (!CryptographicOperations.FixedTimeEquals(clear, requested)) throw new InvalidOperationException("Existing webhook secret differs; overwrite refused.");
                }
                finally { CryptographicOperations.ZeroMemory(requested); }
                return SecretInstallResult.AlreadyInstalled;
            }
            if (File.Exists(path))
            {
                new FileInfo(path).SetAccessControl(acl);
                using var backup = PrivateFiles.Create(path + "." + Guid.NewGuid().ToString("N") + ".bak", (FileSecurity)PrivateFiles.WebhookAcl(serviceAccount, false, true));
                using var source = File.OpenRead(path);
                source.CopyTo(backup); backup.Flush(true);
            }
            clear = Encoding.UTF8.GetBytes(secret);
            values.Add(secretName, Prefix + Convert.ToBase64String(ProtectedData.Protect(clear, Entropy, DataProtectionScope.LocalMachine)));
            PrivateFiles.AtomicWrite(path, JsonSerializer.SerializeToUtf8Bytes(values), acl);
            var verified = Decrypt(ReadMap(path)[secretName]);
            try { if (!CryptographicOperations.FixedTimeEquals(clear, verified)) throw new InvalidOperationException("Protected webhook verification failed."); }
            finally { CryptographicOperations.ZeroMemory(verified); }
            return SecretInstallResult.Installed;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or JsonException or ArgumentException or FormatException or System.Security.Principal.IdentityNotMappedException or System.Security.SecurityException)
        { throw new InvalidOperationException("Protected webhook file update failed; contents suppressed. Check local path, account, ACL and concurrent writers."); }
        finally { if (clear is not null) CryptographicOperations.ZeroMemory(clear); }
    }

    public static bool Matches(string path, string secretName, string secret)
    {
        if (!File.Exists(path)) return false;
        var values = ReadMap(path);
        if (!values.TryGetValue(secretName, out var encrypted)) return false;
        var clear = Decrypt(encrypted);
        var expected = Encoding.UTF8.GetBytes(secret);
        try { return CryptographicOperations.FixedTimeEquals(clear, expected); }
        finally { CryptographicOperations.ZeroMemory(clear); CryptographicOperations.ZeroMemory(expected); }
    }

    private static Dictionary<string, string> ReadMap(string path)
    {
        using var json = JsonDocument.Parse(File.ReadAllBytes(path));
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Invalid protected webhook map.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in json.RootElement.EnumerateObject())
            if (entry.Value.ValueKind != JsonValueKind.String || !values.TryAdd(entry.Name, entry.Value.GetString()!))
                throw new InvalidOperationException("Invalid protected webhook map entry.");
        return values;
    }

    private static byte[] Decrypt(string encrypted)
    {
        if (!encrypted.StartsWith(Prefix, StringComparison.Ordinal)) throw new InvalidOperationException("Invalid protected webhook format.");
        return ProtectedData.Unprotect(Convert.FromBase64String(encrypted[Prefix.Length..]), Entropy, DataProtectionScope.LocalMachine);
    }
}

