using System.Globalization;
using System.Net.Mail;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ISTDocAi.Provisioning;

namespace ISTDocAi.OperatorTool;

public static class AzureInfra
{
    public static string Template()
    {
        using var stream = typeof(AzureInfra).Assembly.GetManifestResourceStream("ISTDocAi.OperatorTool.azure.main.json")!;
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    private static string Required(IProvisionConsole c, string label, string? fallback = null)
    {
        var value = CliPrompts.Ask(c, label, fallback).Trim();
        if (value.Length == 0) throw new CliRefusal(label + " is required."); return value;
    }
    private static bool Boolean(IProvisionConsole c, string label, bool fallback)
    {
        return Required(c, label + " yes/no", fallback ? "yes" : "no").ToLowerInvariant() switch
        { "yes" => true, "no" => false, _ => throw new CliRefusal("Enter yes or no.") };
    }
    public static JsonObject Parameters(IProvisionConsole c, AzureFiles files, string location)
    {
        var environment = Required(c, "Environment dev|prod", "dev");
        if (environment is not ("dev" or "prod")) throw new CliRefusal("Environment must be dev or prod.");
        var prefix = Required(c, "Resource prefix", "docai");
        if (!Regex.IsMatch(prefix, @"\A[a-z][a-z0-9-]{1,9}\z")) throw new CliRefusal("Invalid prefix.");
        var identityPath = CliPrompts.Ask(c, "Identity JSON path (blank to enter service principal object id)").Trim().Trim('"');
        var principal = identityPath.Length > 0 ? files.Read(identityPath)["servicePrincipalObjectId"]?.GetValue<string>() ?? "" : Required(c, "Service principal object id");
        principal = AzureIdentity.GuidValue(principal);
        var di = Required(c, "Existing Document Intelligence name");
        var diGroup = Required(c, "Existing Document Intelligence resource group");
        var sqlName = Required(c, "SQL admin group name");
        var sqlId = AzureIdentity.GuidValue(Required(c, "SQL admin group object id"));
        var developer = CliPrompts.Ask(c, "Developer group object id (blank for none; must be blank in prod)").Trim();
        if (developer.Length > 0) developer = AzureIdentity.GuidValue(developer);
        if (environment == "prod" && developer.Length > 0) throw new CliRefusal("Developer group must be empty in prod.");
        var alerts = Boolean(c, "Enable alerts", false);
        var emails = Required(c, "Alert emails (comma separated)").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (emails.Length == 0 || emails.Any(e => !MailAddress.TryCreate(e, out var address) || address.Address != e)) throw new CliRefusal("Invalid alert emails.");
        if (!int.TryParse(Required(c, "Monthly budget (subscription currency)", "150"), NumberStyles.None, CultureInfo.InvariantCulture, out var budget) || budget < 1) throw new CliRefusal("Budget must be a positive integer.");
        var date = Required(c, "Budget start date YYYY-MM-01 (keep stable on redeploy)", DateTime.UtcNow.ToString("yyyy-MM-01", CultureInfo.InvariantCulture));
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) || start.Day != 1) throw new CliRefusal("Budget start must be the first day of a month.");
        var purge = Boolean(c, "Purge protection (enabling is irreversible)", environment == "prod");
        var values = new JsonObject
        {
            ["prefix"] = prefix, ["environment"] = environment, ["location"] = location, ["sqlAdminGroupName"] = sqlName,
            ["sqlAdminGroupObjectId"] = sqlId, ["developerGroupObjectId"] = developer, ["enableAlerts"] = alerts,
            ["alertEmails"] = new JsonArray(emails.Select(e => (JsonNode?)JsonValue.Create(e)).ToArray()), ["monthlyBudget"] = budget,
            ["budgetStartDate"] = date, ["purgeProtection"] = purge, ["documentIntelligenceMode"] = "existing",
            ["existingDocumentIntelligenceName"] = di, ["existingDocumentIntelligenceResourceGroup"] = diGroup,
            ["hostingMode"] = "onPremises", ["onPremisesPrincipalObjectId"] = principal
        };
        return new JsonObject(values.Select(p => new KeyValuePair<string, JsonNode?>(p.Key, new JsonObject { ["value"] = p.Value?.DeepClone() })));
    }
    public static async Task Execute(AzureArguments a, IProvisionConsole c, AzureRest rest, AzureFiles files, CancellationToken ct)
    {
        var subscriptions = await rest.List(AzureRest.Arm + "subscriptions?api-version=2022-12-01", ct);
        if (subscriptions.Count == 0) throw new CliRefusal("No subscriptions available to this account.");
        for (var n = 0; n < subscriptions.Count; n++) c.WriteLine($"{n + 1}) {subscriptions[n]!["displayName"]} ({subscriptions[n]!["subscriptionId"]})");
        if (!int.TryParse(Required(c, "Subscription number"), out var index) || index < 1 || index > subscriptions.Count) throw new CliRefusal("Invalid subscription selection.");
        var subscription = AzureIdentity.GuidValue(subscriptions[index - 1]!["subscriptionId"]!.GetValue<string>());
        var group = Required(c, "Resource group", "docai-dev-rg");
        if (!Regex.IsMatch(group, @"\A[A-Za-z0-9_().-]{1,90}\z") || group.EndsWith('.')) throw new CliRefusal("Invalid resource group name.");
        var location = Required(c, "Location", "francecentral");
        if (!Regex.IsMatch(location, @"\A[a-z0-9]+\z")) throw new CliRefusal("Invalid location.");
        var parameters = Parameters(c, files, location);
        var output = Path.GetFullPath(Required(c, "Outputs JSON path (must not exist)").Trim('"')); files.ValidateOutput(output);
        c.WriteLine("Subscription: " + subscription + "; resource group: " + group + "; location: " + location);
        c.WriteLine("Parameters (no secrets): " + parameters.ToJsonString(new() { WriteIndented = true }));
        var scope = AzureRest.Arm + "subscriptions/" + subscription + "/resourcegroups/" + Uri.EscapeDataString(group);
        var groupUrl = scope + "?api-version=2025-04-01";
        if (await rest.Send(HttpMethod.Get, groupUrl, missing: true, ct: ct) is null)
        {
            c.WriteLine("Create missing resource group " + group + " in " + location + ". Deployment incurs costs.");
            CliPrompts.Confirm(c, false); // --yes never skips this separate creation approval.
            await rest.Send(HttpMethod.Put, groupUrl, new JsonObject { ["location"] = location }, ct: ct);
        }
        var deployment = scope + "/providers/Microsoft.Resources/deployments/docai-onprem-" + Guid.NewGuid().ToString("N");
        var body = new JsonObject { ["properties"] = new JsonObject { ["mode"] = "Incremental", ["template"] = JsonNode.Parse(Template()), ["parameters"] = parameters } };
        var preview = await rest.Send(HttpMethod.Post, deployment + "/whatIf?api-version=2025-04-01", body, ct: ct);
        var changes = (preview?["properties"]?["changes"] ?? preview?["changes"]) as JsonArray ?? throw new CliEnvironment("What-if returned no changes list; refusing deployment.");
        c.WriteLine("What-if changes:");
        foreach (var change in changes) c.WriteLine($"  {change!["changeType"]}: {change["resourceId"]}");
        if (changes.Count == 0) c.WriteLine("  No resource changes.");
        c.WriteLine("Deploy the reviewed changes? Resources incur costs; Incremental mode retains resources absent from the template.");
        CliPrompts.Confirm(c, a.Yes);
        var url = deployment + "?api-version=2025-04-01";
        await rest.Send(HttpMethod.Put, url, body, ct: ct);
        var result = await rest.Send(HttpMethod.Get, url, ct: ct);
        if (result?["properties"]?["provisioningState"]?.GetValue<string>() != "Succeeded") result = await rest.Poll(url, ct);
        if (result["properties"]?["outputs"] is not JsonObject outputs) throw new CliEnvironment("Deployment completed without outputs. Retrieve outputs from Azure before packaging.");
        // Allowlist the keyless output contract. Never write arbitrary provider output.
        var selected = new JsonObject();
        foreach (var name in new[] { "blobEndpoint", "documentIntelligenceEndpoint", "keyVaultEndpoint", "hosting" })
            selected[name] = outputs[name]?.DeepClone() ?? throw new CliEnvironment("Deployment output contract is incomplete.");
        files.Outputs(output, selected); c.WriteLine("Saved keyless deployment outputs: " + output);
    }
}
