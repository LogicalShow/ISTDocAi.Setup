using System.Text.RegularExpressions;

namespace ISTDocAi.Provisioning;

public static class Codes
{
    public static string Text(string? value, string field, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Any(char.IsControl) || value.Contains("$(", StringComparison.Ordinal))
            throw new ArgumentException($"Invalid {field}.");
        return value;
    }

    public static string Part(string? value, string field)
    {
        if (value is null || !Regex.IsMatch(value, "\\A[A-Z0-9]{1,20}\\z")) throw new ArgumentException($"Invalid {field}.");
        return value;
    }

    public static string ConsumerCode(string productCode, string tenantCode)
    {
        var code = Part(productCode, "ProductCode") + "-" + Part(tenantCode, "TenantCode");
        if (code.Length > 30) throw new ArgumentException("Invalid ConsumerCode length.");
        return code;
    }

    public static string Name(string value) => Text(value, "Name", 200);
    public static string CallbackUrl(string value)
    {
        var uri = Https(value, "CallbackUrl", 400);
        if (!uri.AbsolutePath.EndsWith("/api/DocAi/Webhook", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Invalid CallbackUrl path.");
        return value;
    }

    public static string BaseUrl(string value)
    {
        var uri = Https(value, "BaseUrl", int.MaxValue);
        return uri.AbsoluteUri.TrimEnd('/') + "/";
    }

    private static Uri Https(string value, string field, int maximum)
    {
        Text(value, field, maximum);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || string.IsNullOrEmpty(uri.Host) ||
            uri.UserInfo.Length != 0 || Regex.IsMatch(value, @"\Ahttps://[^/]*@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) || uri.Query.Length != 0 || uri.Fragment.Length != 0 || value.Contains('\\') || value.Any(char.IsWhiteSpace))
            throw new ArgumentException($"Invalid {field}; HTTPS without credentials, query or fragment required.");
        return uri;
    }
}

