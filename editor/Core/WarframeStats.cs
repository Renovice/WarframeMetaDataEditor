using System.Globalization;

namespace MetadataPatchEditor.Core;

public sealed class WarframeBaseStat
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string Group { get; init; }
    public required string Description { get; init; }
    public required string Original { get; init; }
    public string Value { get; set; } = string.Empty;
}

public sealed record WarframeRankMilestone(string Path, decimal Original);

public sealed class WarframeRankStat
{
    public required string UpgradeType { get; init; }
    public required string Label { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<WarframeRankMilestone> Milestones { get; init; }
    public decimal StockTotal => Milestones.Sum(milestone => milestone.Original);
    public string Value { get; set; } = string.Empty;
}

public sealed record WarframeStatChange(
    string Key,
    string Path,
    bool TopLevel,
    string Original,
    string Value,
    string Display);

/// <summary>
/// Beginner-facing projection of powersuit metadata. It edits only fields that
/// are present in the composed type and preserves the native LevelUpgrades
/// milestone schedule when an aggregate rank-30 gain is changed.
/// </summary>
public sealed class WarframeStatsComposition
{
    static readonly (string Label, string Group, string Description, string[] Aliases)[] BaseDefinitions =
    [
        ("Base Health", "Survivability", "Health before rank bonuses and mods.",
            ["MaxHealthOverride", "MaxHealth", "Health"]),
        ("Base Shields", "Survivability", "Shield capacity before rank bonuses and mods.",
            ["MaxShieldOverride", "MaxShield", "Shield"]),
        ("Base Armor", "Survivability", "Armor before mods. This reduces incoming health damage.",
            ["ArmourRatingOverride", "ArmourRating", "Armour"]),
        ("Base Energy", "Resources & movement", "Maximum ability energy before rank bonuses and mods.",
            ["MaxEnergy", "MaxPower", "Power"]),
        ("Starting Energy", "Resources & movement", "Energy available when the mission starts.",
            ["InitialEnergy"]),
        ("Sprint Speed", "Resources & movement", "Native movement-speed multiplier used by the Warframe.",
            ["MovementSpeedMultiplier", "SprintSpeed", "RunSpeed"]),
    ];

    static readonly Dictionary<string, (string Label, string Description)> RankDefinitions =
        new(StringComparer.Ordinal)
        {
            ["AVATAR_HEALTH_MAX"] = ("Health gained by rank 30", "Total Health added by the existing native rank milestones."),
            ["AVATAR_SHIELD_MAX"] = ("Shields gained by rank 30", "Total Shields added by the existing native rank milestones."),
            ["AVATAR_POWER_MAX"] = ("Energy gained by rank 30", "Total maximum Energy added by the existing native rank milestones."),
            ["AVATAR_ARMOUR_MAX"] = ("Armor gained by rank 30", "Total Armor added by the existing native rank milestones."),
            ["AVATAR_ARMOR_MAX"] = ("Armor gained by rank 30", "Total Armor added by the existing native rank milestones."),
            ["AVATAR_STAMINA_MAX"] = ("Stamina gained by rank 30", "Total Stamina added by the existing native rank milestones."),
        };

    public required string Path { get; init; }
    public required IReadOnlyList<WarframeBaseStat> BaseStats { get; init; }
    public required IReadOnlyList<WarframeRankStat> RankStats { get; init; }

    public static WarframeStatsComposition Parse(string text)
    {
        var parsed = DumpParser.ParseText(text);
        var baseStats = new List<WarframeBaseStat>();
        foreach (var definition in BaseDefinitions)
        {
            var field = definition.Aliases
                .Select(alias => parsed.TopLevel.FirstOrDefault(candidate => candidate.Key == alias))
                .FirstOrDefault(candidate => candidate is not null);
            if (field is null) continue;
            string value = PatchGenerator.Compact(field.RawValue);
            baseStats.Add(new WarframeBaseStat
            {
                Key = field.Key,
                Label = definition.Label,
                Group = definition.Group,
                Description = definition.Description,
                Original = value,
                Value = value,
            });
        }

        var rankStats = new List<WarframeRankStat>();
        var levelUpgrades = parsed.TopLevel.FirstOrDefault(field => field.Key == "LevelUpgrades");
        if (levelUpgrades is not null)
        {
            foreach (var group in Extract.StatUpgrades(levelUpgrades.RawValue, "LevelUpgrades")
                         .Where(upgrade => RankDefinitions.ContainsKey(upgrade.Key))
                         .Select(upgrade => new
                         {
                             Upgrade = upgrade,
                             Parsed = decimal.TryParse(upgrade.Value, NumberStyles.Float,
                                 CultureInfo.InvariantCulture, out decimal value),
                             Value = value,
                         })
                         .Where(entry => entry.Parsed && entry.Value != 0)
                         .GroupBy(entry => entry.Upgrade.Key, StringComparer.Ordinal))
            {
                var definition = RankDefinitions[group.Key];
                var milestones = group.Select(entry =>
                    new WarframeRankMilestone(entry.Upgrade.Path, entry.Value)).ToList();
                decimal total = milestones.Sum(milestone => milestone.Original);
                rankStats.Add(new WarframeRankStat
                {
                    UpgradeType = group.Key,
                    Label = definition.Label,
                    Description = definition.Description
                        + $" {milestones.Count} existing milestone(s) are scaled proportionally; their rank positions are preserved.",
                    Milestones = milestones,
                    Value = Number(total),
                });
            }
        }

        return new WarframeStatsComposition
        {
            Path = parsed.Path,
            BaseStats = baseStats,
            RankStats = rankStats,
        };
    }

    public IReadOnlyList<WarframeStatChange> BuildChanges()
    {
        var changes = new List<WarframeStatChange>();
        foreach (var stat in BaseStats)
        {
            decimal value = RequiredNonNegative(stat.Value, stat.Label);
            string normalized = Number(value);
            if (normalized == Normalize(stat.Original)) continue;
            changes.Add(new WarframeStatChange(stat.Key, stat.Key, true, stat.Original, normalized, stat.Label));
        }

        foreach (var stat in RankStats)
        {
            decimal target = RequiredNonNegative(stat.Value, stat.Label);
            decimal stock = stat.StockTotal;
            if (target == stock) continue;
            if (stock <= 0 || stat.Milestones.Count == 0)
                throw new InvalidDataException($"{stat.Label}: no non-zero native milestones exist to scale.");

            decimal emitted = 0;
            for (int index = 0; index < stat.Milestones.Count; index++)
            {
                var milestone = stat.Milestones[index];
                decimal value = index == stat.Milestones.Count - 1
                    ? target - emitted
                    : decimal.Round(milestone.Original * target / stock, 9, MidpointRounding.AwayFromZero);
                emitted += value;
                string original = Number(milestone.Original);
                string normalized = Number(value);
                if (normalized == original) continue;
                changes.Add(new WarframeStatChange(
                    stat.UpgradeType,
                    milestone.Path,
                    false,
                    original,
                    normalized,
                    stat.Label));
            }
        }
        return changes;
    }

    public decimal? BaseValue(params string[] labels)
    {
        var stat = BaseStats.FirstOrDefault(candidate => labels.Contains(candidate.Label, StringComparer.Ordinal));
        return stat is not null && decimal.TryParse(stat.Value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out decimal value) ? value : null;
    }

    public decimal RankTotal(string upgradeType)
    {
        var stat = RankStats.FirstOrDefault(candidate => candidate.UpgradeType == upgradeType);
        return stat is not null && decimal.TryParse(stat.Value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out decimal value) ? value : 0;
    }

    static decimal RequiredNonNegative(string text, string label)
    {
        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value)
            || value < 0 || value > 1_000_000_000m)
            throw new InvalidDataException($"{label} must be a number from 0 to 1,000,000,000.");
        return value;
    }

    static string Normalize(string text)
        => decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value)
            ? Number(value)
            : text;

    static string Number(decimal value) => value.ToString("0.#########", CultureInfo.InvariantCulture);
}
