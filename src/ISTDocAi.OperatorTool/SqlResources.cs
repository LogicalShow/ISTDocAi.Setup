using System.Text;
using System.Text.RegularExpressions;

namespace ISTDocAi.OperatorTool;

public static class SqlResources
{
    public static byte[] Bytes(string suffix)
    {
        var assembly = typeof(SqlResources).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + suffix, StringComparison.Ordinal)))!;
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
    }
    public static string Read(string suffix) => Encoding.UTF8.GetString(Bytes(suffix)).TrimStart('\uFEFF');
    // Replace all placeholders in one pass: user text can never become a second template substitution.
    public static string Expand(string template, Dictionary<string, string> values) => Regex.Replace(Read(template), @"\$\([^)]+\)|\$[A-Za-z][A-Za-z0-9]*", m =>
        values.TryGetValue(m.Value, out var value) ? value : throw new ArgumentException("Unknown SQL template marker: " + m.Value));
    public static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    public static string Statement(string label) => Regex.Match(Read("03_docai_statements.sql"), @"(?ms)^-- \[" + label + @"\].*?(?=^-- \[S\d+[a-z]?\]|\z)").Value is { Length: > 0 } sql ? sql : throw new InvalidOperationException("Reference statement missing.");
    public static string Activation => Regex.Match(Read("03_docai_statements.sql"), @"(?s)-- \[S14\].*?(SET XACT_ABORT ON;.*?)-- \[S15\]").Groups[1].Value is { Length: > 0 } sql ? sql : throw new InvalidOperationException("Cannot find S14 model activation.");
}
