using Azure.Core;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using ISTDocAi.Provisioning;

namespace ISTDocAi.OperatorTool;

// No mutation is automatically retried: an interrupted POST may already have committed.
public sealed class AzureRest(HttpClient http, TokenCredential credential, Func<CancellationToken, Task>? delay = null)
{
    public const string Graph = "https://graph.microsoft.com/v1.0/";
    public const string Arm = "https://management.azure.com/";
    public static Uri ValidateUri(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.Host is not ("graph.microsoft.com" or "management.azure.com"))
            throw new CliRefusal("Unexpected Azure response URL refused.");
        return uri;
    }
    public async Task<JsonObject?> Send(HttpMethod method, string url, JsonNode? body = null, bool missing = false, CancellationToken ct = default, bool followAsync = true)
    {
        var uri = ValidateUri(url);
        var token = await credential.GetTokenAsync(new TokenRequestContext([uri.Host == "graph.microsoft.com" ? "https://graph.microsoft.com/.default" : "https://management.azure.com/.default"]), ct);
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, ct);
        if (missing && response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw new CliEnvironment($"Azure request failed (HTTP {(int)response.StatusCode}). Inspect Azure state and permissions before retrying; details suppressed.");
        var text = await response.Content.ReadAsStringAsync(ct);
        var result = text.Length == 0 ? new JsonObject() : JsonNode.Parse(text)!.AsObject();
        if (response.StatusCode == HttpStatusCode.Accepted && followAsync)
        {
            var next = response.Headers.TryGetValues("Azure-AsyncOperation", out var asyncUrls) ? asyncUrls.Single() : response.Headers.Location?.AbsoluteUri;
            if (next is not null) return await Poll(next, ct);
        }
        return result;
    }
    public async Task<JsonObject> Poll(string url, CancellationToken ct)
    {
        // Bound both request duration (HttpClient) and total polling time (caller token + limit).
        for (var attempt = 0; attempt < 360; attempt++)
        {
            await (delay?.Invoke(ct) ?? Task.Delay(TimeSpan.FromSeconds(5), ct));
            var result = await Send(HttpMethod.Get, url, ct: ct, followAsync: false) ?? throw new CliEnvironment("Azure polling response missing.");
            var state = result["status"]?.GetValue<string>() ?? result["properties"]?["provisioningState"]?.GetValue<string>();
            if (state == "Succeeded") return result;
            if (state is "Failed" or "Canceled" or "Cancelled") throw new CliEnvironment("Azure operation failed or was cancelled. Inspect the deployment in Azure; details suppressed.");
        }
        throw new CliEnvironment("Azure operation timed out. Inspect its state before retrying.");
    }
    public async Task<JsonArray> List(string url, CancellationToken ct = default)
    {
        var rows = new JsonArray(); var seen = new HashSet<string>(StringComparer.Ordinal);
        while (seen.Add(url))
        {
            var page = await Send(HttpMethod.Get, url, ct: ct) ?? throw new CliEnvironment("Azure list response missing.");
            foreach (var row in page["value"]!.AsArray()) rows.Add(row!.DeepClone());
            var next = (page["@odata.nextLink"] ?? page["nextLink"])?.GetValue<string>();
            if (next is null) return rows;
            if (ValidateUri(next).Host != ValidateUri(url).Host) throw new CliRefusal("Cross-service pagination refused.");
            url = next;
        }
        throw new CliEnvironment("Repeated Azure pagination URL refused.");
    }
}
