using System.Text;

namespace MetadataPatchEditor.Core;

public enum ModEquipmentKind
{
    Warframe,
    Necramech,
    Generic,
}

public sealed record ModSlotLayoutRequest(
    string TargetPath,
    string Category,
    int AdditionalOrdinarySlots,
    int AuraSlots,
    string NewSlotPolarity = "AP_ANY");

public sealed record ModSlotLayoutPlan(
    ModEquipmentKind Kind,
    string Patch,
    IReadOnlyList<string> OriginalSlots,
    IReadOnlyList<string> ModifiedSlots,
    int OriginalOrdinarySlots,
    int ResultOrdinarySlots,
    int ResultAuraSlots,
    int ResultUtilitySlots,
    IReadOnlyList<string> OriginalAdditionalBaseModTypes,
    IReadOnlyList<string> ModifiedAdditionalBaseModTypes,
    IReadOnlyList<string> Findings);

/// <summary>
/// Builds metadata-only mod-slot layouts using the client renderer's observed U43 rules.
/// It preserves every stock polarity at its original index and only appends AP_* entries.
/// </summary>
public static class ModSlotLayouts
{
    public static readonly string[] JadeSecondAuraBaseTypes =
    {
        "/Lotus/Types/Game/LotusAuraUpgrade",
        "/Lotus/Upgrades/Mods/Aura/FairyQuest/FairyQuestBaseAuraMod",
    };

    // DiegeticUpgradeCards U43 uses eight normal slots, then Aura, Utility, and
    // a second Aura. Warframe ArtifactSlots also contain two Arcane-tail entries
    // that the renderer subtracts before building the mod-card grid.
    public const int WarframeNormalPrefix = 8;
    public const int WarframeAuraIndex = 9;
    public const int WarframeUtilityIndex = 10;
    public const int WarframeSecondAuraIndex = 11;
    public const int WarframeArcaneTailEntries = 2;
    public const int NativeDefaultGridRows = 3;
    public const int NativeDefaultGridColumns = 4;
    public const int NativeDefaultGridCapacity = NativeDefaultGridRows * NativeDefaultGridColumns;

    public static ModEquipmentKind Classify(string category, string targetPath)
    {
        if (category.Equals("Necramechs", StringComparison.OrdinalIgnoreCase))
            return ModEquipmentKind.Necramech;

        if (category.Equals("Warframes", StringComparison.OrdinalIgnoreCase))
            return ModEquipmentKind.Warframe;

        // Path fallback is for CLI callers that do not have a catalog category. The GUI always
        // supplies one, preventing Operator/Archwing powersuits from being mistaken for Warframes.
        if (string.IsNullOrWhiteSpace(category))
        {
            if (targetPath.Contains("/EntratiMech/", StringComparison.OrdinalIgnoreCase))
                return ModEquipmentKind.Necramech;
            if (targetPath.Contains("/Powersuits/", StringComparison.OrdinalIgnoreCase)
                && !targetPath.Contains("/EntratiMech/", StringComparison.OrdinalIgnoreCase))
                return ModEquipmentKind.Warframe;
        }

        return ModEquipmentKind.Generic;
    }

    public static int WarframeOrdinaryCountFromArtifactCount(int artifactCount)
    {
        int cardSlots = Math.Max(0, artifactCount - WarframeArcaneTailEntries);
        return Math.Min(cardSlots, WarframeNormalPrefix) + Math.Max(0, cardSlots - WarframeSecondAuraIndex);
    }

    public static int WarframeAuraCountFromArtifactCount(int artifactCount)
    {
        int cardSlots = Math.Max(0, artifactCount - WarframeArcaneTailEntries);
        if (cardSlots >= WarframeSecondAuraIndex) return 2;
        return cardSlots >= WarframeAuraIndex ? 1 : 0;
    }

    public static int WarframeUtilityCountFromArtifactCount(int artifactCount)
        => Math.Max(0, artifactCount - WarframeArcaneTailEntries) >= WarframeUtilityIndex ? 1 : 0;

    public static ModSlotLayoutPlan Build(string composedText, ModSlotLayoutRequest request)
    {
        if (request.AdditionalOrdinarySlots < 0 || request.AdditionalOrdinarySlots > 16)
            throw new ArgumentOutOfRangeException(nameof(request), "Additional ordinary slots must be between 0 and 16.");
        if (request.AuraSlots is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(request), "Aura/Stance slots must be between 0 and 2.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(request.NewSlotPolarity, "^AP_[A-Z0-9_]+$"))
            throw new ArgumentException("New-slot polarity must be an AP_* token.", nameof(request));

        var original = EquipmentExperiments.ReadSet(composedText, "ArtifactSlots").ToList();
        var originalAdditionalTypes = EquipmentExperiments.ReadSet(composedText, "AdditionalBaseModTypes").ToList();
        var modifiedAdditionalTypes = originalAdditionalTypes.ToList();
        if (original.Count == 0)
            throw new InvalidOperationException("The selected item has no decoded ArtifactSlots field, so it is not safe to generate a mod layout.");
        if (original.Any(v => !v.StartsWith("AP_", StringComparison.Ordinal)))
            throw new InvalidOperationException("ArtifactSlots contains a non-polarity entry. Refusing to guess its layout.");

        var kind = Classify(request.Category, request.TargetPath);
        int originalOrdinary;
        int resultOrdinary;
        int resultAura;
        int resultUtility;
        int targetArtifactCount;
        var findings = new List<string>();

        switch (kind)
        {
            case ModEquipmentKind.Warframe:
                originalOrdinary = WarframeOrdinaryCountFromArtifactCount(original.Count);
                int originalAura = WarframeAuraCountFromArtifactCount(original.Count);
                resultUtility = WarframeUtilityCountFromArtifactCount(original.Count);
                if (originalOrdinary < WarframeNormalPrefix || originalAura < 1 || resultUtility < 1)
                    throw new InvalidOperationException(
                        $"Unsupported Warframe baseline: decoded {original.Count} ArtifactSlots maps to " +
                        $"{originalOrdinary} ordinary, {originalAura} Aura, {resultUtility} Utility. Refusing to rewrite a nonstandard layout.");

                resultOrdinary = originalOrdinary + request.AdditionalOrdinarySlots;
                resultAura = Math.Max(originalAura, request.AuraSlots);
                if (resultOrdinary > WarframeNormalPrefix && resultAura < 2)
                    throw new InvalidOperationException(
                        "This client fixes the second Aura at card index 11. More than eight ordinary Warframe slots therefore requires two Aura slots; choose 2 Aura slots or add no ordinary slots.");

                // 8 ordinary + Aura + Utility [+ second Aura] [+ ordinary overflow] + two Arcane-tail entries.
                int cardSlots = WarframeNormalPrefix + 1 + 1;
                if (resultAura == 2) cardSlots++;
                if (resultOrdinary > WarframeNormalPrefix) cardSlots += resultOrdinary - WarframeNormalPrefix;
                targetArtifactCount = cardSlots + WarframeArcaneTailEntries;
                targetArtifactCount = Math.Max(targetArtifactCount, original.Count);
                if (targetArtifactCount - WarframeArcaneTailEntries > NativeDefaultGridCapacity)
                    throw new InvalidOperationException(
                        $"This request needs {targetArtifactCount - WarframeArcaneTailEntries} visible mod cards, but the native Warframe grid is " +
                        $"{NativeDefaultGridRows}x{NativeDefaultGridColumns} ({NativeDefaultGridCapacity}). Metadata beyond that is clipped; request at most " +
                        "one additional ordinary Warframe slot until the runtime row/column hook is installed.");
                resultOrdinary = WarframeOrdinaryCountFromArtifactCount(targetArtifactCount);
                resultAura = WarframeAuraCountFromArtifactCount(targetArtifactCount);
                resultUtility = WarframeUtilityCountFromArtifactCount(targetArtifactCount);
                if (resultAura == 2)
                {
                    foreach (var type in JadeSecondAuraBaseTypes)
                        if (!modifiedAdditionalTypes.Contains(type, StringComparer.Ordinal))
                            modifiedAdditionalTypes.Add(type);
                }
                findings.Add("TRUE — the U43 Warframe renderer fixes slots 9/10/11 as Aura/Utility/second Aura.");
                findings.Add("TRUE — overflow positions after slot 11 are ordinary and remain in the native installed-card grid.");
                findings.Add("TRUE — Jade's stock metadata declares the two Aura base types emitted for a second Aura layout.");
                break;

            case ModEquipmentKind.Necramech:
                if (request.AuraSlots != 0)
                    throw new InvalidOperationException("Necramech Aura slots are not proven by metadata or live testing; choose 0 Aura slots.");
                originalOrdinary = original.Count;
                resultOrdinary = originalOrdinary + request.AdditionalOrdinarySlots;
                resultAura = 0;
                resultUtility = 0;
                targetArtifactCount = resultOrdinary;
                if (targetArtifactCount > NativeDefaultGridCapacity)
                    throw new InvalidOperationException(
                        $"Stock Necramechs already fill the native {NativeDefaultGridRows}x{NativeDefaultGridColumns} grid. More than " +
                        $"{NativeDefaultGridCapacity} visible slots requires the runtime row/column hook and would otherwise be clipped.");
                findings.Add("TRUE — stock Necramechs use a native 12-ordinary-slot grid backed directly by 12 ArtifactSlots.");
                findings.Add("UNPROVEN — Necramech Aura/Utility special slots; the editor refuses to invent them.");
                break;

            default:
                if (request.AuraSlots != 0)
                    throw new InvalidOperationException("Aura/Stance placement is category-specific and is not yet proven for this equipment category; choose 0.");
                originalOrdinary = original.Count;
                resultOrdinary = originalOrdinary + request.AdditionalOrdinarySlots;
                resultAura = 0;
                resultUtility = 0;
                targetArtifactCount = resultOrdinary;
                if (targetArtifactCount > NativeDefaultGridCapacity)
                    throw new InvalidOperationException(
                        $"The conservative native-grid limit is {NativeDefaultGridCapacity} ArtifactSlots. Larger layouts require a category-proven runtime row/column hook.");
                findings.Add("TRUE — ArtifactSlots is the selected item's logical polarity array.");
                findings.Add("UNPROVEN — exact special-slot classification for this category; generated changes are append-only and preserve all stock indices.");
                break;
        }

        if (targetArtifactCount > 32)
            throw new InvalidOperationException($"The requested layout needs {targetArtifactCount} ArtifactSlots; the editor safety limit is 32.");

        var modified = original.ToList();
        while (modified.Count < targetArtifactCount) modified.Add(request.NewSlotPolarity);

        var sb = new StringBuilder();
        sb.AppendLine("# RENOVICE MOD SLOT LAYOUT — generated, append-only, reversible");
        sb.Append("# Category: ").Append(request.Category).Append("; profile: ").AppendLine(kind.ToString());
        sb.Append("# ArtifactSlots: ").Append(original.Count).Append(" -> ").AppendLine(modified.Count.ToString());
        sb.Append("# Result: ordinary=").Append(resultOrdinary)
            .Append(" aura/stance=").Append(resultAura)
            .Append(" utility/exilus=").Append(resultUtility)
            .AppendLine("; Arcane layout unchanged (maximum two).");
        sb.AppendLine("# Revert: remove this patch file and restart/reload metadata patches.");
        sb.AppendLine(request.TargetPath);
        sb.Append("    ArtifactSlots=").AppendLine(EquipmentExperiments.FormatSet(modified));
        if (!modifiedAdditionalTypes.SequenceEqual(originalAdditionalTypes))
            sb.Append("    AdditionalBaseModTypes=").AppendLine(EquipmentExperiments.FormatSet(modifiedAdditionalTypes));

        return new ModSlotLayoutPlan(kind, sb.ToString(), original, modified,
            originalOrdinary, resultOrdinary, resultAura, resultUtility,
            originalAdditionalTypes, modifiedAdditionalTypes, findings);
    }
}
