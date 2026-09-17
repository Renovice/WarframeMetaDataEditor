namespace OpenWFMetadataUpdater;

/// <summary>
/// Minimal structural parser for the line-oriented metadata text stored in Packages.bin.
/// It deliberately preserves values as strings and only understands balanced blocks, fields,
/// and list elements; it does not guess game semantics.
/// </summary>
public static class DeMetadataSyntax
{
    public static IReadOnlyDictionary<string, string> Fields(string text)
    {
        var body = StripOuterBlock(text.Replace("\r", ""));
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int p = 0;
        while (p < body.Length)
        {
            SkipSeparators(body, ref p);
            if (p >= body.Length) break;

            int keyStart = p;
            while (p < body.Length && body[p] != '=' && body[p] != '\n') p++;
            if (p >= body.Length || body[p] != '=')
            {
                SkipLine(body, ref p);
                continue;
            }

            string key = body[keyStart..p].Trim();
            p++;
            SkipHorizontalWhitespace(body, ref p);
            string value = p < body.Length && body[p] == '{'
                ? ReadBalanced(body, ref p)
                : ReadScalar(body, ref p);
            if (key.Length != 0)
                result[key] = value.Trim();
        }
        return result;
    }

    public static List<string> List(string text)
    {
        var body = StripOuterBlock(text.Replace("\r", ""));
        var values = new List<string>();
        int p = 0;
        while (p < body.Length)
        {
            SkipSeparators(body, ref p);
            if (p >= body.Length) break;
            string value = body[p] == '{' ? ReadBalanced(body, ref p) : ReadScalar(body, ref p);
            value = value.Trim().TrimEnd(',').Trim();
            if (value.Length != 0) values.Add(Unquote(value));
        }
        return values;
    }

    public static string? Scalar(IReadOnlyDictionary<string, string> fields, string key)
        => fields.TryGetValue(key, out var value) ? Unquote(value.Trim()) : null;

    public static bool? Boolean(IReadOnlyDictionary<string, string> fields, string key)
    {
        var value = Scalar(fields, key);
        return value switch { "1" => true, "0" => false, _ => null };
    }

    public static int? Integer(IReadOnlyDictionary<string, string> fields, string key)
        => int.TryParse(Scalar(fields, key), out int value) ? value : null;

    static string StripOuterBlock(string text)
    {
        string t = text.Trim();
        if (t.Length >= 2 && t[0] == '{')
        {
            int p = 0;
            string block = ReadBalanced(t, ref p);
            if (p == t.Length) return block[1..^1];
        }
        return t;
    }

    static void SkipSeparators(string s, ref int p)
    {
        while (p < s.Length && (char.IsWhiteSpace(s[p]) || s[p] == ',')) p++;
    }

    static void SkipHorizontalWhitespace(string s, ref int p)
    {
        while (p < s.Length && s[p] is ' ' or '\t') p++;
    }

    static void SkipLine(string s, ref int p)
    {
        while (p < s.Length && s[p] != '\n') p++;
        if (p < s.Length) p++;
    }

    static string ReadScalar(string s, ref int p)
    {
        int start = p;
        bool quoted = false;
        bool escaped = false;
        while (p < s.Length)
        {
            char c = s[p];
            if (quoted)
            {
                p++;
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') quoted = false;
                continue;
            }
            if (c == '"') { quoted = true; p++; continue; }
            if (c is '\n' or ',') break;
            p++;
        }
        string value = s[start..p];
        if (p < s.Length && s[p] == ',') p++;
        if (p < s.Length && s[p] == '\n') p++;
        return value;
    }

    static string ReadBalanced(string s, ref int p)
    {
        if (p >= s.Length || s[p] != '{') throw new FormatException($"Expected '{{' at offset {p}.");
        int start = p;
        int depth = 0;
        bool quoted = false;
        bool escaped = false;
        while (p < s.Length)
        {
            char c = s[p++];
            if (quoted)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') quoted = false;
                continue;
            }
            if (c == '"') { quoted = true; continue; }
            if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return s[start..p];
        }
        throw new FormatException($"Unterminated metadata block beginning at offset {start}.");
    }

    static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
        return value;
    }
}
