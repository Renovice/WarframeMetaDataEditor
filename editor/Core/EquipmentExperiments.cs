using System.Text.RegularExpressions;

namespace MetadataPatchEditor.Core;

/// <summary>
/// Shared equipment metadata parsing and validation. Generators produce patch text only;
/// they never write to a live game folder.
/// </summary>
public static class EquipmentExperiments
{
    public static IReadOnlyList<string> ReadSet(string composedText, string key)
    {
        var field = DumpParser.ParseText(composedText).TopLevel.FirstOrDefault(f => f.Key == key);
        if (field == null || field.Kind == FieldKind.Scalar) return Array.Empty<string>();
        var raw = field.RawValue.Replace("\r", "");
        int first = raw.IndexOf('{'), last = raw.LastIndexOf('}');
        if (first < 0 || last <= first) return Array.Empty<string>();
        return raw[(first + 1)..last]
            .Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => v.Length > 0)
            .ToArray();
    }

    public static string FormatSet(IEnumerable<string> values)
        => "{" + string.Join(',', values.Select(v => v.Trim()).Where(v => v.Length > 0)) + "}";

    public static IReadOnlyList<string> ParseCompactSet(string value)
    {
        var compact = PatchGenerator.Compact(value);
        if (compact.Length < 2 || compact[0] != '{' || compact[^1] != '}') return Array.Empty<string>();
        return compact[1..^1]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => v.Length > 0)
            .ToArray();
    }

    public static string? ValidateArtifactSlots(string value)
    {
        var slots = ParseCompactSet(value);
        if (slots.Count == 0 || slots.Count > 32)
            return "ArtifactSlots must be a non-empty {...} set containing at most 32 entries.";
        if (slots.Any(slot => !Regex.IsMatch(slot, "^AP_[A-Z0-9_]+$", RegexOptions.CultureInvariant)))
            return "Every ArtifactSlots entry must be an AP_* polarity token.";
        return null;
    }

    public static string? ValidateNonEmptySet(string key, string value)
        => ParseCompactSet(value).Count == 0 ? $"{key} must be a non-empty {{...}} set." : null;

    public static string? ValidateAbsolutePathSet(string key, string value)
    {
        var entries = ParseCompactSet(value);
        if (entries.Count == 0) return $"{key} must be a non-empty {{...}} set.";
        return entries.Any(path => !path.StartsWith("/", StringComparison.Ordinal))
            ? $"Every {key} entry must be an absolute /Lotus/... object path."
            : null;
    }

    public static string? ValidateAbsolutePath(string key, string value)
        => PatchGenerator.Compact(value).StartsWith("/", StringComparison.Ordinal)
            ? null
            : $"{key} must be an absolute /Lotus/... object path.";

}
