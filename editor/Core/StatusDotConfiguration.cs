using System.Globalization;
using System.Text.RegularExpressions;

namespace MetadataPatchEditor.Core;

/// <summary>
/// Preserves an existing native status DOT payload or creates the common payload
/// inherited by LotusDamageProc subclasses. Creation is statically supported by
/// the reflected schema; a newly converted status still requires live-game QA.
/// </summary>
public sealed class StatusDotConfiguration
{
    readonly string? _originalDamageOverTime;
    readonly string? _originalPercentBlock;
    readonly string? _originalConsolidation;
    readonly string? _originalHighest;
    readonly string? _originalStackStyle;

    public bool Existed { get; }
    public string DamageType { get; set; }
    public string PercentOfSourceHit { get; set; }
    public string ConsolidationMode { get; set; }
    public string HighestDamageBasisMode { get; set; }
    public string StackStyle { get; set; }

    StatusDotConfiguration(bool existed, string damageType, string percentOfSourceHit,
        string consolidationMode, string highestDamageBasisMode, string stackStyle,
        string? originalDamageOverTime, string? originalPercentBlock,
        string? originalConsolidation, string? originalHighest, string? originalStackStyle)
    {
        Existed = existed;
        DamageType = damageType;
        PercentOfSourceHit = percentOfSourceHit;
        ConsolidationMode = consolidationMode;
        HighestDamageBasisMode = highestDamageBasisMode;
        StackStyle = stackStyle;
        _originalDamageOverTime = originalDamageOverTime;
        _originalPercentBlock = originalPercentBlock;
        _originalConsolidation = originalConsolidation;
        _originalHighest = originalHighest;
        _originalStackStyle = originalStackStyle;
    }

    public static StatusDotConfiguration Parse(string composedText, string displayName)
    {
        var top = DumpParser.ParseText(composedText).TopLevel
            .GroupBy(field => field.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

        top.TryGetValue("DamageOverTime", out var dot);
        top.TryGetValue("DOTPercentOfBaseDamage", out var percent);
        top.TryGetValue("ConsolidateDamageOverTime", out var consolidation);
        top.TryGetValue("UseHighestDamageOverTime", out var highest);
        top.TryGetValue("StackStyle", out var stackStyle);

        string damageType = dot == null
            ? GameplayMetadata.DefaultDotDamageType(displayName)
            : ReadScalar(dot.RawValue, "Type") ?? GameplayMetadata.DefaultDotDamageType(displayName);
        string fraction = percent == null ? "0.5" : ReadRangeValue(percent.RawValue) ?? "0.5";
        return new StatusDotConfiguration(dot != null, damageType, fraction,
            consolidation?.RawValue ?? GameplayMetadata.OriginalOrInherited,
            highest?.RawValue ?? GameplayMetadata.OriginalOrInherited,
            stackStyle?.RawValue ?? GameplayMetadata.OriginalOrInherited,
            dot?.RawValue, percent?.RawValue, consolidation?.RawValue,
            highest?.RawValue, stackStyle?.RawValue);
    }

    public string? Validate()
    {
        if (!GameplayMetadata.DamageTypes.Contains(DamageType, StringComparer.Ordinal))
            return $"Choose a known damage type; '{DamageType}' is not a decoded DT_* value.";
        if (!decimal.TryParse(PercentOfSourceHit, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal fraction)
            || fraction < 0)
            return "Damage per tick must be a non-negative decimal. Example: 0.5 means 50% of the source hit.";
        if (!GameplayMetadata.DotConsolidationModes.Contains(ConsolidationMode, StringComparer.Ordinal))
            return "Choose Original / inherited or one of the observed native consolidation values: 0, 1, or 10.";
        if (!GameplayMetadata.HighestDotBasisModes.Contains(HighestDamageBasisMode, StringComparer.Ordinal))
            return "Choose Original / inherited, Off (0), or On (1) for highest DOT basis.";
        if (!GameplayMetadata.StackStyles.Contains(StackStyle, StringComparer.Ordinal))
            return "Choose Original / inherited or one of the three native proc-storage modes.";
        return null;
    }

    public IReadOnlyDictionary<string, string> BuildChangedTopLevelFields()
    {
        string dot = UpsertScalar(_originalDamageOverTime ?? "{}", "Type", DamageType);
        dot = UpsertScalar(dot, "ProcChance", ReadScalar(dot, "ProcChance") ?? "0");
        string percent = UpsertScalar(_originalPercentBlock ?? "{}", "ValueRange",
            "{" + PatchGenerator.Compact(PercentOfSourceHit) + "," + PatchGenerator.Compact(PercentOfSourceHit) + "}");

        var changes = new Dictionary<string, string>(StringComparer.Ordinal);
        AddIfChanged(changes, "DamageOverTime", _originalDamageOverTime, dot);
        AddIfChanged(changes, "DOTPercentOfBaseDamage", _originalPercentBlock, percent);
        AddOptionalIfChanged(changes, "ConsolidateDamageOverTime", _originalConsolidation, ConsolidationMode);
        AddOptionalIfChanged(changes, "UseHighestDamageOverTime", _originalHighest, HighestDamageBasisMode);
        AddOptionalIfChanged(changes, "StackStyle", _originalStackStyle, StackStyle);
        return changes;
    }

    static void AddOptionalIfChanged(Dictionary<string, string> changes, string key,
        string? original, string value)
    {
        if (value == GameplayMetadata.OriginalOrInherited) return;
        AddIfChanged(changes, key, original, value);
    }

    static void AddIfChanged(Dictionary<string, string> changes, string key, string? original, string value)
    {
        if (original == null || PatchGenerator.Compact(original) != PatchGenerator.Compact(value))
            changes[key] = PatchGenerator.Compact(value);
    }

    static string? ReadScalar(string raw, string key)
    {
        var match = Regex.Match(raw, "(?m)^\\s*" + Regex.Escape(key) + "=([^\\r\\n]+)");
        return match.Success ? match.Groups[1].Value.Trim().TrimEnd(',') : null;
    }

    static string? ReadRangeValue(string raw)
    {
        string? range = ReadScalar(raw, "ValueRange");
        if (range == null) return null;
        string inner = range.Trim().TrimStart('{').TrimEnd('}');
        return inner.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    }

    static string UpsertScalar(string raw, string key, string value)
    {
        var regex = new Regex("(?m)^(\\s*)" + Regex.Escape(key) + "=[^\\r\\n]*");
        if (regex.IsMatch(raw))
            return regex.Replace(raw, match => match.Groups[1].Value + key + "=" + value, 1);

        int close = raw.LastIndexOf('}');
        if (close < 0) return "{\n" + key + "=" + value + "\n}";
        string before = raw[..close].TrimEnd();
        string separator = before.EndsWith('{') ? "\n" : "\n";
        return before + separator + key + "=" + value + "\n" + raw[close..];
    }
}
