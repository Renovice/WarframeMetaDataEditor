using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MetadataPatchEditor.Core;

public sealed record ModEffectSpec(
    string UpgradeType,
    string OperationType,
    string Value,
    string DamageType,
    string DescriptionLocTag,
    bool DisplayAsPercent,
    bool SmallerIsBetter);

/// <summary>Builds ordinary native upgrade entries; it never invents a script hook or a custom engine operation.</summary>
public static class ModEffects
{
    public static readonly ModEffectSpec VanillaFireResistance = new(
        "AVATAR_DAMAGE_TAKEN", "MULTIPLY", "0.89999998", "DT_FIRE",
        "/Lotus/Language/Upgrades/AvatarDamageResistanceFirePercentModDesc", true, false);

    public static bool IsFireResistance(ModEffectSpec effect)
        => effect.UpgradeType == VanillaFireResistance.UpgradeType
        && effect.OperationType == VanillaFireResistance.OperationType
        && effect.Value == VanillaFireResistance.Value
        && effect.DamageType == VanillaFireResistance.DamageType
        && effect.DisplayAsPercent == VanillaFireResistance.DisplayAsPercent
        && effect.SmallerIsBetter == VanillaFireResistance.SmallerIsBetter;

    public static string? Validate(ModEffectSpec effect)
    {
        if (!Regex.IsMatch(effect.UpgradeType, "^[A-Z][A-Z0-9_]*$")) return "Choose a decoded UpgradeType enum.";
        if (!GameplayMetadata.UpgradeOperations.Contains(effect.OperationType, StringComparer.Ordinal)) return "Choose a known OperationType.";
        if (!GameplayMetadata.DamageTypes.Contains(effect.DamageType, StringComparer.Ordinal)) return "Choose a known DamageType.";
        if (!decimal.TryParse(effect.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return "Value must be a number using a decimal point.";
        if (effect.DescriptionLocTag.Length > 0 && !effect.DescriptionLocTag.StartsWith("/Lotus/Language/", StringComparison.Ordinal))
            return "Description localization must be empty or an absolute /Lotus/Language/... key.";
        return null;
    }

    public static string BuildEntry(ModEffectSpec effect)
    {
        var warning = Validate(effect);
        if (warning != null) throw new ArgumentException(warning, nameof(effect));
        string loc = effect.DescriptionLocTag.Length == 0 ? "\"\"" : effect.DescriptionLocTag;
        return $$"""
{
UpgradeType={{effect.UpgradeType}}
OperationType={{effect.OperationType}}
Value={{effect.Value}}
DamageType={{effect.DamageType}}
AutoType=0
ValidType=""
CheckTypeOnInstall=0
SymbolFilter=""
UpgradeObject=""
ValidPostures={}
ValidModifiers={}
InvalidModifiers={}
ValidProcTypes={}
OverrideLocalization={{(effect.DescriptionLocTag.Length > 0 ? 1 : 0)}}
LocTag={{loc}}
LocKeyWordScript={
Script=""
}
DisplayAsMultiplier=0
DisplayAsPercent={{(effect.DisplayAsPercent ? 1 : 0)}}
ReverseValueSymbol=0
SmallerIsBetter={{(effect.SmallerIsBetter ? 1 : 0)}}
RoundTo=0.1
RoundingMode=RM_ROUND
AllowConditionalLocMerge=0
}
""";
    }

    public static string Append(string upgradesRaw, ModEffectSpec effect)
    {
        string raw = upgradesRaw.Trim();
        if (!raw.StartsWith('{') || !raw.EndsWith('}'))
            throw new ArgumentException("Upgrades is not a complete native collection block.", nameof(upgradesRaw));
        int close = raw.LastIndexOf('}');
        string before = raw[..close].TrimEnd();
        bool empty = before.Trim() == "{";
        return before + (empty ? "\n" : ",\n") + BuildEntry(effect).Trim() + "\n}";
    }

    public static int EntryCount(string upgradesRaw)
        => Extract.Upgrades(upgradesRaw, "Upgrades").Select(u => u.Path.Split('.')[1]).Distinct(StringComparer.Ordinal).Count();
}
