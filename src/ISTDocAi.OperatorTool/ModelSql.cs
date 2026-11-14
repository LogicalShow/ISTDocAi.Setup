using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ISTDocAi.OperatorTool;

public sealed record ModelSet(string BlModelId, decimal BlTestFieldAccuracy)
{
    public decimal BlMinimumAccuracy { get; init; } = .95m;
    public decimal BlMinFieldConfidence { get; init; } = .8m;
    public string BlMappingFile { get; init; } = "";
    public string ManifestModelId { get; init; } = "";
    public decimal? ManifestTestFieldAccuracy { get; init; }
    public decimal ManifestMinFieldConfidence { get; init; } = .8m;
    public string ManifestMappingFile { get; init; } = "";
    public string ClassifierModelId { get; init; } = "";
    public decimal? ClassifierTestFieldAccuracy { get; init; }
    public decimal ClassifierMinConfidence { get; init; } = .5m;
    public decimal RouterMinimumAccuracy { get; init; } = .6m;
    public string BillClassName { get; init; } = "bill-of-lading";
    public string ManifestClassName { get; init; } = "cargo-manifest";
    public string OtherClassName { get; init; } = "other";
    public string BillShippingLine { get; init; } = "";
}

public static class ModelSql
{
    public static void Identifier(string value, int max = 64)
    {
        if (value.Length < 1 || value.Length > max || !Regex.IsMatch(value, @"\A[A-Za-z0-9_.-]+\z")) throw new ArgumentException("Model IDs and class names require 1-64 ASCII letters, digits, dot, underscore or hyphen.");
    }
    private static string Number(decimal value)
    {
        if (value is < 0 or > 1) throw new ArgumentException("Confidence and measured accuracy must be between 0 and 1.");
        return value.ToString(CultureInfo.InvariantCulture);
    }
    public static string Binding(string model, string doc, string mappingJson = "")
    {
        Identifier(model);
        if (doc is not ("BL" or "MANIFEST")) throw new ArgumentException("Mapping document type must be BL or MANIFEST.");
        var values = new Dictionary<string, string> { ["$ModelId"] = model, ["$DocumentTypeCode"] = doc, ["$content"] = "" };
        if (mappingJson.Length > 0)
        {
            using var document = JsonDocument.Parse(mappingJson);
            var p = document.RootElement;
            string[] names = ["DocumentTypeCode", "MappingShape", "ChangeReference", "Rows", "ContainersField", "ContainerNumberField", "SealNumberField", "BillsOfLadingField"];
            if (p.ValueKind != JsonValueKind.Object || p.EnumerateObject().Count() != names.Length || p.EnumerateObject().Any(x => !names.Contains(x.Name, StringComparer.Ordinal)) || names.Any(n => !p.TryGetProperty(n, out _))) throw new ArgumentException("Missing, duplicate or unknown mapping property.");
            string S(string name) => p.GetProperty(name).ValueKind == JsonValueKind.String ? p.GetProperty(name).GetString()! : throw new ArgumentException("Invalid mapping property: " + name);
            var shape = S("MappingShape");
            if (S("DocumentTypeCode") != doc || shape is not ("LegacyFallback" or "ModelProfile") || shape == "LegacyFallback" && doc != "BL") throw new ArgumentException("Mapping shape/document type mismatch.");
            foreach (var name in names.Skip(4)) { var v = S(name); Identifier(v, 128); values["$($profile." + name + ")"] = v; }
            values["$($profile.MappingShape)"] = shape;
            var reference = S("ChangeReference");
            if (reference.Length is < 1 or > 256 || reference.TrimEnd(' ').Length == 0) throw new ArgumentException("ChangeReference requires 1-256 characters (not only spaces).");
            values["$reference"] = "CONVERT(nvarchar(256),0x" + Convert.ToHexString(Encoding.Unicode.GetBytes(reference)) + ")";
            var rows = p.GetProperty("Rows");
            if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 256) throw new ArgumentException("Rows must be an array with at most 256 entries.");
            var keys = new HashSet<string>(StringComparer.Ordinal); var sqlRows = new List<string>();
            foreach (var row in rows.EnumerateArray())
            {
                string[] fields = ["MapKind", "CanonicalKey", "AzureName"];
                if (row.ValueKind != JsonValueKind.Object || row.EnumerateObject().Count() != 3 || fields.Any(n => !row.TryGetProperty(n, out var v) || v.ValueKind != JsonValueKind.String)) throw new ArgumentException("Invalid mapping row.");
                var kind = row.GetProperty("MapKind").GetString()!; var key = row.GetProperty("CanonicalKey").GetString()!; var azure = row.GetProperty("AzureName").GetString()!;
                if (kind is not ("Field" or "Header" or "Bill" or "Container") || shape == "LegacyFallback" && kind is "Header" or "Bill") throw new ArgumentException("Invalid mapping row/kind.");
                if (key.Length > 64 || !Regex.IsMatch(key, @"\A[A-Za-z][A-Za-z0-9_]*\z")) throw new ArgumentException("Invalid mapping key.");
                Identifier(azure, 128);
                if (!keys.Add(kind + ":" + key)) throw new ArgumentException("Duplicate mapping key.");
                sqlRows.Add($"('{kind}','{key}','{azure}')");
            }
            values["$insertRows"] = sqlRows.Count == 0 ? "" : "INSERT @mappingRows VALUES " + string.Join(',', sqlRows) + ";";
            values["$content"] = SqlResources.Expand("Mapping0.sql", values);
        }
        return SqlResources.Expand("Mapping1.sql", values);
    }
    private static string MappingFile(string path) => path.Length == 0 ? "" : File.ReadAllText(path);
    public static string Mapping(string model, string doc, string json) => Transaction($"IF NOT EXISTS (SELECT 1 FROM dbo.ModelRegistry WITH(UPDLOCK,HOLDLOCK) WHERE ModelId='{Checked(model)}' AND Kind='extract' AND DocumentTypeCode='{Checked(doc)}') THROW 51204, 'Register the matching extract model first.', 1;\n" + Binding(model, doc, json));
    private static string Checked(string id) { Identifier(id); return id; }
    public static string Bl(string id, decimal accuracy, decimal gate = .95m, decimal confidence = .8m, string json = "")
    {
        Identifier(id); var binding = Binding(id, "BL", json);
        return SqlResources.Expand("Bl0.sql", new() { ["$ModelId"] = id, ["$accuracy"] = Number(accuracy), ["$confidence"] = Number(confidence), ["$gate"] = Number(gate), ["$s14"] = SqlResources.Escape(SqlResources.Activation), ["$bindingExec"] = Execute(binding) });
    }
    public static string Set(ModelSet r)
    {
        var ids = new[] { r.BlModelId, r.ManifestModelId, r.ClassifierModelId }.Where(x => x.Length > 0).ToArray();
        foreach (var id in ids) Identifier(id);
        Identifier(r.BlModelId);
        string[] classes = [r.BillClassName, r.ManifestClassName, r.OtherClassName]; foreach (var c in classes) Identifier(c);
        if (ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Length || classes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3) throw new ArgumentException("Model IDs and class names must be distinct.");
        if (r.BillShippingLine.Length > 0) Identifier(r.BillShippingLine, 20);
        foreach (var n in new[] { r.BlTestFieldAccuracy, r.BlMinimumAccuracy, r.BlMinFieldConfidence, r.ManifestMinFieldConfidence, r.ClassifierMinConfidence, r.RouterMinimumAccuracy, r.ManifestTestFieldAccuracy ?? 0, r.ClassifierTestFieldAccuracy ?? 0 }) Number(n);
        if (r.ManifestModelId.Length > 0 && r.ManifestTestFieldAccuracy is null || r.ClassifierModelId.Length > 0 && (r.ManifestModelId.Length == 0 || r.ClassifierTestFieldAccuracy is null)) throw new ArgumentException("Manifest/classifier require measured accuracy; classifier requires Manifest.");
        var batches = new List<string>();
        if (r.ManifestModelId.Length > 0)
        {
            var v = new Dictionary<string, string>
            {
                ["$manifest"] = r.ManifestModelId, ["$classifier"] = r.ClassifierModelId,
                ["$manifestConfidence"] = Number(r.ManifestMinFieldConfidence), ["$classConfidence"] = Number(r.ClassifierMinConfidence),
                ["$manifestAccuracy"] = Number(r.ManifestTestFieldAccuracy!.Value), ["$classAccuracy"] = Number(r.ClassifierTestFieldAccuracy ?? 0),
                ["$manifestVersionSql"] = "(SELECT ISNULL(MAX(CONVERT(int,Version)),0)+1 FROM dbo.ModelRegistry WITH (UPDLOCK,HOLDLOCK) WHERE DocumentTypeCode='MANIFEST' AND ShippingLineCode IS NULL AND Kind='extract')",
                ["$ClassifierVersion"] = "(SELECT ISNULL(MAX(CONVERT(int,Version)),0)+1 FROM dbo.ModelRegistry WITH (UPDLOCK,HOLDLOCK) WHERE DocumentTypeCode='AUTO' AND ShippingLineCode IS NULL AND Kind='classify')", ["$blClass"] = r.BillClassName, ["$manifestClass"] = r.ManifestClassName, ["$otherClass"] = r.OtherClassName,
                ["$line"] = r.BillShippingLine.Length == 0 ? "NULL" : "'" + r.BillShippingLine + "'",
                ["$binding"] = Binding(r.ManifestModelId, "MANIFEST", MappingFile(r.ManifestMappingFile))
            };
            v["$classifierGroupCheck"] = r.ClassifierModelId.Length == 0 ? "" : SqlResources.Expand("Router0.sql", v);
            v["$classifierRegistration"] = r.ClassifierModelId.Length == 0 ? "" : SqlResources.Expand("Router1.sql", v);
            batches.Add(SqlResources.Expand("Router2.sql", v));
        }
        batches.Add(Bl(r.BlModelId, r.BlTestFieldAccuracy, r.BlMinimumAccuracy, r.BlMinFieldConfidence, MappingFile(r.BlMappingFile)));
        if (r.ManifestModelId.Length > 0)
        {
            foreach (var kind in new[] { "extract", "classify" })
            {
                if (kind == "classify" && r.ClassifierModelId.Length == 0) continue;
                var id = kind == "extract" ? r.ManifestModelId : r.ClassifierModelId;
                var v = new Dictionary<string, string>
                {
                    ["$id"] = id, ["$kind"] = kind, ["$doc"] = kind == "extract" ? "MANIFEST" : "AUTO",
                    ["$accuracySql"] = Number(kind == "extract" ? r.ManifestTestFieldAccuracy!.Value : r.ClassifierTestFieldAccuracy!.Value),
                    ["$gate"] = Number(r.RouterMinimumAccuracy), ["$confidence"] = Number(r.ClassifierMinConfidence), ["$s14"] = SqlResources.Escape(SqlResources.Activation),
                    ["$binding"] = kind == "extract" ? Binding(id, "MANIFEST", MappingFile(r.ManifestMappingFile)) : "",
                    ["$prepare"] = kind == "classify" ? $"UPDATE dbo.ModelRegistry SET MinFieldConfidence={Number(r.ClassifierMinConfidence)} WHERE ModelId='{id}';" : ""
                };
                batches.Add(SqlResources.Expand("Router4.sql", v));
            }
        }
        var body = string.Join("\n", batches.Select(Execute));
        body += "\nSELECT ModelId, DocumentTypeCode, ShippingLineCode, Kind, Version, Status, MinFieldConfidence, TestFieldAccuracy FROM dbo.ModelRegistry WHERE ModelId IN (" + string.Join(',', ids.Select(x => "'" + x + "'")) + ") ORDER BY ModelId;";
        if (r.ClassifierModelId.Length > 0) body += $"\nSELECT ClassifierModelId, ClassName, TargetDocumentTypeCode, ShippingLineCode FROM dbo.ClassifierClasses WHERE ClassifierModelId='{r.ClassifierModelId}' ORDER BY ClassName;";
        return "-- Target: DocAi database; run after database.migration_completed. Required role: docai_modelteam.\n-- All registration and activation batches commit atomically.\n" + Regex.Replace(Transaction(body), @"[^\x00-\x7F]", "-") + "\n";
    }
    private static string Execute(string sql) => "EXEC sys.sp_executesql N'" + SqlResources.Escape(sql) + "';";
    private static string Transaction(string body) => "SET NOCOUNT ON; SET XACT_ABORT ON;\nBEGIN TRY\nBEGIN TRANSACTION;\n" + body + "\nCOMMIT;\nEND TRY\nBEGIN CATCH\nIF XACT_STATE()<>0 ROLLBACK;\nTHROW;\nEND CATCH;";
}
