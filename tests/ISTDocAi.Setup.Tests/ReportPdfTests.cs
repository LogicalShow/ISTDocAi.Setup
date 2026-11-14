using NUnit.Framework;
using Assert = ISTDocAi.Setup.Tests.PortAssert;
using System.Text.Json;
using System.Text.RegularExpressions;
using ISTDocAi.ReportPdf;
using UglyToad.PdfPig;

namespace ISTDocAi.Setup.Tests.Reports;

public sealed class ReportPdfTests : ISTDocAi.Setup.Tests.GuardedToolTests
{
    public static IEnumerable<object[]> Cases() => from audience in new[]{"Ist","Customer"}
        from kind in new[]{"Cycles","Cycle","Documents"}
        from scenario in new[]{"normal","empty","prelude","reversed","negative","many","long"}
        select new object[]{audience,kind,scenario};

    [TestCaseSource(nameof(Cases))]
    public void Renders_valid_searchable_reports(string audience,string kind,string scenario)
    {
        var row = new {topUpGrantId=scenario=="prelude" ? (long?)null : 7, startAtUtc="2026-10-01T12:34:00Z",endAtUtc=(string?)null,topUpPages=1234,isReversed=scenario=="reversed",openingLedgerBalance=1234,adjustmentPages=0,reversalPages=0,chargedPages=29,chargedRuns=3,closingLedgerBalance=scenario=="negative" ? -12 : 1205,azureClassifyPages=30,azureExtractPages=29,azureCostUsd=12.3456m,azureCostPerChargedPageUsd=0.425710m,unpricedRows=1};
        var rows=scenario=="empty" ? [] : new[]{row};
        var document=new {chargeId=42,jobId="12345678-1234-1234-1234-123456789012",runNo=2,pages=29,chargedAtUtc="2026-10-02T10:11:00Z",resolvedDocumentTypeCode="BILL_OF_LADING",runReason="Reprocess",azureClassifyPages=30,azureExtractPages=29,azureCostUsd=12.3456m};
        var payload=new {
            companyName=scenario=="long" ? string.Concat(Enumerable.Repeat("Long company name ",30)) : "\u0634\u0631\u0643\u0629",consumerCode="FWD-UNF",
            report=new { totals=new {grantedPages=scenario=="empty" ? 0 : 1234,chargedPages=scenario=="empty" ? 0 : 29,chargedRuns=scenario=="empty" ? 0 : 3,ledgerBalance=scenario=="empty" ? 0 : -12,currentReservedPages=2,currentAvailablePages=-14,azureCostUsd=12.3456m,chargedRunsAzureCostUsd=12.3456m,unchargedAzureCostUsd=0.0000m,azureCostPerChargedPageUsd=0.425710m,unpricedRows=1,currentListPrices=new[]{new {meterCode="DI_CUSTOM_EXTRACT",pricePer1000=30.1234m,effectiveFrom="2026-10-01",source="Azure retail list"}}},prelude=scenario=="prelude" ? row : null,cycles=scenario=="prelude" ? [] : scenario=="many" ? Enumerable.Repeat(row,300).ToArray() : rows,hasMoreCycles=false},
            documentReport=new {documents=Enumerable.Repeat(document,scenario=="empty" ? 0 : scenario=="many" ? 300 : 1).ToArray()},truncated=scenario=="many"
        };
        using var stream=new MemoryStream();
        ReportRenderer.Render(JsonSerializer.Serialize(payload),audience,kind,stream);
        var bytes=stream.ToArray(); Assert.Equal("%PDF",System.Text.Encoding.ASCII.GetString(bytes,0,4));
        using var pdf=PdfDocument.Open(bytes);
        var text=string.Join(" ",pdf.GetPages().Select(p=>p.Text));
        Assert.Contains("Page",text); Assert.Contains("UTC",text);
        if(scenario!="long") Assert.Contains("FWD-UNF",text);
        if(scenario=="many") {Assert.True(pdf.NumberOfPages>1);if(kind!="Cycles")Assert.Contains("10000",text);}
        if(kind!="Documents" && scenario!="empty") {Assert.Contains("1234",text);Assert.Contains("-12",text);}
        Assert.Contains("/FontFile2",System.Text.Encoding.Latin1.GetString(bytes));
        Assert.InRange((double)pdf.GetPage(1).Width,594,596);
        Assert.InRange((double)pdf.GetPage(1).Height,841,843);
        if(kind!="Cycles" && scenario!="empty")Assert.Contains("29",text);
        if(scenario=="empty")Assert.Contains("Norecords.",text);
        if(audience=="Customer") Assert.False(Regex.IsMatch(text,@"\$|USD|Azure|cost|price|meter",RegexOptions.IgnoreCase),text);
        else {Assert.Contains("Internal-donotsendtocustomers",text);if(kind!="Documents" || scenario!="empty")Assert.Contains("12.3456",text);}
    }
    [Test]
    public void Customer_free_text_cannot_leak_financial_terms()
    {
        using var stream=new MemoryStream();
        ReportRenderer.Render("""{"companyName":"Azure COST $ USD price meter","report":{},"documentReport":{"documents":[{"resolvedDocumentTypeCode":"Azure","runReason":"cost"}]}}""","Customer","Documents",stream);
        using var pdf=PdfDocument.Open(stream.ToArray());
        Assert.DoesNotMatch(new Regex(@"\$|USD|Azure|cost|price|meter",RegexOptions.IgnoreCase),string.Join(" ",pdf.GetPages().Select(p=>p.Text)));
    }

}
