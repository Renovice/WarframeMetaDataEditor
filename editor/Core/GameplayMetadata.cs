using System.Globalization;
using System.Text.RegularExpressions;

namespace MetadataPatchEditor.Core;

public enum GuidedMetadataKind { StatusEffect, WeaponDamage }

public sealed record GuidedField(
    string Key,
    string Path,
    string Display,
    string Group,
    string Description,
    string Original,
    bool TopLevel,
    bool IsSet,
    IReadOnlyList<string> Options);

public sealed record StatusEffectStep(
    string Title,
    string Description,
    string TargetGroup = "",
    string Icon = "effect",
    string Value = "");

public sealed record StatusVisualMetric(
    string Label,
    string Value,
    string Hint,
    string Icon);

public sealed record StatusBehaviorOverview(
    string Status,
    string Accent,
    string Scope,
    string Summary,
    string Rules,
    IReadOnlyList<StatusVisualMetric> Metrics,
    IReadOnlyList<StatusEffectStep> Steps,
    string DotCapability,
    bool HasDot);

/// <summary>
/// Evidence-constrained presentation and validation for native status handlers
/// and weapon AttackData. This class does not invent gameplay values: it only
/// labels fields that exist in the selected composed metadata and keeps the
/// exact query path beside every friendly label.
/// </summary>
public static class GameplayMetadata
{
    public const string OriginalOrInherited = "Original / inherited";

    public static readonly string[] StackStyles =
    [
        OriginalOrInherited,
        "OneActiveInstance",
        "OneInstancePerInstigator",
        "OneInstancePerHit"
    ];

    public static readonly string[] DotConsolidationModes =
    [
        OriginalOrInherited,
        "0",
        "1",
        "10"
    ];

    public static readonly string[] HighestDotBasisModes =
    [
        OriginalOrInherited,
        "0",
        "1"
    ];

    public static readonly string[] WeaponCategories =
    [
        "Primary Weapons", "Secondary Weapons", "Melee Weapons", "Archwing Weapons",
        "Companion Weapons", "Amps", "Parazon", "Railjack Weapons", "Projectiles"
    ];

    public static readonly string[] DamageTypes =
    [
        "DT_ANY", "DT_PHYSICAL", "DT_IMPACT", "DT_PUNCTURE", "DT_SLASH",
        "DT_FIRE", "DT_FREEZE", "DT_ELECTRICITY", "DT_POISON", "DT_EXPLOSION",
        "DT_RADIATION", "DT_GAS", "DT_MAGNETIC", "DT_VIRAL", "DT_CORROSIVE",
        "DT_RADIANT", "DT_SENTIENT", "DT_VOID", "DT_CINEMATIC"
    ];

    public static readonly string[] ProcTypes =
    [
        "PT_IMPACT", "PT_PUNCTURE", "PT_SLASH", "PT_IMMOLATION", "PT_FREEZE",
        "PT_ELECTROCUTION", "PT_POISONED", "PT_EXPLOSION", "PT_RADIATION",
        "PT_GAS", "PT_MAGNETIZED", "PT_VIRAL", "PT_CORRODED", "PT_RADIANT"
    ];

    public static readonly string[] UpgradeOperations =
    [
        "STACKING_MULTIPLY", "ADD", "MULTIPLY", "SET", "ADD_BASE"
    ];

    static readonly HashSet<string> StatusTopLevel = new(StringComparer.Ordinal)
    {
        "Duration", "MaxStacks", "StackStyle", "ProcInjuryType", "CanBlockInjury",
        "ConsolidateDamageOverTime", "UseHighestDamageOverTime", "BaseDamageModifier",
        "RepeatDamageModifier", "RadialPercentOfBaseDamage", "RadialDamageTickRate",
        "DestroyAvatar", "DestroyAvatarDelay", "IsSpace", "InjuryExitsWallSlide",
        "MaxStacksWithOverguard", "BaseFreezeModifier", "RepeatFreezeModifier",
        "FrozenDuration", "ReworkMaxStacks", "PostFrozenStacks",
        "OverfreezeDurationPercentPerStack"
    };

    static readonly string[] StatusBehaviorBlocks =
    [
        "DamageOverTime", "DOTPercentOfBaseDamage", "RadialDamage", "DamageRadius",
        "StackedUpgrades", "IncrementalUpgrades", "FrozenDebuffs"
    ];

    static readonly string[] OptionalInheritedStatusFields =
    [
        "StackStyle",
        "ConsolidateDamageOverTime",
        "UseHighestDamageOverTime"
    ];

    public static bool IsOptionalInheritedStatusField(string key)
        => OptionalInheritedStatusFields.Contains(key, StringComparer.Ordinal);

    public static bool IsWeaponCategory(string category)
        => WeaponCategories.Contains(category, StringComparer.Ordinal);

    public static bool IsStatusHandlerPath(string path)
    {
        if (!path.Contains("InjuryHandler", StringComparison.Ordinal)) return false;
        var leaf = Leaf(path);
        return leaf.EndsWith("DamageProc", StringComparison.Ordinal)
            || leaf.EndsWith("FireProc", StringComparison.Ordinal)
            || leaf is "LotusFreezeHandler" or "RadiationDamageProc" or "ViralDamageProc"
                or "MagneticDamageProc" or "LotusCorrosiveDamageProc";
    }

    public static string StatusDisplayName(string path)
    {
        var leaf = Leaf(path);
        string core = leaf.Replace("DamageProc", "", StringComparison.Ordinal)
                          .Replace("Proc", "", StringComparison.Ordinal);
        string status = core.Contains("Radiant", StringComparison.Ordinal) ? "Void"
                      : core.Contains("Sentient", StringComparison.Ordinal) || core.Contains("Tau", StringComparison.Ordinal) ? "Tau"
                      : core.Contains("Fire", StringComparison.Ordinal) ? "Heat"
                      : core.Contains("Poison", StringComparison.Ordinal) ? "Toxin"
                      : core.Contains("Freeze", StringComparison.Ordinal) ? "Cold"
                      : core.Contains("Explosion", StringComparison.Ordinal) ? "Blast"
                      : core.Contains("Electricity", StringComparison.Ordinal) ? "Electricity"
                      : core.Contains("Corrosive", StringComparison.Ordinal) ? "Corrosive"
                      : core.Contains("Radiation", StringComparison.Ordinal) ? "Radiation"
                      : core.Contains("Magnetic", StringComparison.Ordinal) ? "Magnetic"
                      : core.Contains("Viral", StringComparison.Ordinal) ? "Viral"
                      : core.Contains("Slash", StringComparison.Ordinal) ? "Slash"
                      : core.Contains("Puncture", StringComparison.Ordinal) ? "Puncture"
                      : core.Contains("Impact", StringComparison.Ordinal) ? "Impact"
                      : core.Contains("Gas", StringComparison.Ordinal) ? "Gas"
                      : Pretty(core.Replace("Base", "", StringComparison.Ordinal));

        string scope = leaf.StartsWith("NokkoVIP", StringComparison.Ordinal) ? "Nokko Colony VIP override"
                     : leaf.StartsWith("Triangle", StringComparison.Ordinal) ? "Triangle unit · Man in the Wall override"
                     : path.Contains("/SpaceBattles/", StringComparison.Ordinal) ? "Railjack / Space handler"
                     : path.Contains("/Player/", StringComparison.Ordinal) ? "Player / Tenno receiver"
                     : path.Contains("/Corpus/", StringComparison.Ordinal) ? SpecializedScope("Corpus", core)
                     : path.Contains("/Grineer/", StringComparison.Ordinal) ? SpecializedScope("Grineer", core)
                     : path.Contains("/Infested/", StringComparison.Ordinal) ? SpecializedScope("Infested", core)
                     : path.Contains("/Sentient", StringComparison.Ordinal) ? SpecializedScope("Sentient", core)
                     : leaf.StartsWith("Vip", StringComparison.Ordinal) ? "VIP override"
                     : leaf.StartsWith("Base", StringComparison.Ordinal) ? "Enemy Base · ordinary shared default"
                     : Pretty(leaf);
        return $"{status} — {scope}";
    }

    public static StatusBehaviorOverview BuildStatusOverview(string displayName, string path,
        IReadOnlyList<GuidedField> fields)
    {
        string status = displayName.Split('—', 2, StringSplitOptions.TrimEntries)[0];
        bool hasDot = fields.Any(f => f.Path.StartsWith("DamageOverTime.", StringComparison.Ordinal));
        string? dotType = fields.FirstOrDefault(f => f.Path == "DamageOverTime.Type")?.Original;
        string summary = status switch
        {
            "Cold" => "Slows the target, builds toward freezing, and applies the listed critical-damage vulnerability.",
            "Heat" => "Deals Heat damage over time and applies the listed armor reduction.",
            "Toxin" => "Deals Toxin damage over time.",
            "Slash" => "Applies the native Bleed damage-over-time effect.",
            "Electricity" => "Applies electrical damage over time and crowd control.",
            "Gas" => "Applies the native Gas damage-over-time area.",
            "Corrosive" => "Applies the listed armor-reduction stack effect.",
            "Viral" => "Applies the listed health-damage vulnerability stack effect.",
            "Magnetic" => "Applies the listed shield and Overguard vulnerability effects.",
            "Puncture" => "Applies the listed outgoing-damage reduction stack effect.",
            "Radiation" => "Applies the native Radiation behavior and listed vulnerability effects.",
            "Blast" => "Applies the handler's native radial Blast behavior.",
            "Impact" => "Applies the handler's native stagger and stack behavior.",
            "Void" => "Creates the native Void bullet-attractor effect around the affected target.",
            "Tau" => "Applies Tau's native status-chance vulnerability stack effect.",
            _ when hasDot => $"Applies a native {FriendlyDamageType(dotType)} damage-over-time effect.",
            _ => "Applies the native status behavior and stack effects listed below."
        };

        string duration = OriginalValue(fields, "Duration");
        string stacks = OriginalValue(fields, "MaxStacks");
        string stackStyle = OriginalValue(fields, "StackStyle");
        var rules = new List<string>();
        if (duration.Length > 0) rules.Add($"Duration: {duration} seconds");
        if (stacks.Length > 0) rules.Add(status == "Cold" && TryAddedStackTotal(stacks, out int coldTotal)
            ? $"Full freeze: {coldTotal} total procs (first proc + {stacks} added stacks)"
            : $"Maximum stacks: {stacks}");
        if (stackStyle.Length > 0) rules.Add($"Stacking style: {stackStyle}");
        rules.Add(hasDot
            ? $"Damage over time: yes ({FriendlyDamageType(dotType)})"
            : "Damage over time: not enabled — the weapon hit can still deal up-front elemental damage");

        var steps = BuildEffectSteps(status, fields, hasDot, dotType);
        var metrics = BuildStatusMetrics(status, path, fields, hasDot, dotType);
        string dotCapability = hasDot
            ? $"DOT is enabled. It deals {FriendlyDamageType(dotType)} damage using this handler's payload and scaling fields."
            : "DOT is not enabled in this record. The reflected native base schema accepts a DamageOverTime payload, so the editor can create one; live testing is still required for a newly converted status.";

        return new StatusBehaviorOverview(status, StatusAccent(status), StatusScopeDescription(path), summary,
            string.Join("  •  ", rules), metrics, steps, dotCapability, hasDot);
    }

    static IReadOnlyList<StatusVisualMetric> BuildStatusMetrics(string status, string path,
        IReadOnlyList<GuidedField> fields, bool hasDot, string? dotType)
    {
        string duration = OriginalValue(fields, "Duration");
        string stacks = OriginalValue(fields, "MaxStacks");
        var result = new List<StatusVisualMetric>();
        result.Add(new("Duration", duration.Length == 0 ? "Inherited" : duration + " s",
            "How long one application remains active.", "duration"));

        if (status == "Cold" && TryAddedStackTotal(stacks, out int total))
            result.Add(new("Full freeze", total + " procs", $"First proc plus {stacks} added stacks.", "freeze"));
        else
            result.Add(new("Stack cap", stacks.Length == 0 ? "Inherited" : stacks,
                "Maximum accepted stacks in this handler.", "stacks"));

        result.Add(new("Damage over time", hasDot ? FriendlyDamageType(dotType) : "Off",
            hasDot ? "This handler owns an explicit DOT payload." : "No DOT payload exists in this handler.", "dot"));
        result.Add(new("Applies to", FriendlyScope(path), "The receiver family changed by this handler.", "scope"));
        return result;
    }

    static IReadOnlyList<StatusEffectStep> BuildEffectSteps(string status, IReadOnlyList<GuidedField> fields,
        bool hasDot, string? dotType)
    {
        List<StatusEffectStep> steps = status switch
        {
            "Cold" => new List<StatusEffectStep>
            {
                new("Slow buildup", ColdSlowExplanation(fields), "Behavior & stacking", "slow", ColdRepeatValue(fields)),
                new("Full freeze", ColdFreezeExplanation(fields), "Behavior & stacking", "freeze", ColdFreezeValue(fields)),
                new("Critical vulnerability", "Cold stacks increase critical damage received. The frozen-state bonus is stored separately.",
                    "Stack effects", "vulnerability", UpgradeSummary(fields, "StackedUpgrades"))
            },
            "Heat" =>
            [
                new("Burning reaction", "Applies Heat's native reaction and visual effect.", "Behavior & stacking", "heat"),
                new("Armor reduction", "The incremental armor modifier ramps while the proc is active.", "Stack effects", "armor", UpgradeSummary(fields, "IncrementalUpgrades")),
                new("Heat DOT", "Deals Heat damage over time from the triggering hit's damage basis.", "Damage over time", "dot", DotBasis(fields))
            ],
            "Toxin" =>
            [
                new("Toxin DOT", "Deals Toxin damage over time using the native shield-bypass rules.", "Damage over time", "dot", DotBasis(fields))
            ],
            "Slash" => [new("Bleed", "Applies the native Slash Bleed damage-over-time payload.", "Damage over time", "bleed", DotBasis(fields))],
            "Electricity" =>
            [
                new("Electrical DOT", "Applies the handler's Electricity damage-over-time payload.", "Damage over time", "electricity", DotBasis(fields)),
                new("Crowd control", "Stuns the affected target and reaches nearby targets where radial fields are configured.", "Behavior & stacking", "stun", RadialSummary(fields))
            ],
            "Gas" => [new("Gas cloud", "Applies the handler's area damage-over-time behavior.", "Damage over time", "cloud", RadialSummary(fields))],
            "Corrosive" => [new("Armor reduction", "Each accepted stack applies the listed armor modifier up to the stack cap.", "Stack effects", "armor", UpgradeSummary(fields, "StackedUpgrades"))],
            "Viral" => [new("Health-damage vulnerability", "Each accepted stack increases damage dealt to health.", "Stack effects", "health", UpgradeSummary(fields, "StackedUpgrades"))],
            "Magnetic" => [new("Shield and Overguard vulnerability", "Applies shield/Overguard damage modifiers and native recharge disruption.", "Stack effects", "shield", UpgradeSummary(fields, "StackedUpgrades"))],
            "Puncture" => [new("Outgoing-damage reduction", "Reduces damage dealt by the affected target.", "Stack effects", "damage-down", UpgradeSummary(fields, "StackedUpgrades"))],
            "Radiation" => [new("Confusion", "Changes target allegiance through the native Radiation reaction and applies any listed vulnerability.", "Behavior & stacking", "confusion", UpgradeSummary(fields, "StackedUpgrades"))],
            "Blast" => [new("Blast detonation", "Builds toward the handler's native radial detonation behavior.", "Behavior & stacking", "blast", RadialSummary(fields))],
            "Impact" => [new("Stagger", "Applies the native Impact stagger and its repeated-stack behavior.", "Behavior & stacking", "impact", OriginalValue(fields, "ProcInjuryType"))],
            "Void" => [new("Bullet attractor", "Creates the native Void bullet-attractor field around the affected target.", "Behavior & stacking", "orbit", DurationValue(fields))],
            "Tau" => [new("Status vulnerability", "Increases the chance for later status effects to affect this target.", "Stack effects", "tau", UpgradeSummary(fields, "StackedUpgrades"))],
            _ => [new("Native proc", "Applies this handler's native reaction and the modifiers listed below.", "Behavior & stacking", "effect")]
        };
        string storage = OriginalValue(fields, "StackStyle");
        steps.Add(new StatusEffectStep(
            "Proc storage and source ownership",
            "Choose whether repeated procs share one record, keep one record per source, or remain independent per hit.",
            "Behavior & stacking", "instances", FriendlyStackStyle(storage)));
        return steps;
    }

    static string FriendlyStackStyle(string value) => value switch
    {
        "OneActiveInstance" => "One shared record",
        "OneInstancePerInstigator" => "One record per source",
        "OneInstancePerHit" => "One record per hit",
        _ => "Original / inherited"
    };

    static string StatusAccent(string status) => status switch
    {
        "Impact" => "#69B8E7", "Puncture" => "#E9C85E", "Slash" => "#E77D83",
        "Heat" => "#FF8A43", "Cold" => "#70C7F3", "Electricity" => "#D9C75A",
        "Toxin" => "#72D36C", "Blast" => "#F2A354", "Radiation" => "#D77CE5",
        "Gas" => "#84C47B", "Magnetic" => "#A48BE8", "Viral" => "#E57DAD",
        "Corrosive" => "#AED45D", "Void" => "#7CCFEA", "Tau" => "#E6E1CA",
        _ => "#57B9E8"
    };

    static string FriendlyScope(string path)
    {
        if (path.Contains("/SpaceBattles/", StringComparison.Ordinal)) return "Railjack";
        if (path.Contains("/Player/", StringComparison.Ordinal) || path.Contains("Tenno", StringComparison.OrdinalIgnoreCase)) return "Player / Tenno";
        if (path.Contains("/BaseInjuryHandlers/Base", StringComparison.Ordinal)) return "Ground enemies";
        return "Specialized unit";
    }

    static bool TryAddedStackTotal(string value, out int total)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int added) && added >= 0)
        {
            total = added + 1;
            return true;
        }
        total = 0;
        return false;
    }

    static string ColdRepeatValue(IReadOnlyList<GuidedField> fields)
        => PercentValue(OriginalValue(fields, "RepeatFreezeModifier"), "+", " per added stack");

    static string ColdFreezeValue(IReadOnlyList<GuidedField> fields)
        => TryAddedStackTotal(OriginalValue(fields, "MaxStacks"), out int total) ? total + " total procs" : "Inherited cap";

    static string ColdSlowExplanation(IReadOnlyList<GuidedField> fields)
    {
        string repeat = PercentValue(OriginalValue(fields, "RepeatFreezeModifier"), "+", "");
        return repeat.Length == 0
            ? "The first Cold proc slows the target; repeated stacks strengthen the evaluator's freeze modifier."
            : $"The first proc applies the evaluator's native slow. Every added stack contributes {repeat} more slow buildup.";
    }

    static string ColdFreezeExplanation(IReadOnlyList<GuidedField> fields)
    {
        string max = OriginalValue(fields, "MaxStacks");
        if (!TryAddedStackTotal(max, out int total))
            return "Full freeze occurs when this handler reaches its inherited stack cap.";
        string repeat = PercentValue(OriginalValue(fields, "RepeatFreezeModifier"), "", "");
        string extra = repeat.Length == 0 ? "" : $" Each of the {max} added stacks contributes {repeat} freeze modifier.";
        return $"Full freeze triggers at the cap: one initial proc plus {max} added stacks = {total} total procs.{extra} The trigger is the cap, not a displayed modifier reaching 100%.";
    }

    static string UpgradeSummary(IReadOnlyList<GuidedField> fields, string block)
    {
        string first = ValueAt(fields, block + ".0.BaseValue");
        string repeat = ValueAt(fields, block + ".0.RepeatValue");
        var parts = new List<string>();
        string firstPercent = PercentValue(first, "+", " first");
        string repeatPercent = PercentValue(repeat, "+", " each");
        if (firstPercent.Length > 0) parts.Add(firstPercent);
        if (repeatPercent.Length > 0) parts.Add(repeatPercent);
        return parts.Count == 0 ? "Native values" : string.Join(" · ", parts);
    }

    static string DotBasis(IReadOnlyList<GuidedField> fields)
    {
        string value = ValueAt(fields, "DOTPercentOfBaseDamage.ValueRange");
        return value.Length == 0 ? "Native scaling" : value + " source-hit basis";
    }

    static string RadialSummary(IReadOnlyList<GuidedField> fields)
    {
        string radius = OriginalValue(fields, "DamageRadius");
        string percent = OriginalValue(fields, "RadialPercentOfBaseDamage");
        var parts = new List<string>();
        if (radius.Length > 0) parts.Add(radius + " m radius");
        string formatted = PercentValue(percent, "", " damage");
        if (formatted.Length > 0) parts.Add(formatted);
        return parts.Count == 0 ? "Native area" : string.Join(" · ", parts);
    }

    static string DurationValue(IReadOnlyList<GuidedField> fields)
    {
        string value = OriginalValue(fields, "Duration");
        return value.Length == 0 ? "Inherited duration" : value + " s";
    }

    static string ValueAt(IReadOnlyList<GuidedField> fields, string path)
        => fields.FirstOrDefault(f => f.Path.Equals(path, StringComparison.Ordinal))?.Original is { } value && value != "(absent)"
            ? value : "";

    static string PercentValue(string value, string prefix, string suffix)
    {
        if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number)) return "";
        return prefix + (number * 100m).ToString("0.###", CultureInfo.InvariantCulture) + "%" + suffix;
    }

    public static string DefaultDotDamageType(string displayName)
    {
        string status = displayName.Split('—', 2, StringSplitOptions.TrimEntries)[0];
        return status switch
        {
            "Heat" => "DT_FIRE", "Cold" => "DT_FREEZE", "Toxin" => "DT_POISON",
            "Electricity" => "DT_ELECTRICITY", "Slash" => "DT_SLASH", "Gas" => "DT_GAS",
            "Blast" => "DT_EXPLOSION", "Radiation" => "DT_RADIATION", "Magnetic" => "DT_MAGNETIC",
            "Viral" => "DT_VIRAL", "Corrosive" => "DT_CORROSIVE", "Impact" => "DT_IMPACT",
            "Puncture" => "DT_PUNCTURE", "Void" => "DT_RADIANT", "Tau" => "DT_SENTIENT",
            _ => "DT_ANY"
        };
    }

    public static string StatusScopeDescription(string path)
    {
        string leaf = Leaf(path);
        if (leaf.StartsWith("NokkoVIP", StringComparison.Ordinal))
            return "Scope: Nokko Colony VIP child handler. It inherits the base status behavior and owns only its explicit overrides.";
        if (leaf.StartsWith("Triangle", StringComparison.Ordinal))
            return "Scope: special Man-in-the-Wall Triangle-unit child handler. It inherits the base status behavior and owns only its explicit overrides.";
        if (path.Contains("/SpaceBattles/", StringComparison.Ordinal))
            return "Scope: Railjack and space-combat targets only; this is separate from ordinary ground-enemy status handling.";
        if (path.Contains("/Player/", StringComparison.Ordinal) || path.Contains("Tenno", StringComparison.OrdinalIgnoreCase))
            return "Scope: statuses received by the player or Tenno; this does not edit ordinary enemies.";
        if (path.Contains("/BaseInjuryHandlers/Base", StringComparison.Ordinal)
            || leaf.StartsWith("BaseEnemy", StringComparison.Ordinal))
            return "Scope: shared ordinary-enemy base handler. Child handlers inherit it unless they explicitly override a field.";
        return "Scope: specialized child handler for the exact unit or encounter named above; unchanged fields continue to inherit from its parent.";
    }

    static string SpecializedScope(string faction, string core)
    {
        string qualifier = core;
        foreach (string token in new[] { "Damage", "Fire", "Poison", "Freeze", "Explosion", "Electricity", "Corrosive", "Radiation", "Magnetic", "Viral", "Slash", "Puncture", "Impact", "Gas", "Base" })
            qualifier = qualifier.Replace(token, "", StringComparison.Ordinal);
        qualifier = qualifier.Trim();
        return qualifier.Length == 0 ? faction + " override" : Pretty(qualifier) + " · " + faction + " override";
    }

    static string OriginalValue(IReadOnlyList<GuidedField> fields, string key)
        => fields.FirstOrDefault(f => f.Key == key && f.TopLevel)?.Original is { } value && value != "(absent)"
            ? value : "";

    static string FriendlyDamageType(string? value) => value switch
    {
        "DT_FIRE" => "Heat",
        "DT_FREEZE" => "Cold",
        "DT_POISON" => "Toxin",
        "DT_ELECTRICITY" => "Electricity",
        "DT_SLASH" => "Slash",
        "DT_GAS" => "Gas",
        null or "" => "status",
        _ => Pretty(value.StartsWith("DT_", StringComparison.Ordinal) ? value[3..] : value)
    };

    public static IReadOnlyList<GuidedField> BuildStatusFields(string text)
    {
        var fields = Extract.QueryableText(text)
            .Where(f => (!f.IsSet || f.Key == "ValueRange" && f.Path.Contains("DOTPercentOfBaseDamage", StringComparison.Ordinal))
                     && IsStatusBehaviorField(f))
            .Select(f => Create(f, GuidedMetadataKind.StatusEffect))
            .ToList();
        // These are common inherited DamageProc controls. Concrete handlers often omit them
        // because they use the native default. Always expose them uniformly; the sentinel
        // emits no override, while choosing a native value adds that field to this handler.
        foreach (string key in OptionalInheritedStatusFields)
        {
            if (fields.Any(f => f.Key == key)) continue;
            fields.Add(new GuidedField(key, key, DisplayFor(key, key), "Behavior & stacking",
                DescriptionFor(key, key) + " This field is currently inherited; it is written only when you choose an explicit value.",
                OriginalOrInherited, true, false,
                OptionsFor(key, key)));
        }
        return fields.OrderBy(f => GroupOrder(f.Group)).ThenBy(f => f.Path, StringComparer.Ordinal).ToList();
    }

    public static IReadOnlyList<GuidedField> BuildWeaponFields(string text)
    {
        return Extract.QueryableText(text)
            .Where(f => !f.IsSet && IsWeaponDamageField(f))
            .Select(f => Create(f, GuidedMetadataKind.WeaponDamage))
            .OrderBy(f => BehaviorIndex(f.Path)).ThenBy(f => GroupOrder(f.Group))
            .ThenBy(f => f.Path, StringComparer.Ordinal)
            .ToList();
    }

    public static string GroupFor(string category, string key, string path)
    {
        if (category.StartsWith("Status Effects", StringComparison.Ordinal)) return StatusGroup(key, path);
        if (IsWeaponCategory(category) && IsWeaponDamageKey(key, path)) return WeaponGroup(path);
        if (key is "Rarity" or "FusionLimit" or "UpgradeType" or "OperationType" || path.Contains("Upgrades", StringComparison.Ordinal))
            return "Upgrades";
        if (key.Contains("Script", StringComparison.OrdinalIgnoreCase) || path.Contains("Script", StringComparison.OrdinalIgnoreCase))
            return "Scripting";
        if (key.Contains("ArtifactSlot", StringComparison.Ordinal) || key.Contains("Compatibility", StringComparison.Ordinal))
            return "Equipment";
        return "General";
    }

    public static string DescriptionFor(string key, string path)
    {
        if (key == "LocalizeTag") return "Localization key used for the displayed item or mod name.";
        if (key == "LocTag") return "Localization key used for the displayed description. Changing gameplay effects does not automatically rewrite this text.";
        if (key == "Icon") return "Game asset path for the item's icon or card artwork.";
        if (key == "Rarity") return "Visible rarity tier used for the mod card, drop tables, and presentation.";
        if (key == "FusionLimit") return "Client-side rank/fusion tier enum. This is not the exact numeric server max rank.";
        if (key == "BaseDrain") return "Base mod-capacity drain tier before rank scaling.";
        if (key == "ProductCategory") return "Broad inventory/store family used by the game to classify this object.";
        if (key == "ItemCompatibility") return "Exact equipment type that is allowed to install this mod.";
        if (key == "ArtifactPolarity") return "Mod polarity used for slot matching and capacity cost.";
        if (key == "UpgradeType") return "Gameplay stat or behavior modified by this upgrade entry.";
        if (key == "OperationType") return "How the upgrade value combines with the underlying stat: add, multiply, set, or stacking multiply.";
        if (key == "Value" && IsUpgradeContainer(path)) return "Magnitude of this status modifier. Read it together with Upgrade Type and Operation Type in the same entry.";
        if (key == "Value") return "Primary magnitude of this upgrade entry. Its interpretation depends on UpgradeType and OperationType.";
        if (key == "AutoType") return "Whether the game automatically determines supporting upgrade behavior for this entry.";
        if (key == "DisplayAsMultiplier") return "Show the value as a multiplier in generated UI text.";
        if (key == "DisplayAsPercent") return "Show the value as a percentage in generated UI text.";
        if (key == "OverrideLocalization") return "Use this entry's explicit localization behavior instead of the inherited/default description rule.";
        if (key == "ReverseValueSymbol") return "Reverse the displayed positive/negative sign convention for this value.";
        if (key == "RoundingMode") return "Rule used when the displayed value must be rounded.";
        if (key == "RoundTo") return "Smallest display increment used by the rounding rule.";
        if (key == "SmallerIsBetter") return "Marks lower numerical values as beneficial for UI coloring and comparison.";
        if (key == "SymbolFilter") return "Optional symbol restriction controlling where this upgrade entry applies.";
        if (key == "UpgradeObject") return "Optional referenced object used by specialized upgrade behavior.";
        if (key == "ValidModifiers") return "Only these modifier identifiers are eligible for this upgrade entry.";
        if (key == "InvalidModifiers") return "Modifier identifiers explicitly excluded from this upgrade entry.";
        if (key == "ValidPostures") return "Posture/stance restrictions for applying this upgrade entry.";
        if (key == "ValidProcTypes") return "Status-proc identifiers that this upgrade entry is allowed to affect.";
        if (key == "ValidType") return "Native bit flags restricting which target or item variants are valid.";
        if (key == "CheckTypeOnInstall") return "Native install-time compatibility flag/value checked when equipping the mod.";
        if (key.Contains("Script", StringComparison.OrdinalIgnoreCase)) return "Lua script asset or script hook used by this metadata object.";
        if (key == "Duration") return "Base status duration in seconds.";
        if (key == "MaxStacks") return "Maximum simultaneous stacks accepted by this handler.";
        if (key == "MaxStacksWithOverguard") return "Separate Cold stack cap while the target still has Overguard.";
        if (key == "StackStyle") return "Controls proc storage and damage-source ownership. Original / inherited emits no override. OneActiveInstance shares one record; OneInstancePerInstigator keeps one record per weapon, ability, or other source; OneInstancePerHit keeps every proc independent.";
        if (key == "ProcInjuryType") return "Reaction animation requested when the status procs: ANY, PAIN, STAGGER, or STUN.";
        if (key == "CanBlockInjury") return "Whether this status reaction is allowed to interrupt or block another injury reaction.";
        if (key == "InjuryExitsWallSlide") return "Whether receiving this status reaction forces a target out of wall-slide movement.";
        if (key == "UseHighestDamageOverTime") return "DOT handlers only: 0 = normal merge; 1 = keep the highest incoming basis when this handler matches or consolidates DOT damage. Independent per-hit DOT instances remain separate. It affects only the selected handler scope.";
        if (key == "ConsolidateDamageOverTime") return "How this handler asks the engine to consolidate DOT instances. Observed native values are 0, 1, and 10; this is not the maximum-stack setting.";
        if (key == "ProcChance" && path.Contains("DamageOverTime", StringComparison.Ordinal))
            return "Proc-chance field inside this status handler's DOT payload. This is not the weapon's normal status chance.";
        if (key == "ProcChance") return "Weapon status chance as a decimal: 0.25 means 25%.";
        if (key == "ForcedProcs") return "One guaranteed PT_* status identifier in this attack's existing forced-proc list.";
        if (key == "ForcedDeathProcs") return "One PT_* status identifier in this attack's existing on-kill proc list.";
        if (key == "ForcedProcCount") return "How many entries from the forced-proc set are applied.";
        if (key == "Amount") return path.Contains("AttackData", StringComparison.Ordinal)
            ? "Base damage before mods for this exact attack profile."
            : "Native damage amount for this status behavior.";
        if (key == "DamageType" && IsUpgradeContainer(path))
            return "Damage-type filter for this nested upgrade. DT_ANY means the upgrade affects incoming damage of every type; it does not redefine the selected status element.";
        if (key == "Type" && path.Contains("DamageOverTime", StringComparison.Ordinal))
            return "Element dealt by this damage-over-time component.";
        if (key == "Type" && IsDamageContainer(path))
            return "Damage family or element dealt by this exact attack or radial-damage component.";
        if (key == "DamageType") return "Damage-type filter used by this exact metadata component.";
        if (key.StartsWith("DT_", StringComparison.Ordinal)) return "Damage distribution share for this element; values normally sum to 1 within an AttackData profile.";
        if (key == "criticalHitChance") return "Critical chance as a decimal: 0.2 means 20%.";
        if (key == "criticalHitDamageMultiplier") return "Critical damage multiplier for this attack profile.";
        if (key is "BaseDamageModifier" or "RepeatDamageModifier") return "Native first-stack or additional-stack modifier used by this status subclass.";
        if (key == "BaseFreezeModifier") return "Cold slow applied by the first stack. This value is a native modifier, not DOT damage.";
        if (key == "RepeatFreezeModifier") return "Additional Cold slow contributed by each repeated stack. This value is a native modifier, not DOT damage.";
        if (key == "FrozenDuration") return "Duration of the handler's full-freeze state in seconds.";
        if (key == "ReworkMaxStacks") return "Cold-specific stack threshold used by the reworked freeze logic.";
        if (key == "PostFrozenStacks") return "Cold stacks retained or applied after the target leaves full freeze.";
        if (key == "OverfreezeDurationPercentPerStack") return "Extra full-freeze duration contributed by stacks beyond the freeze threshold.";
        if (key == "ValueRange" && path.Contains("DOTPercentOfBaseDamage", StringComparison.Ordinal))
            return "Minimum and maximum fraction of the source hit used for DOT damage. {0.5,0.5} means a fixed 50% basis.";
        if (key == "ValueRange") return "Minimum and maximum metadata values, written as {min,max}.";
        if (key == "NumIncrements") return "Number of steps used to ramp this incremental status modifier to its final value.";
        if (key == "BuildUpTime") return "Native ramp-up time used by this incremental status modifier.";
        if (key == "IsAutonomous") return "Engine flag controlling whether this nested status modifier runs autonomously.";
        if (key == "DestroyAvatar") return "Whether the handler may destroy the affected avatar when its native death condition is reached.";
        if (key == "DestroyAvatarDelay") return "Delay in seconds before the handler's destroy-avatar behavior runs.";
        return "Exact metadata field. The emitted patch keeps the original field name and query path.";
    }

    public static IReadOnlyList<string> OptionsFor(string key, string path)
    {
        if (key == "DamageType" || key == "Type" && IsDamageContainer(path)) return DamageTypes;
        if (key is "ForcedProcs" or "ForcedDeathProcs") return ProcTypes;
        if (key == "OperationType") return UpgradeOperations;
        if (key == "StackStyle") return StackStyles;
        if (key == "ProcInjuryType") return ["ANY", "PAIN", "STAGGER", "STUN"];
        if (key == "ConsolidateDamageOverTime") return DotConsolidationModes;
        if (key == "UseHighestDamageOverTime") return HighestDotBasisModes;
        if (key is "CanBlockInjury" or "DestroyAvatar" or "IsSpace" or "InjuryExitsWallSlide") return ["0", "1"];
        return Array.Empty<string>();
    }

    public static string? Validate(GuidedField field, string value)
    {
        var compact = PatchGenerator.Compact(value);
        if (IsOptionalInheritedStatusField(field.Key) && compact == OriginalOrInherited)
            return null;
        if (field.Key == "ValueRange")
        {
            string[] values = compact.Trim().TrimStart('{').TrimEnd('}')
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (values.Length != 2 || values.Any(part => !double.TryParse(part, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double number) || number < 0))
                return $"{field.Display}: enter a non-negative range such as {{0.5,0.5}}.";
            return null;
        }
        if ((field.Key == "DamageType" || field.Key == "Type" && IsDamageContainer(field.Path))
            && !DamageTypes.Contains(compact, StringComparer.Ordinal))
            return $"{field.Display}: '{compact}' is not a known DT_* value.";
        if (field.Key is "UseHighestDamageOverTime" or "CanBlockInjury" or "DestroyAvatar" or "IsSpace" or "InjuryExitsWallSlide")
            if (compact is not "0" and not "1") return $"{field.Display}: use 0 or 1.";
        if (field.Key is "Duration" or "Amount" or "ProcChance" or "criticalHitChance" or "criticalHitDamageMultiplier"
            or "BaseDamageModifier" or "RepeatDamageModifier" or "ForcedProcCount" or "ProcExtraTime"
            or "DOTPercentOfBaseDamage" or "RadialPercentOfBaseDamage" or "RadialDamageTickRate"
            or "DestroyAvatarDelay" or "Value" or "BuildUpTime"
            || field.Key.StartsWith("DT_", StringComparison.Ordinal))
        {
            if (!double.TryParse(compact, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number < 0)
                return $"{field.Display}: enter a non-negative number.";
            // Weapon status and critical chance can legitimately exceed 1 after editing;
            // the engine interprets the extra probability rather than requiring a 0..1 cap.
        }
        if (field.Key == "MaxStacks" && (!int.TryParse(compact, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stacks) || stacks < 0))
            return $"{field.Display}: enter a non-negative whole number.";
        if (field.Key is "ForcedProcs" or "ForcedDeathProcs")
        {
            foreach (var token in SetTokens(compact))
                if (!token.StartsWith("PT_", StringComparison.Ordinal))
                    return $"{field.Display}: '{token}' must be a PT_* proc identifier.";
        }
        if (field.Key == "OperationType" && !UpgradeOperations.Contains(compact, StringComparer.Ordinal))
            return $"{field.Display}: use one of {string.Join(", ", UpgradeOperations)}.";
        if (field.Key == "StackStyle" && !StackStyles.Contains(compact, StringComparer.Ordinal))
            return $"{field.Display}: choose Original / inherited or one of the three native storage modes.";
        if (field.Key == "ConsolidateDamageOverTime" && !DotConsolidationModes.Contains(compact, StringComparer.Ordinal))
            return $"{field.Display}: choose Original / inherited, 0, 1, or 10.";
        if (field.Key == "UpgradeType" && field.Options.Count >= 2 && !field.Options.Contains(compact, StringComparer.Ordinal))
            return $"{field.Display}: choose a decoded UpgradeType from the dropdown.";
        return null;
    }

    static GuidedField Create(QueryField field, GuidedMetadataKind kind)
    {
        string group = kind == GuidedMetadataKind.StatusEffect ? StatusGroup(field.Key, field.Path) : WeaponGroup(field.Path);
        string display = DisplayFor(field.Key, field.Path);
        return new GuidedField(field.Key, field.Path, display, group,
            DescriptionFor(field.Key, field.Path), field.Value, field.TopLevel, field.IsSet,
            OptionsFor(field.Key, field.Path));
    }

    static bool IsStatusBehaviorField(QueryField field)
    {
        if (field.TopLevel && StatusTopLevel.Contains(field.Key)) return true;
        return StatusBehaviorBlocks.Any(b => field.Path.Contains(b, StringComparison.Ordinal));
    }

    static bool IsWeaponDamageField(QueryField field) => IsWeaponDamageKey(field.Key, field.Path);

    static bool IsWeaponDamageKey(string key, string path)
    {
        bool attack = path.Contains("AttackData", StringComparison.Ordinal)
                   || path.Contains("RadialDamage", StringComparison.Ordinal)
                   || path.Contains("DamageOverTime", StringComparison.Ordinal);
        if (attack && (key is "Type" or "Amount" or "ProcChance" or "ForcedProcs" or "ForcedDeathProcs"
            or "ForcedProcCount" or "ProcExtraTime" or "DamageType" || key.StartsWith("DT_", StringComparison.Ordinal))) return true;
        return key is "criticalHitChance" or "criticalHitDamageMultiplier" or "RadialPercentOfBaseDamage"
            or "DOTPercentOfBaseDamage";
    }

    static string StatusGroup(string key, string path)
    {
        if (path.Contains("DamageOverTime", StringComparison.Ordinal) || path.Contains("DOTPercent", StringComparison.Ordinal)) return "Damage over time";
        if (path.Contains("Radial", StringComparison.Ordinal) || path.Contains("DamageRadius", StringComparison.Ordinal)) return "Radial damage";
        if (path.Contains("FrozenDebuffs", StringComparison.Ordinal)) return "Frozen-state effects";
        if (path.Contains("StackedUpgrades", StringComparison.Ordinal) || path.Contains("IncrementalUpgrades", StringComparison.Ordinal)) return "Stack effects";
        if (key is "Duration" or "MaxStacks" or "MaxStacksWithOverguard" or "StackStyle"
            or "ConsolidateDamageOverTime" or "UseHighestDamageOverTime" or "BaseFreezeModifier"
            or "RepeatFreezeModifier" or "FrozenDuration" or "ReworkMaxStacks" or "PostFrozenStacks"
            or "OverfreezeDurationPercentPerStack") return "Behavior & stacking";
        return "Status rules";
    }

    static string WeaponGroup(string path)
    {
        var match = Regex.Match(path, @"Behaviors\.(\d+)");
        string prefix = match.Success ? $"Fire mode {int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) + 1}" : "Weapon";
        if (path.Contains("AlternateAttackData", StringComparison.Ordinal)) return prefix + " · alternate damage";
        if (path.Contains("RadialDamage", StringComparison.Ordinal)) return prefix + " · radial damage";
        if (path.Contains("DamageOverTime", StringComparison.Ordinal)) return prefix + " · damage over time";
        return prefix + " · direct damage";
    }

    public static string DisplayFor(string key, string path)
    {
        var indexMatch = Regex.Match(path, @"\.(\d+)$");
        string ordinal = indexMatch.Success ? $" {int.Parse(indexMatch.Groups[1].Value, CultureInfo.InvariantCulture) + 1}" : "";
        if (key.StartsWith("DT_", StringComparison.Ordinal)) return Pretty(key[3..]) + " damage share";
        return key switch
        {
            "DamageType" when IsUpgradeContainer(path) => "Upgrade damage filter",
            "Type" when path.Contains("DamageOverTime", StringComparison.Ordinal) => "DOT damage type",
            "Type" when path.Contains("RadialDamage", StringComparison.Ordinal) => "Radial damage type",
            "Type" when path.Contains("AttackData", StringComparison.Ordinal) => "Attack damage type",
            "Amount" => path.Contains("AttackData", StringComparison.Ordinal) ? "Base damage" : "Damage amount",
            "ProcChance" => "Status chance",
            "ForcedProcs" => "Forced status proc" + ordinal,
            "ForcedDeathProcs" => "Forced status proc on kill" + ordinal,
            "ForcedProcCount" => "Forced proc count",
            "criticalHitChance" => "Critical chance",
            "criticalHitDamageMultiplier" => "Critical damage multiplier",
            "UseHighestDamageOverTime" => "Keep highest DOT damage basis",
            "ConsolidateDamageOverTime" => "DOT consolidation mode",
            "DOTPercentOfBaseDamage" => "DOT percent of base damage",
            "BaseDamageModifier" => "First-stack damage modifier",
            "RepeatDamageModifier" => "Additional-stack modifier",
            "MaxStacks" => "Maximum stacks",
            "StackStyle" => "Proc storage / damage ownership",
            _ => Pretty(key)
        };
    }

    static bool IsDamageContainer(string path)
        => path.Contains("AttackData", StringComparison.Ordinal)
        || path.Contains("DamageOverTime", StringComparison.Ordinal)
        || path.Contains("RadialDamage", StringComparison.Ordinal);

    static bool IsUpgradeContainer(string path)
        => path.Contains("Upgrades", StringComparison.Ordinal);

    static IEnumerable<string> SetTokens(string compact)
        => compact.Trim().TrimStart('{').TrimEnd('}').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    static int BehaviorIndex(string path)
    {
        var m = Regex.Match(path, @"Behaviors\.(\d+)");
        return m.Success && int.TryParse(m.Groups[1].Value, out int i) ? i : -1;
    }

    static int GroupOrder(string group)
    {
        if (group.Contains("Behavior", StringComparison.Ordinal)) return 0;
        if (group.Contains("direct", StringComparison.Ordinal)) return 0;
        if (group.Contains("Damage over", StringComparison.Ordinal)) return 1;
        if (group.Contains("radial", StringComparison.OrdinalIgnoreCase)) return 2;
        if (group.Contains("Stack", StringComparison.Ordinal)) return 3;
        return 4;
    }

    static string Pretty(string value)
    {
        value = value.Replace('_', ' ');
        var spaced = Regex.Replace(value, "(?<=[a-z0-9])(?=[A-Z])", " ");
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spaced.ToLowerInvariant());
    }

    static string Leaf(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }
}
