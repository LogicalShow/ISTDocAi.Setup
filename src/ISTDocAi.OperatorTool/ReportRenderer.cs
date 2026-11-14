using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace ISTDocAi.ReportPdf;

public static class ReportRenderer
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    public static void Render(string json, string audience, string kind, Stream output)
    {
        if (audience is not ("Ist" or "Customer") || kind is not ("Cycles" or "Cycle" or "Documents"))
            throw new ArgumentException("Invalid audience or report kind.");
        GlobalFontSettings.FontResolver ??= new ArialFontResolver();
        using var input = JsonDocument.Parse(json);
        var root = input.RootElement;
        var report = Get(root, "report");
        var documents = Get(root, "documentReport");
        bool internalReport = audience == "Ist";
        var doc = new Document();
        doc.Info.Title = internalReport ? "DocAi AI page usage - IST internal (contains cost estimates)" : "AI page usage statement";
        doc.Styles[StyleNames.Normal]!.Font.Name = "Arial";
        doc.Styles[StyleNames.Normal]!.Font.Size = 9;
        doc.Styles[StyleNames.Heading1]!.Font.Size = 14;
        doc.Styles[StyleNames.Heading1]!.Font.Bold = true;
        doc.Styles[StyleNames.Heading1]!.ParagraphFormat.SpaceAfter = 8;
        doc.Styles[StyleNames.Heading2]!.Font.Size = 10;
        doc.Styles[StyleNames.Heading2]!.Font.Bold = true;
        doc.Styles[StyleNames.Heading2]!.ParagraphFormat.SpaceBefore = 8;
        doc.Styles[StyleNames.Heading2]!.ParagraphFormat.SpaceAfter = 4;
        var section = doc.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(2.3);
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromCentimeter(1.4);
        section.AddParagraph(doc.Info.Title, StyleNames.Heading1);
        if (internalReport) section.AddParagraph("IST internal - do not send to the customer");
        string Clean(string value) => internalReport ? Latin(value) : Regex.Replace(Latin(value), @"\$|USD|Azure|cost|price|meter", "[omitted]", RegexOptions.IgnoreCase);
        var company = Text(root, "companyName");
        if (company.Any(c => c > 255)) company = Text(root, "consumerCode");
        if (!string.IsNullOrWhiteSpace(company)) section.AddParagraph("Company: " + Clean(company));
        section.AddParagraph("Generated at " + DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", Invariant));
        section.AddParagraph(kind == "Cycle" ? "Selected period and documents" : kind == "Documents" ? "Documents in selected period" : "All history totals and reporting periods");
        var totals = Get(report, "totals");
        if (kind != "Documents")
        {
            Heading("All history summary (including when a single period is selected)");
            Table(new[] { internalReport ? "Granted pages" : "Pages purchased", internalReport ? "Charged pages" : "Pages used", internalReport ? "Charged runs" : "Documents", "Balance", "Reserved", "Available" },
                new[] { new[] { Num(totals,"grantedPages"), Num(totals,"chargedPages"), Num(totals,"chargedRuns"), Num(totals,"ledgerBalance"), Num(totals,"currentReservedPages"), Num(totals,"currentAvailablePages") } });
            if (internalReport)
            {
                Table(new[] { "Azure total USD", "Charged runs USD", "Uncharged USD", "USD / charged page" }, new[] { new[] { Num(totals,"azureCostUsd",4), Num(totals,"chargedRunsAzureCostUsd",4), Num(totals,"unchargedAzureCostUsd",4), Num(totals,"azureCostPerChargedPageUsd",6) } });
                Warning(totals);
                Heading("Current Azure list prices");
                Table(new[] { "Meter", "USD per 1000", "Effective from UTC", "Source" }, Rows(totals,"currentListPrices").Select(r => new[] { Text(r,"meterCode"), Num(r,"pricePer1000",4), Date(r,"effectiveFrom"), Text(r,"source") }));
            }
            var cycles = new List<JsonElement>();
            var prelude = Get(report,"prelude");
            if (prelude.ValueKind == JsonValueKind.Object) cycles.Add(prelude);
            cycles.AddRange(Rows(report,"cycles"));
            Heading("Reporting periods");
            if (internalReport)
            {
                Table(new[] { "Cycle", "Start UTC", "End UTC", "Top-up pages", "Reversed" }, cycles.Select(r=> new[] { Cycle(r), Date(r,"startAtUtc"), Date(r,"endAtUtc"), Num(r,"topUpPages"), Text(r,"isReversed") }));
                Table(new[] { "Cycle", "Opening", "Adjustments", "Reversals", "Charged pages", "Runs", "Closing" }, cycles.Select(r=> new[] { Cycle(r), Num(r,"openingLedgerBalance"), Num(r,"adjustmentPages"), Num(r,"reversalPages"), Num(r,"chargedPages"), Num(r,"chargedRuns"), Num(r,"closingLedgerBalance") }));
                Table(new[] { "Cycle", "Azure classify pages", "Azure extract pages", "Azure USD", "USD per page", "Unpriced rows" }, cycles.Select(r=> new[] { Cycle(r), Num(r,"azureClassifyPages"), Num(r,"azureExtractPages"), Num(r,"azureCostUsd",4), Num(r,"azureCostPerChargedPageUsd",6), Num(r,"unpricedRows") }));
            }
            else Table(new[] { "Top-up date UTC", "Pages added", "Opening", "Used", "Documents", "Closing", "Reversed" }, cycles.Select(r=> new[] { Get(r,"topUpGrantId").ValueKind == JsonValueKind.Null ? "Prelude" : Date(r,"startAtUtc"), Num(r,"topUpPages"), Num(r,"openingLedgerBalance"), Num(r,"chargedPages"), Num(r,"chargedRuns"), Num(r,"closingLedgerBalance"), Text(r,"isReversed") }));
            if (Get(report,"hasMoreCycles").ValueKind == JsonValueKind.True) section.AddParagraph("Latest 200 top-ups shown; totals cover all history. Select a grant for an older period.");
        }
        if (kind != "Cycles")
        {
            Heading("Documents");
            var rows = Rows(documents,"documents").ToArray();
            if (internalReport)
            {
                Table(new[] { "Charge id", "Job id", "Run", "Pages", "Charged UTC", "Type", "Reason" }, rows.Select(r=>new[] { Num(r,"chargeId"), Text(r,"jobId"), Num(r,"runNo"), Num(r,"pages"), Date(r,"chargedAtUtc"), Text(r,"resolvedDocumentTypeCode"), Text(r,"runReason") }));
                Table(new[] { "Charge id", "Azure classify pages", "Azure extract pages", "USD" }, rows.Select(r=>new[] { Num(r,"chargeId"), Num(r,"azureClassifyPages"), Num(r,"azureExtractPages"), Num(r,"azureCostUsd",4) }));
                Warning(documents);
            }
            else Table(new[] { "Date UTC", "Pages", "Document type", "Reason", "Job id" }, rows.Select(r=>new[] { Date(r,"chargedAtUtc"), Num(r,"pages"), Text(r,"resolvedDocumentTypeCode"), Text(r,"runReason"), Text(r,"jobId")[..Math.Min(12,Text(r,"jobId").Length)] }));
            if (Get(root,"truncated").ValueKind == JsonValueKind.True) section.AddParagraph("Document list truncated at 10000 rows; more documents exist.");
        }
        if (!internalReport)
        {
            section.AddParagraph("A period runs from one top-up to the next. Reversed top-ups remain period boundaries.");
            section.AddParagraph("Pages reserved for documents still being read are not final usage.");
            section.AddParagraph("Documents counts charged reading runs; reading a document again may count again.");
        }
        var note = section.AddParagraph("Ordering: timestamp, grants before charges at the same timestamp, then identifier. Periods are reporting boundaries, not FIFO allocation. Coverage: recorded customer charges only; earlier history may be absent. Periods are newest first; documents follow charge id. Each fetched page is a separate snapshot; concurrent activity may change totals.");
        note.Format.Font.Size = 7;
        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = 7;
        if (internalReport) footer.AddText("Estimate from Azure list prices; the Azure invoice is authoritative. Internal - do not send to customers.\n");
        footer.AddText("Page "); footer.AddPageField(); footer.AddText(" of "); footer.AddNumPagesField();
        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        renderer.PdfDocument.Save(output, false);

        void Heading(string title) => section.AddParagraph(title, StyleNames.Heading2);
        void Warning(JsonElement value)
        {
            var count = Num(value,"unpricedRows");
            if (count != "0" && count != "-") section.AddParagraph("Warning: " + count + " unpriced rows; unknown amounts excluded.");
        }
        void Table(string[] headers, IEnumerable<string[]> values)
        {
            var table = section.AddTable();
            table.Borders.Width = 0.3;
            table.Borders.Color = Colors.LightGray;
            table.Format.Font.Size = 8;
            foreach (var _ in headers) table.AddColumn(Unit.FromCentimeter(18.2 / headers.Length));
            var header = table.AddRow(); header.HeadingFormat = true; header.Shading.Color = Colors.LightGray; header.Format.Font.Bold = true;
            for (int i=0;i<headers.Length;i++) header.Cells[i].AddParagraph(headers[i]);
            bool any = false;
            foreach (var valuesRow in values)
            {
                any = true;
                var row = table.AddRow();
                for (int i=0;i<headers.Length;i++) row.Cells[i].AddParagraph(Wrap(Clean(valuesRow[i])));
            }
            if (!any) { var row = table.AddRow(); row.Cells[0].MergeRight = headers.Length-1; row.Cells[0].AddParagraph("No records."); }
            section.AddParagraph().Format.SpaceAfter = 4;
        }
    }
    private static string Latin(string s) => new(s.Select(c => c is >= ' ' and <= '\u00ff' ? c : ' ').ToArray());
    private static string Wrap(string s) => Regex.Replace(s, @"\S{18}", "$0\u200b");
    private static JsonElement Get(JsonElement e,string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key,out var v) ? v : default;
    private static string Text(JsonElement e,string key) { var v=Get(e,key); return v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? "" : v.ToString(); }
    private static IEnumerable<JsonElement> Rows(JsonElement e,string key) { var v=Get(e,key); return v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : []; }
    private static string Num(JsonElement e,string key,int places=0) => decimal.TryParse(Text(e,key),NumberStyles.Number,Invariant,out var n) ? n.ToString("F"+places,Invariant) : "-";
    private static string Date(JsonElement e,string key) => DateTimeOffset.TryParse(Text(e,key),Invariant,DateTimeStyles.AssumeUniversal,out var d) ? d.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'",Invariant) : "-";
    private static string Cycle(JsonElement e) => string.IsNullOrEmpty(Text(e,"topUpGrantId")) ? "Prelude" : Text(e,"topUpGrantId");
}

internal sealed class ArialFontResolver : IFontResolver
{
    public FontResolverInfo ResolveTypeface(string familyName,bool bold,bool italic) => new(bold ? "ArialBold" : "Arial");
    public byte[] GetFont(string faceName)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), faceName == "ArialBold" ? "arialbd.ttf" : "arial.ttf");
        if (!File.Exists(path)) throw new InvalidOperationException("Arial fonts missing. Install Windows Arial (arial.ttf and arialbd.ttf) on the rendering server.");
        return File.ReadAllBytes(path);
    }
}
