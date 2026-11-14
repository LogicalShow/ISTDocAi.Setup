using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ISTDocAi.Provisioning;

public sealed class PairingData
{
    public int V { get; init; } = 1;
    public Guid SetupId { get; init; }
    public required string ProductCode { get; init; }
    public required string TenantCode { get; init; }
    public required string ConsumerCode { get; init; }
    public required string Name { get; init; }
    public required string ApiKeyHash { get; init; }
    public required string CallbackUrl { get; init; }
    public required string WebhookSecretName { get; init; }
    public required string WebhookSecret { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public override string ToString() => "Pairing data (contents suppressed)";
}

public static class PairingCode
{
    private const string Prefix = "DOCAI-PAIR-1.";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static void Validate(PairingData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.V != 1) throw new ArgumentException("Unsupported pairing version.");
        if (data.SetupId == Guid.Empty) throw new ArgumentException("Invalid SetupId.");
        if (Codes.ConsumerCode(data.ProductCode, data.TenantCode) != data.ConsumerCode) throw new ArgumentException("ConsumerCode mismatch.");
        Codes.Name(data.Name);
        Codes.CallbackUrl(data.CallbackUrl);
        if (data.ApiKeyHash is null || !Regex.IsMatch(data.ApiKeyHash, "\\A[0-9A-F]{64}\\z")) throw new ArgumentException("Invalid API key hash.");
        if (data.WebhookSecretName != Credentials.SecretName(data.SetupId)) throw new ArgumentException("Webhook secret name mismatch.");
        try
        {
            var bytes = Convert.FromBase64String(data.WebhookSecret);
            try
            {
                if (bytes.Length != 32 || Convert.ToBase64String(bytes) != data.WebhookSecret) throw new FormatException();
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception e) when (e is FormatException or ArgumentNullException) { throw new ArgumentException("Invalid webhook key."); }
        if (data.CreatedUtc == default || data.CreatedUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Invalid CreatedUtc.");
    }

    public static string Encode(PairingData data)
    {
        Validate(data);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(data, Options);
        try { return Prefix + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') + "." + Convert.ToHexString(SHA256.HashData(bytes))[..8]; }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static PairingData Decode(string code)
    {
        if (code is null || !code.StartsWith(Prefix, StringComparison.Ordinal)) throw new ArgumentException("Unsupported pairing prefix or version.");
        var parts = code[Prefix.Length..].Split('.');
        if (parts.Length != 2 || parts[0].Length > 16000 || !Regex.IsMatch(parts[0], "\\A[A-Za-z0-9_-]+\\z") || !Regex.IsMatch(parts[1], "\\A[0-9A-F]{8}\\z"))
            throw new ArgumentException("Invalid pairing encoding.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(parts[0].Replace('-', '+').Replace('_', '/') + new string('=', (4 - parts[0].Length % 4) % 4)); }
        catch (FormatException) { throw new ArgumentException("Invalid pairing encoding."); }
        try
        {
            if (Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') != parts[0]) throw new ArgumentException("Invalid pairing encoding.");
            if (Convert.ToHexString(SHA256.HashData(bytes))[..8] != parts[1]) throw new ArgumentException("Pairing checksum mismatch.");
            try
            {
                using var document = JsonDocument.Parse(bytes);
                var fields = document.RootElement.EnumerateObject().Select(x => x.Name).ToArray();
                string[] expected = ["v", "setupId", "productCode", "tenantCode", "consumerCode", "name", "apiKeyHash", "callbackUrl", "webhookSecretName", "webhookSecret", "createdUtc"];
                if (fields.Length != expected.Length || !fields.Order().SequenceEqual(expected.Order())) throw new JsonException();
                var timestamp = document.RootElement.GetProperty("createdUtc").GetString();
                if (!DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) throw new JsonException();
                var result = JsonSerializer.Deserialize<PairingData>(bytes, Options) ?? throw new JsonException();
                Validate(result);
                return result;
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { throw new ArgumentException("Invalid pairing JSON fields."); }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
