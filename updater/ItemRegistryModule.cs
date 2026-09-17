using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using MetadataPatchEditor.Core;

namespace OpenWFMetadataUpdater;

/// <summary>
/// Builds a loss-accounted registry of client store products. The denominator is every decoded
/// /Lotus/StoreItems wrapper. A wrapper is admitted only when its exact mirrored inventory type,
/// effective ProductCategory, and absolute localization tag are all present. Rejected records are
/// retained in a deterministic audit sidecar instead of being silently dropped.
///
/// Public Export membership is evidence that OpenWF already has an explicit record. It is not a
/// claim that every runtime acquisition or gameplay behavior has been validated.
/// </summary>
public static class ItemRegistryModule
{
    const string StorePrefix = "/Lotus/StoreItems/";
    const string LotusPrefix = "/Lotus/";

    // These are the exact public-export tables consumed by OpenWF's item acquisition dispatcher.
    // Tables used only for presentation, missions, enemies, vendors, or recipes' nested references
    // are deliberately excluded from the server-record claim.
    static readonly string[] ServerItemDatasets =
    [
        "ExportArcanes.json",
        "ExportBoosters.json",
        "ExportBundles.json",
        "ExportCreditBundles.json",
        "ExportCustoms.json",
        "ExportDrones.json",
        "ExportEmailItems.json",
        "ExportFlavour.json",
        "ExportFusionBundles.json",
        "ExportGear.json",
        "ExportKeys.json",
        "ExportRailjackWeapons.json",
        "ExportRecipes.json",
        "ExportResources.json",
        "ExportSentinels.json",
        "ExportUpgrades.json",
        "ExportWarframes.json",
        "ExportWeapons.json"
    ];

    // Exact effective fields needed by the acquisition, commerce, platform, and Arsenal-discovery
    // layers. Values remain in DE's metadata syntax and each one carries its inheritance source;
    // consumers may interpret only the fields they explicitly support.
    static readonly string[] PreservedEffectiveFields =
    [
        "RegularPrice",
        "PremiumPrice",
        "SellingPrice",
        "LocalizeDescTag",
        "Icon",
        "MarketMode",
        "ShowInMarket",
        "OneTimePurchasable",
        "AlwaysAvailable",
        "TradeCapability",
        "CodexSecret",
        "ExcludeFromCodex",
        "GenericRequirement",
        "ItemCompatibility",
        "Compatible",
        "CompatibleSlots",
        "PreventDuplicates",
        "AvailableOnPlatform[CP_WINDOWS]",
        "AvailableOnPlatform[CP_XBONE]",
        "AvailableOnPlatform[CP_PS4]",
        "AvailableOnPlatform[CP_SWITCH]",
        "AvailableOnPlatform[CP_PS5]",
        "AvailableOnPlatform[CP_XSX]",
        "AvailableOnPlatform[CP_IOS]",
        "AvailableOnPlatform[CP_ANDROID]"
    ];

    public sealed record Result(
        SortedDictionary<string, GeneratedItem> Items,
        ItemRegistryAudit Audit);

    sealed record ResolvedField(string Value, string SourcePath);

    public static Result Extract(
        PackagesBinDecoder.DecodeResult decoded,
        string? publicExportChallengesPath,
        IReadOnlyDictionary<string, GeneratedCosmetic> generatedCosmetics,
        List<ValidationIssue> issues)
    {
        var publicExport = ReadPublicExportIndex(publicExportChallengesPath, issues, out string? packageVersion);
        var items = new SortedDictionary<string, GeneratedItem>(StringComparer.Ordinal);
        var rejections = new List<ItemRegistryRejection>();
        int exactPairs = 0;

        var wrappers = decoded.Types.Keys
            .Where(path => path.StartsWith(StorePrefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        foreach (string storeItem in wrappers)
        {
            string typeName = FromStoreItem(storeItem);
            if (!decoded.Types.ContainsKey(typeName))
            {
                Reject("MISSING_MIRRORED_TYPE", typeName, storeItem, rejections);
                continue;
            }
            exactPairs++;

            ResolvedField? category = ResolveEffectiveField(decoded, typeName, "ProductCategory")
                                      ?? ResolveEffectiveField(decoded, storeItem, "ProductCategory");
            if (category == null || string.IsNullOrWhiteSpace(category.Value))
            {
                Reject("MISSING_PRODUCT_CATEGORY", typeName, storeItem, rejections);
                continue;
            }

            ResolvedField? localization = ResolveAbsoluteLocalization(decoded, typeName)
                                          ?? ResolveAbsoluteLocalization(decoded, storeItem);
            if (localization == null)
            {
                Reject("MISSING_ABSOLUTE_LOCALIZATION", typeName, storeItem, rejections);
                continue;
            }

            var datasets = new SortedSet<string>(StringComparer.Ordinal);
            if (publicExport.TryGetValue(typeName, out var typeDatasets)) datasets.UnionWith(typeDatasets);
            if (publicExport.TryGetValue(storeItem, out var storeDatasets)) datasets.UnionWith(storeDatasets);

            string serverRecordSource = generatedCosmetics.ContainsKey(typeName)
                ? "GeneratedCosmetic"
                : datasets.Count != 0 ? "PublicExport" : "None";
            var effectiveMetadata = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var effectiveMetadataSourcePaths = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (string fieldName in PreservedEffectiveFields)
            {
                ResolvedField? field = ResolveEffectiveField(decoded, typeName, fieldName)
                                       ?? ResolveEffectiveField(decoded, storeItem, fieldName);
                if (field == null) continue;
                effectiveMetadata[fieldName] = field.Value;
                effectiveMetadataSourcePaths[fieldName] = field.SourcePath;
            }
            items.Add(typeName, new GeneratedItem
            {
                TypeName = typeName,
                StoreItem = storeItem,
                ProductCategory = category.Value,
                LocalizeTag = localization.Value,
                CategorySourcePath = category.SourcePath,
                LocalizationSourcePath = localization.SourcePath,
                PublicExportDatasets = datasets.ToList(),
                ServerRecordSource = serverRecordSource,
                EffectiveMetadata = effectiveMetadata,
                EffectiveMetadataSourcePaths = effectiveMetadataSourcePaths,
                BoosterPack = BuildBoosterPack(decoded, typeName)
            });
        }

        rejections = rejections
            .OrderBy(x => x.Reason, StringComparer.Ordinal)
            .ThenBy(x => x.StoreItem, StringComparer.Ordinal)
            .ToList();
        var noExplicitServerRecord = items.Values
            .Where(x => x.ServerRecordSource == "None")
            .Select(x => x.TypeName)
            .Order(StringComparer.Ordinal)
            .ToList();
        var summary = new ItemRegistrySummary
        {
            DecodedTypeCount = decoded.Types.Count,
            StoreWrapperCount = wrappers.Count,
            ExactPairCount = exactPairs,
            AdmittedCount = items.Count,
            RejectedCount = rejections.Count,
            RejectionSha256 = HashLines(rejections.Select(x => $"{x.Reason}\t{x.StoreItem}\t{x.TypeName}")),
            NoExplicitServerRecordCount = noExplicitServerRecord.Count,
            NoExplicitServerRecordSha256 = HashLines(noExplicitServerRecord),
            PublicExportPackageVersion = packageVersion,
            RejectionCounts = CountBy(rejections.Select(x => x.Reason)),
            CategoryCounts = CountBy(items.Values.Select(x => x.ProductCategory)),
            ServerRecordSourceCounts = CountBy(items.Values.Select(x => x.ServerRecordSource))
        };
        var audit = new ItemRegistryAudit
        {
            Summary = summary,
            Rejections = rejections,
            NoExplicitServerRecordTypeNames = noExplicitServerRecord
        };
        Validate(decoded, generatedCosmetics, items, audit, issues);
        return new Result(items, audit);
    }

    static Dictionary<string, SortedSet<string>> ReadPublicExportIndex(
        string? exportChallengesPath,
        List<ValidationIssue> issues,
        out string? packageVersion)
    {
        packageVersion = null;
        var result = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(exportChallengesPath))
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Warning,
                Code = "ITEM_REGISTRY_PUBLIC_EXPORT_UNAVAILABLE",
                Message = "The item registry was generated without Public Export server-record evidence."
            });
            return result;
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(exportChallengesPath))!;
        string packagePath = Path.Combine(directory, "package.json");
        if (File.Exists(packagePath))
        {
            using var package = JsonDocument.Parse(File.ReadAllBytes(packagePath));
            if (package.RootElement.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String)
                packageVersion = version.GetString();
        }

        foreach (string fileName in ServerItemDatasets)
        {
            string path = Path.Combine(directory, fileName);
            if (!File.Exists(path))
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Code = "ITEM_REGISTRY_PUBLIC_EXPORT_DATASET_MISSING",
                    Message = $"OpenWF's item-record dataset {fileName} is missing.",
                    Path = path
                });
                continue;
            }

            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"{fileName} is not a top-level JSON object.");
            string dataset = Path.GetFileNameWithoutExtension(fileName);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name.StartsWith(LotusPrefix, StringComparison.Ordinal))
                    AddPublicExportPath(result, property.Name, dataset);

                // ExportBoosters is keyed by a StoreItem and dispatches the inventory type from typeName.
                if (fileName == "ExportBoosters.json" && property.Value.ValueKind == JsonValueKind.Object &&
                    property.Value.TryGetProperty("typeName", out var typeName) &&
                    typeName.ValueKind == JsonValueKind.String &&
                    typeName.GetString() is { } value && value.StartsWith(LotusPrefix, StringComparison.Ordinal))
                    AddPublicExportPath(result, value, dataset);
            }
        }
        return result;
    }

    static void AddPublicExportPath(
        Dictionary<string, SortedSet<string>> index,
        string path,
        string dataset)
    {
        if (!index.TryGetValue(path, out var datasets))
            index[path] = datasets = new SortedSet<string>(StringComparer.Ordinal);
        datasets.Add(dataset);
    }

    static ResolvedField? ResolveAbsoluteLocalization(PackagesBinDecoder.DecodeResult decoded, string path)
    {
        ResolvedField? field = ResolveEffectiveField(decoded, path, "LocalizeTag");
        return field != null && field.Value.StartsWith("/", StringComparison.Ordinal) ? field : null;
    }

    static ResolvedField? ResolveEffectiveField(
        PackagesBinDecoder.DecodeResult decoded,
        string path,
        string fieldName)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (string? current = path;
             current != null && decoded.Types.TryGetValue(current, out var type) && seen.Add(current);
             current = string.IsNullOrEmpty(type.Parent) ? null : type.Parent)
        {
            if (string.IsNullOrWhiteSpace(type.OwnText)) continue;
            string? value = DeMetadataSyntax.Scalar(DeMetadataSyntax.Fields(type.OwnText), fieldName);
            if (!string.IsNullOrWhiteSpace(value)) return new ResolvedField(value, current);
        }
        return null;
    }

    static GeneratedBoosterPack? BuildBoosterPack(PackagesBinDecoder.DecodeResult decoded, string typeName)
    {
        if (!typeName.StartsWith("/Lotus/Types/BoosterPacks/", StringComparison.Ordinal)) return null;
        ResolvedField? componentsField = ResolveEffectiveField(decoded, typeName, "Components");
        ResolvedField? weightsField = ResolveEffectiveField(decoded, typeName, "RarityWeights");
        if (componentsField == null || weightsField == null) return null;

        var components = new List<GeneratedBoosterPackComponent>();
        foreach (string raw in DeMetadataSyntax.List(componentsField.Value))
        {
            var fields = DeMetadataSyntax.Fields(raw);
            string? item = DeMetadataSyntax.Scalar(fields, "Item");
            string? rarity = DeMetadataSyntax.Scalar(fields, "Rarity");
            if (item == null || rarity == null || !item.StartsWith("/Lotus/", StringComparison.Ordinal)) return null;
            components.Add(new GeneratedBoosterPackComponent
            {
                Item = item,
                Amount = DeMetadataSyntax.Integer(fields, "Amount") ?? 1,
                Probability = Double(fields, "Probability") is 0 ? null : Double(fields, "Probability"),
                PityIncreaseRate = Double(fields, "PityIncreaseRate") is 0 ? null : Double(fields, "PityIncreaseRate"),
                Rarity = rarity
            });
        }

        var weights = new List<SortedDictionary<string, double>>();
        foreach (string raw in DeMetadataSyntax.List(weightsField.Value))
        {
            var fields = DeMetadataSyntax.Fields(raw);
            var row = new SortedDictionary<string, double>(StringComparer.Ordinal);
            foreach (string rarity in new[] { "COMMON", "UNCOMMON", "RARE", "LEGENDARY" })
            {
                double? value = Double(fields, rarity);
                if (value == null) return null;
                row[rarity] = value.Value;
            }
            weights.Add(row);
        }
        if (components.Count == 0 || weights.Count == 0) return null;
        return new GeneratedBoosterPack
        {
            Components = components,
            RarityWeightsPerRoll = weights,
            CanGiveDuplicates = ResolveEffectiveField(decoded, typeName, "PreventDuplicates")?.Value != "1"
        };
    }

    static double? Double(IReadOnlyDictionary<string, string> fields, string key)
        => double.TryParse(DeMetadataSyntax.Scalar(fields, key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : null;

    static void Reject(
        string reason,
        string typeName,
        string storeItem,
        List<ItemRegistryRejection> rejections)
        => rejections.Add(new ItemRegistryRejection { Reason = reason, TypeName = typeName, StoreItem = storeItem });

    static SortedDictionary<string, int> CountBy(IEnumerable<string> values)
        => new(values.GroupBy(x => x, StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal), StringComparer.Ordinal);

    static string HashLines(IEnumerable<string> values)
    {
        string canonical = string.Concat(values.Select(x => x + "\n"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    static string FromStoreItem(string storeItem) => LotusPrefix + storeItem[StorePrefix.Length..];

    static void Validate(
        PackagesBinDecoder.DecodeResult decoded,
        IReadOnlyDictionary<string, GeneratedCosmetic> generatedCosmetics,
        SortedDictionary<string, GeneratedItem> items,
        ItemRegistryAudit audit,
        List<ValidationIssue> issues)
    {
        ItemRegistrySummary summary = audit.Summary;
        if (summary.StoreWrapperCount == 0 || items.Count == 0)
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Code = "ITEM_REGISTRY_EMPTY",
                Message = "Packages.bin produced no admitted store-item registry."
            });
            return;
        }
        if (summary.AdmittedCount + summary.RejectedCount != summary.StoreWrapperCount ||
            summary.AdmittedCount != items.Count || summary.RejectedCount != audit.Rejections.Count)
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Code = "ITEM_REGISTRY_DENOMINATOR_MISMATCH",
                Message = "The item registry did not account for every decoded /Lotus/StoreItems wrapper exactly once."
            });
        }

        foreach (var (key, item) in items)
        {
            bool valid = key == item.TypeName &&
                         item.TypeName.StartsWith(LotusPrefix, StringComparison.Ordinal) &&
                         !item.TypeName.StartsWith(StorePrefix, StringComparison.Ordinal) &&
                         item.StoreItem == StorePrefix + item.TypeName[LotusPrefix.Length..] &&
                         decoded.Types.ContainsKey(item.TypeName) && decoded.Types.ContainsKey(item.StoreItem) &&
                         decoded.Types.ContainsKey(item.CategorySourcePath) &&
                         decoded.Types.ContainsKey(item.LocalizationSourcePath) &&
                         !string.IsNullOrWhiteSpace(item.ProductCategory) &&
                         item.LocalizeTag.StartsWith("/", StringComparison.Ordinal) &&
                         item.PublicExportDatasets.SequenceEqual(item.PublicExportDatasets.Order(StringComparer.Ordinal)) &&
                         item.EffectiveMetadata.Keys.SequenceEqual(item.EffectiveMetadata.Keys.Order(StringComparer.Ordinal)) &&
                         item.EffectiveMetadata.Keys.SequenceEqual(item.EffectiveMetadataSourcePaths.Keys) &&
                         item.EffectiveMetadataSourcePaths.Values.All(decoded.Types.ContainsKey) &&
                         (item.BoosterPack == null ||
                          (item.BoosterPack.Components.Count != 0 && item.BoosterPack.RarityWeightsPerRoll.Count != 0 &&
                           item.BoosterPack.Components.All(x => x.Item.StartsWith("/Lotus/", StringComparison.Ordinal) &&
                                                                x.Amount > 0 && !string.IsNullOrWhiteSpace(x.Rarity)) &&
                           item.BoosterPack.RarityWeightsPerRoll.All(x =>
                               x.Keys.SequenceEqual(new[] { "COMMON", "LEGENDARY", "RARE", "UNCOMMON" })))) &&
                         item.ServerRecordSource is "GeneratedCosmetic" or "PublicExport" or "None";
            if (!valid)
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Code = "ITEM_REGISTRY_INVALID_RECORD",
                    Message = "An admitted item registry record failed exact structural validation.",
                    Path = key
                });
            }
        }

        foreach (var cosmetic in generatedCosmetics.Values)
        {
            if (!items.TryGetValue(cosmetic.TypeName, out var item) ||
                item.StoreItem != cosmetic.StoreItem || item.ProductCategory != cosmetic.ProductCategory ||
                item.LocalizeTag != cosmetic.LocalizeTag || item.ServerRecordSource != "GeneratedCosmetic")
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Code = "ITEM_REGISTRY_COSMETIC_MISMATCH",
                    Message = "A verified generated cosmetic is missing from or disagrees with the complete item registry.",
                    Path = cosmetic.TypeName
                });
            }
        }

        string rejectionCounts = string.Join(", ", summary.RejectionCounts.Select(x => $"{x.Key}={x.Value}"));
        string adapterCounts = string.Join(", ", summary.ServerRecordSourceCounts.Select(x => $"{x.Key}={x.Value}"));
        issues.Add(new ValidationIssue
        {
            Severity = ValidationSeverity.Info,
            Code = "ITEM_REGISTRY_SUMMARY",
            Message = $"Accounted for {summary.StoreWrapperCount} store wrappers: {summary.AdmittedCount} admitted, " +
                      $"{summary.RejectedCount} rejected ({rejectionCounts}); explicit server records: {adapterCounts}."
        });
    }
}
