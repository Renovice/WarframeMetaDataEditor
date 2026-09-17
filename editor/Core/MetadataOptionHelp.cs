using System.Text.RegularExpressions;

namespace MetadataPatchEditor.Core;

public sealed record MetadataOptionInfo(string Value, string Label, string Tooltip);

/// <summary>
/// Human-readable labels and evidence-constrained hover help for native metadata
/// enum values. Unknown decoded values keep their exact token and receive a safe
/// field-level explanation rather than an invented gameplay claim.
/// </summary>
public static class MetadataOptionHelp
{
    public static MetadataOptionInfo Describe(string key, string value, string? fieldDescription = null)
    {
        string description = fieldDescription?.Trim() ?? "";
        MetadataOptionInfo? known = key switch
        {
            "StackStyle" => StackStyle(value),
            "ConsolidateDamageOverTime" => Consolidation(value),
            "UseHighestDamageOverTime" => HighestBasis(value),
            "OperationType" => Operation(value),
            "DamageType" or "Type" => DamageType(value),
            "ForcedProcs" or "ForcedDeathProcs" => ProcType(value),
            "Rarity" => Rarity(value),
            "FusionLimit" or "BaseDrain" => QualityTier(value),
            "ProcInjuryType" => InjuryReaction(value),
            "ArtifactPolarity" => Polarity(value),
            _ => null
        };
        if (known != null) return known;

        string label = FriendlyToken(value);
        string tooltip = value == GameplayMetadata.OriginalOrInherited
            ? "Writes no override. The selected record keeps its current inherited or native value."
            : $"Native value: {value}.";
        if (description.Length > 0) tooltip += " " + description;
        return new MetadataOptionInfo(value, label, tooltip);
    }

    static MetadataOptionInfo? StackStyle(string value) => value switch
    {
        var v when v == GameplayMetadata.OriginalOrInherited => new(v,
            "Use inherited/default behavior",
            "Writes no StackStyle field. The handler keeps its current native or inherited storage behavior."),
        "OneActiveInstance" => new(value, "One shared status instance",
            "All matching procs on the target use one active status record. Ownership and stored modifier context can therefore be shared; DOT consolidation and highest-basis settings still control damage updates."),
        "OneInstancePerInstigator" => new(value, "One instance per source",
            "Keeps one status record for each native instigator identity. Repeated procs from that instigator share its record, while another attacker gets a separate record. Instigator usually means the attacking entity and is not guaranteed to distinguish two weapons owned by the same entity."),
        "OneInstancePerHit" => new(value, "One instance per hit",
            "Every proc receives its own status record, retaining that hit's source, damage basis, modifiers, and duration independently."),
        _ => null
    };

    static MetadataOptionInfo? Consolidation(string value) => value switch
    {
        var v when v == GameplayMetadata.OriginalOrInherited => new(v,
            "Use inherited/default behavior", "Writes no ConsolidateDamageOverTime field."),
        "0" => new(value, "Native repeated-DOT mode 0",
            "Writes ConsolidateDamageOverTime=0. This is the native zero/no-explicit-consolidation mode; confirm converted-status damage in game."),
        "1" => new(value, "Native repeated-DOT mode 1",
            "Writes ConsolidateDamageOverTime=1. This is the shared/consolidated mode observed on the base Heat handler."),
        "10" => new(value, "Native repeated-DOT mode 10",
            "Writes ConsolidateDamageOverTime=10. This is the per-hit repeated-DOT mode observed on the base Toxin handler."),
        _ => null
    };

    static MetadataOptionInfo? HighestBasis(string value) => value switch
    {
        var v when v == GameplayMetadata.OriginalOrInherited => new(v,
            "Use inherited/default behavior", "Writes no UseHighestDamageOverTime field."),
        "0" => new(value, "Disabled",
            "Writes UseHighestDamageOverTime=0. A matched or consolidated DOT basis is allowed to follow normal replacement/update behavior."),
        "1" => new(value, "Keep the highest DOT basis",
            "Writes UseHighestDamageOverTime=1. When the native handler matches or consolidates DOT damage, a weaker incoming basis does not replace the stronger stored basis. Independent per-hit records remain independent."),
        _ => null
    };

    static MetadataOptionInfo? Operation(string value) => value switch
    {
        "ADD" => new(value, "Add", "Adds the upgrade value to the underlying stat."),
        "ADD_BASE" => new(value, "Add to base", "Adds the value to the stat's base amount before later scaling."),
        "MULTIPLY" => new(value, "Multiply", "Multiplies the underlying value by this entry's multiplier."),
        "STACKING_MULTIPLY" => new(value, "Stacking multiplier", "Combines this entry through the game's stacking-multiplier path rather than a flat addition."),
        "SET" => new(value, "Set exact value", "Replaces the affected value with this entry's value."),
        _ => null
    };

    static MetadataOptionInfo? DamageType(string value)
    {
        (string Label, string Help)? info = value switch
        {
            "DT_ANY" => ("Any damage type", "Matches any damage type when this field is a filter; it does not create a new status element."),
            "DT_PHYSICAL" => ("Physical", "The general physical-damage family."),
            "DT_IMPACT" => ("Impact", "Impact damage. A weapon still needs status chance or a forced proc to apply the Impact status."),
            "DT_PUNCTURE" => ("Puncture", "Puncture damage. A weapon still needs status chance or a forced proc to apply the Puncture status."),
            "DT_SLASH" => ("Slash", "Slash damage. Damage type alone does not guarantee a Slash/Bleed proc."),
            "DT_FIRE" => ("Heat", "Heat damage. Damage type alone does not define the Heat status handler's stacking or DOT behavior."),
            "DT_FREEZE" => ("Cold", "Cold damage. Damage type alone does not define Cold slow or full-freeze behavior."),
            "DT_ELECTRICITY" => ("Electricity", "Electricity damage. Status behavior remains controlled by the selected status handler."),
            "DT_POISON" => ("Toxin", "Toxin damage. Status behavior remains controlled by the selected status handler."),
            "DT_EXPLOSION" => ("Blast", "Blast damage. Status behavior remains controlled by the selected status handler."),
            "DT_RADIATION" => ("Radiation", "Radiation damage. Status behavior remains controlled by the selected status handler."),
            "DT_GAS" => ("Gas", "Gas damage. Status behavior remains controlled by the selected status handler."),
            "DT_MAGNETIC" => ("Magnetic", "Magnetic damage. Status behavior remains controlled by the selected status handler."),
            "DT_VIRAL" => ("Viral", "Viral damage. Status behavior remains controlled by the selected status handler."),
            "DT_CORROSIVE" => ("Corrosive", "Corrosive damage. Status behavior remains controlled by the selected status handler."),
            "DT_RADIANT" => ("Void / Radiant", "Internal Radiant damage family used by Void-facing metadata in this cache."),
            "DT_SENTIENT" => ("Tau / Sentient", "Internal Sentient damage family used by Tau-facing metadata in this cache."),
            "DT_VOID" => ("Void", "Native Void damage-family token. Do not assume it is interchangeable with every internal Radiant use."),
            "DT_CINEMATIC" => ("Cinematic", "Special native damage family used by scripted/cinematic behavior."),
            _ => null
        };
        return info is { } found
            ? new MetadataOptionInfo(value, found.Label, found.Help + $" Native value: {value}.")
            : null;
    }

    static MetadataOptionInfo? ProcType(string value)
    {
        string label = value switch
        {
            "PT_IMPACT" => "Impact",
            "PT_PUNCTURE" => "Puncture",
            "PT_SLASH" => "Slash / Bleed",
            "PT_IMMOLATION" => "Heat",
            "PT_FREEZE" => "Cold",
            "PT_ELECTROCUTION" => "Electricity",
            "PT_POISONED" => "Toxin",
            "PT_EXPLOSION" => "Blast",
            "PT_RADIATION" => "Radiation",
            "PT_GAS" => "Gas",
            "PT_MAGNETIZED" => "Magnetic",
            "PT_VIRAL" => "Viral",
            "PT_CORRODED" => "Corrosive",
            "PT_RADIANT" => "Void / Radiant",
            _ => ""
        };
        return label.Length == 0 ? null : new MetadataOptionInfo(value, label,
            $"Forces or permits the {label} proc identifier. Native value: {value}.");
    }

    static MetadataOptionInfo? Rarity(string value) => value switch
    {
        "COMMON" => new(value, "Common", "Common visible rarity tier."),
        "UNCOMMON" => new(value, "Uncommon", "Uncommon visible rarity tier."),
        "RARE" => new(value, "Rare", "Rare visible rarity tier."),
        "LEGENDARY" => new(value, "Legendary", "Legendary visible rarity tier."),
        _ => null
    };

    static MetadataOptionInfo? QualityTier(string value) => value switch
    {
        var v when v == UpgradeIdentity.ClientFusionUnchanged => new(v, "Use inherited client value",
            "Writes no client FusionLimit override; the current cache value remains unchanged."),
        "QA_NONE" => new(value, "None", "Native QA_NONE tier. For FusionLimit this is a coarse client tier, not an exact numeric server rank."),
        "QA_LOW" => new(value, "Low", "Native QA_LOW tier. For FusionLimit this is a coarse client tier, not an exact numeric server rank."),
        "QA_MEDIUM" => new(value, "Medium", "Native QA_MEDIUM tier. For FusionLimit this is a coarse client tier, not an exact numeric server rank."),
        "QA_HIGH" => new(value, "High", "Native QA_HIGH tier. For FusionLimit this is a coarse client tier, not an exact numeric server rank."),
        "QA_VERY_HIGH" => new(value, "Very high", "Native QA_VERY_HIGH tier. For FusionLimit this is a coarse client tier, not an exact numeric server rank."),
        _ => null
    };

    static MetadataOptionInfo? InjuryReaction(string value) => value switch
    {
        "ANY" => new(value, "Any native reaction", "Allows the handler to use its general/default injury reaction."),
        "PAIN" => new(value, "Pain", "Requests the native pain reaction."),
        "STAGGER" => new(value, "Stagger", "Requests the native stagger reaction."),
        "STUN" => new(value, "Stun", "Requests the native stun reaction."),
        _ => null
    };

    static MetadataOptionInfo? Polarity(string value) => value switch
    {
        "AP_ANY" => new(value, "No fixed polarity", "Adds a slot without requiring one fixed polarity. Native value: AP_ANY."),
        "AP_UNIVERSAL" => new(value, "Universal", "Native universal-polarity slot. Native value: AP_UNIVERSAL."),
        "AP_ATTACK" => new(value, "Attack / Madurai", "Native attack-polarity identifier. Native value: AP_ATTACK."),
        "AP_DEFENSE" => new(value, "Defense / Vazarin", "Native defense-polarity identifier. Native value: AP_DEFENSE."),
        "AP_TACTIC" => new(value, "Tactic / Naramon", "Native tactic-polarity identifier. Native value: AP_TACTIC."),
        _ => null
    };

    static string FriendlyToken(string value)
    {
        if (value == GameplayMetadata.OriginalOrInherited) return "Use inherited/default behavior";
        string token = Regex.Replace(value, "^(DT|PT|QA|AVATAR|WEAPON)_", "", RegexOptions.CultureInvariant);
        token = token.Replace('_', ' ').ToLowerInvariant();
        return string.Join(' ', token.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }
}
