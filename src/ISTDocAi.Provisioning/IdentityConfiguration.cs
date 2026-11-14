using System.Text;
using System.Text.Json;

namespace ISTDocAi.Provisioning;

public static class IdentityConfiguration
{
    // Token offsets preserve BOM, whitespace, comments, newline style, casing and every unrelated byte.
    public static byte[] Replace(byte[] source, string expected, string replacement)
    {
        var bom = source.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0;
        var reader = new Utf8JsonReader(source.AsSpan(bom), new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var path = new List<string>(); string pending = ""; var offset = -1; var length = 0; var count = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName) { pending = reader.GetString()!; continue; }
            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) { path.Add(pending); pending = ""; continue; }
            if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray) { path.RemoveAt(path.Count - 1); continue; }
            if (path.Count == 3 && path[0] == "" && path[1].Equals("DocAi", StringComparison.OrdinalIgnoreCase) && path[2].Equals("Identity", StringComparison.OrdinalIgnoreCase) && pending.Equals("CertificateThumbprint", StringComparison.OrdinalIgnoreCase))
            {
                if (reader.TokenType != JsonTokenType.String || !string.Equals(reader.GetString(), expected, StringComparison.OrdinalIgnoreCase) && !string.Equals(reader.GetString(), replacement, StringComparison.OrdinalIgnoreCase))
                    throw new CliRefusal("Configured thumbprint differs from the install journal. Inspect before renewal.");
                count++; offset = checked((int)reader.TokenStartIndex) + bom; length = checked((int)reader.BytesConsumed - (int)reader.TokenStartIndex);
            }
            pending = "";
        }
        if (count != 1) throw new CliRefusal("Expected one DocAi:Identity:CertificateThumbprint value.");
        var token = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(replacement));
        var result = new byte[source.Length - length + token.Length];
        source.AsSpan(0, offset).CopyTo(result); token.CopyTo(result.AsSpan(offset)); source.AsSpan(offset + length).CopyTo(result.AsSpan(offset + token.Length));
        return result;
    }
}
