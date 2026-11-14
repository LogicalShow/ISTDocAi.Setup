using System.Globalization;
using System.Text.RegularExpressions;

namespace ISTDocAi.OperatorTool;

public sealed record PageRequest(string Action, string Consumer)
{
    public long Pages { get; init; }
    public string Note { get; init; } = "";
    public string Reference { get; init; } = "";
    public Guid RequestId { get; init; } = Guid.NewGuid();
    public string OperatorIdentity { get; init; } = "";
    public long GrantId { get; init; }
    public string Mode { get; init; } = "";
    public int Percent { get; init; } = 10;
    public int TopUpPages { get; init; }
    public bool AllowDebt { get; init; }
    public bool ConfirmBlocked { get; init; }
    public int Limit { get; init; } = 100;
    public long AfterChargeId { get; init; }
    public bool Customer { get; init; } = true;
    public bool Reading => Action is "Cycles" or "Documents";
    public bool Mutating => !Reading && Action != "Status";
}

public static class PageSql
{
    public static void Validate(PageRequest r)
    {
        if (r.Action is not ("Status" or "TopUp" or "Adjust" or "Reverse" or "SetMode" or "SetWarningPercent" or "Cycles" or "Documents")) throw new ArgumentException("Invalid page action.");
        if (!Regex.IsMatch(r.Consumer, @"\A[A-Za-z0-9_-]{1,30}\z")) throw new ArgumentException("Invalid consumer code.");
        if (r.Reading)
        {
            if (r.GrantId < 0 || r.AfterChargeId < 0 || r.Limit is < 1 or > 500 || r.Action == "Cycles" && (r.Limit != 100 || r.AfterChargeId != 0)) throw new ArgumentException("Invalid reporting parameters.");
            return;
        }
        foreach (var value in new[] { r.Consumer, r.Note, r.Reference, r.OperatorIdentity, r.Mode })
            if (value.IndexOfAny(['\r', '\n', '"']) >= 0 || value.Contains("$(", StringComparison.Ordinal)) throw new ArgumentException("Newlines, double quotes and SQLCMD substitutions forbidden.");
        if (r.Reference.Length > 100 || Reason(r).Length > 400 || r.Mutating && string.IsNullOrWhiteSpace(r.Note)) throw new ArgumentException("Note requires 1..400 characters including Reference; Reference maximum 100.");
        if (r.Mutating && (string.IsNullOrEmpty(r.OperatorIdentity) || r.OperatorIdentity.Length > 200)) throw new ArgumentException("Operator identity required (maximum 200).");
        if (r.RequestId == Guid.Empty) throw new ArgumentException("Empty RequestId forbidden.");
        if (r.Action == "TopUp" && r.Pages is < 1 or > 1000000 || r.Action == "Adjust" && (r.Pages == 0 || r.Pages is < -1000000 or > 1000000)) throw new ArgumentException("Invalid page count.");
        if (r.Action == "Reverse" && r.GrantId <= 0) throw new ArgumentException("Positive GrantId required.");
        if (CanonicalMode(r.Mode) is null || r.Action == "SetMode" && r.Mode.Length == 0) throw new ArgumentException("Invalid Mode.");
        if (r.Percent is < 1 or > 50) throw new ArgumentException("Percent must be 1..50.");
        if (r.TopUpPages is < 0 or > 1000000 || r.TopUpPages > 0 && (r.Action != "SetMode" || CanonicalMode(r.Mode) != "Prepaid")) throw new ArgumentException("TopUpPages requires SetMode Prepaid, maximum 1000000.");
    }
    private static string Reason(PageRequest r) => r.Note + (r.Reference.Length == 0 ? "" : " [Reference: " + r.Reference + "]");
    private static string? CanonicalMode(string s) => s.Length == 0 ? "" : s.Equals("Prepaid", StringComparison.OrdinalIgnoreCase) ? "Prepaid" : s.Equals("Unlimited", StringComparison.OrdinalIgnoreCase) ? "Unlimited" : null;
    public static string Generate(PageRequest r, bool preview = false, string token = "")
    {
        Validate(r);
        if (r.Reading)
        {
            var label = r.Customer ? r.Action == "Cycles" ? "S27" : "S28" : r.Action == "Cycles" ? "S29" : "S30";
            return $"SET NOCOUNT ON; DECLARE @consumerId smallint=(SELECT ConsumerId FROM dbo.Consumers WHERE Code='{r.Consumer}'); IF @consumerId IS NULL THROW 50018,'Consumer not found.',1; DECLARE @grantId bigint={(r.Action == "Cycles" && r.GrantId == 0 ? "NULL" : N(r.GrantId))}, @limit int={N(r.Limit)}, @afterChargeId bigint={(r.AfterChargeId == 0 ? "NULL" : N(r.AfterChargeId))};\n" + SqlResources.Statement(label);
        }
        var summary = SqlResources.Read("PageSummary.sql");
        if (!r.Mutating) return $"SET NOCOUNT ON; DECLARE @id smallint=(SELECT ConsumerId FROM dbo.Consumers WHERE Code='{r.Consumer}'); IF @id IS NULL THROW 50018,'Consumer not found.',1;\n{summary}\nSELECT TOP(10) GrantId,Pages,Kind,OriginalGrantId,OperatorIdentity,Note,RequestId,BalanceAfter,RecordedAtUtc FROM dbo.PageAllowanceGrants WHERE ConsumerId=@id ORDER BY RecordedAtUtc DESC,GrantId DESC; SELECT CONVERT(date,SYSUTCDATETIME()) UtcDay,COALESCE((SELECT SUM(CONVERT(bigint,PageCount)) FROM dbo.JobRuns WHERE ConsumerId=@id AND CreatedAtUtc>=CONVERT(date,SYSUTCDATETIME())),0) SubmittedPages,COALESCE((SELECT SUM(Pages) FROM dbo.CustomerPageCharges WHERE ConsumerId=@id AND RecordedAtUtc>=CONVERT(date,SYSUTCDATETIME())),0) ChargedPages;";
        if (token.Length != 0 && !Regex.IsMatch(token, @"\A[0-9A-F]{64}\z")) throw new ArgumentException("Invalid preview token.");
        var mode = CanonicalMode(r.Mode)!;
        var debt = r.AllowDebt ? "1" : "0"; var blocked = r.ConfirmBlocked ? "1" : "0";
        var payload = string.Join('|', r.Action, N(r.Pages), N(r.GrantId), mode, N(r.Percent), N(r.TopUpPages), debt, blocked, r.OperatorIdentity, Reason(r));
        return SqlResources.Expand("PageMutation.sql", new()
        {
            ["$code"] = SqlResources.Escape(r.Consumer), ["$summary"] = summary, ["$Pages"] = N(r.Pages), ["$GrantId"] = N(r.GrantId),
            ["$ExpectedToken"] = token, ["$RequestId"] = r.RequestId.ToString(), ["$payload"] = SqlResources.Escape(payload), ["$Action"] = r.Action,
            ["$Mode"] = mode, ["$Percent"] = N(r.Percent), ["$TopUpPages"] = N(r.TopUpPages), ["$debt"] = debt, ["$blocked"] = blocked,
            ["$previewFlag"] = preview ? "1" : "0", ["$operator"] = SqlResources.Escape(r.OperatorIdentity), ["$reason"] = SqlResources.Escape(Reason(r))
        });
    }
    public static Dictionary<string, string> Files(PageRequest r)
    {
        var batch = Generate(r);
        if (!r.Mutating) return new() { [r.RequestId + ".sql"] = batch };
        const string prefix = "-- Paste PREVIEW_TOKEN from the preview result and set confirmation to 1 ONLY after reviewing Before -> After.\r\nDECLARE @ReviewedPreviewToken varchar(64)=''; DECLARE @OperatorConfirmed bit=0;\r\nIF @OperatorConfirmed<>1 OR LEN(@ReviewedPreviewToken)<>64 THROW 50018,'Review preview and confirm before execution.',1;\r\n";
        return new() { [r.RequestId + "-preview.sql"] = Generate(r, true), [r.RequestId + ".sql"] = prefix + batch.Replace("IF ''<>'' AND @token<>''", "IF @token<>@ReviewedPreviewToken", StringComparison.Ordinal) };
    }
    private static string N(long n) => n.ToString(CultureInfo.InvariantCulture);
}
