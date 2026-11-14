using System.Security.Cryptography;
using System.Text;

namespace ISTDocAi.Provisioning;

public static class Credentials
{
    public static string NewApiKey() => NewWebhookKey().TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static string HashApiKey(string apiKey)
    {
        var bytes = Encoding.UTF8.GetBytes(apiKey);
        try { return Convert.ToHexString(SHA256.HashData(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public static string NewWebhookKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        try { return Convert.ToBase64String(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public static Guid NewSetupId() => Guid.NewGuid();
    public static string SecretName(Guid setupId)
    {
        if (setupId == Guid.Empty) throw new ArgumentException("Invalid SetupId.");
        return "webhook-" + setupId.ToString("N");
    }
}


