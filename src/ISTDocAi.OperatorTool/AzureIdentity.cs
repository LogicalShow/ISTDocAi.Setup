using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ISTDocAi.Provisioning;

namespace ISTDocAi.OperatorTool;

public sealed class AzureIdentity(AzureRest rest)
{
    public static X509Certificate2 Certificate(string name)
    {
        if (!Regex.IsMatch(name, @"\A[A-Za-z0-9][A-Za-z0-9-]{0,79}\z")) throw new CliRefusal("Identity name must contain only letters, digits and hyphens (1..80).");
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest("CN=" + name, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMonths(12));
    }
    public static JsonObject PublicKey(X509Certificate2 certificate) => new()
    {
        ["keyId"] = Guid.NewGuid().ToString(), ["type"] = "AsymmetricX509Cert", ["usage"] = "Verify",
        ["key"] = Convert.ToBase64String(certificate.RawData), ["customKeyIdentifier"] = Convert.ToBase64String(certificate.GetCertHash()),
        ["displayName"] = certificate.Subject, ["startDateTime"] = certificate.NotBefore.ToUniversalTime().ToString("o"),
        ["endDateTime"] = certificate.NotAfter.ToUniversalTime().ToString("o")
    };
    public static string GuidValue(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty) throw new CliRefusal("A nonempty GUID is required.");
        return id.ToString();
    }
    public async Task<JsonObject> Application(string client, CancellationToken ct = default) =>
        await rest.Send(HttpMethod.Get, AzureRest.Graph + "applications(appId='" + GuidValue(client) + "')?$select=id,appId,displayName,keyCredentials", ct: ct)
        ?? throw new CliRefusal("Application not found.");

    public async Task RefuseDuplicate(string name, CancellationToken ct = default)
    {
        var apps = await rest.List(AzureRest.Graph + "applications?$filter=" + Uri.EscapeDataString("displayName eq '" + name.Replace("'", "''", StringComparison.Ordinal) + "'") + "&$select=id", ct);
        if (apps.Count > 0) throw new CliRefusal("An application with that name already exists. Inspect it; use --client-id only to target it deliberately.");
    }
    public async Task<JsonObject> Create(string name, X509Certificate2 certificate, CancellationToken ct = default)
    {
        var app = await rest.Send(HttpMethod.Post, AzureRest.Graph + "applications", new JsonObject { ["displayName"] = name, ["keyCredentials"] = new JsonArray(PublicKey(certificate)) }, ct: ct);
        return app ?? throw new CliEnvironment("Application creation response missing.");
    }
    public async Task<string> Principal(string client, bool create, CancellationToken ct = default)
    {
        client = GuidValue(client);
        var rows = await rest.List(AzureRest.Graph + "servicePrincipals?$filter=" + Uri.EscapeDataString("appId eq '" + client + "'") + "&$select=id", ct);
        if (rows.Count > 1) throw new CliRefusal("Ambiguous service principal.");
        if (rows.Count == 1) return GuidValue(rows[0]!["id"]!.GetValue<string>());
        if (!create) throw new CliRefusal("Service principal not found.");
        var sp = await rest.Send(HttpMethod.Post, AzureRest.Graph + "servicePrincipals", new JsonObject { ["appId"] = client }, ct: ct);
        return GuidValue(sp!["id"]!.GetValue<string>());
    }
    public static JsonArray Credentials(JsonObject app)
    {
        var keys = app["keyCredentials"]?.DeepClone().AsArray() ?? throw new CliRefusal("Missing keyCredentials; refusing replacement.");
        foreach (var key in keys)
            if (string.IsNullOrEmpty(key?["key"]?.GetValue<string>())) throw new CliRefusal("An existing public credential is missing; refusing replacement.");
        return keys;
    }
    public static JsonArray Without(JsonObject app, string thumbprint)
    {
        if (!Regex.IsMatch(thumbprint, @"\A[0-9a-fA-F]{40}\z")) throw new CliRefusal("Thumbprint must be 40 hexadecimal characters.");
        var keys = Credentials(app);
        var matches = keys.Where(k => string.Equals(k?["customKeyIdentifier"]?.GetValue<string>(), Convert.ToBase64String(Convert.FromHexString(thumbprint)), StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1) throw new CliRefusal("Expected exactly one matching keyCredential.");
        if (keys.Count <= 1) throw new CliRefusal("Cannot remove the only credential.");
        keys.Remove(matches[0]); return keys;
    }
    public async Task Replace(JsonObject snapshot, JsonArray keys, CancellationToken ct = default)
    {
        // Refuse observed concurrent edits. Operators must serialize rotations; Graph has no transactional key PATCH.
        var current = await Application(snapshot["appId"]!.GetValue<string>(), ct);
        if (!JsonNode.DeepEquals(snapshot["keyCredentials"], current["keyCredentials"])) throw new CliRefusal("Credentials changed during review. Inspect and retry.");
        await rest.Send(HttpMethod.Patch, AzureRest.Graph + "applications/" + GuidValue(snapshot["id"]!.GetValue<string>()), new JsonObject { ["keyCredentials"] = keys.DeepClone() }, ct: ct);
    }
    public static JsonObject Sidecar(string tenant, string client, string principal, X509Certificate2 certificate) => new()
    {
        ["tenantId"] = tenant, ["clientId"] = client, ["thumbprint"] = certificate.Thumbprint,
        ["notAfter"] = certificate.NotAfter.ToUniversalTime().ToString("o"), ["servicePrincipalObjectId"] = principal
    };
}
