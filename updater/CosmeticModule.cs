using MetadataPatchEditor.Core;

namespace OpenWFMetadataUpdater;

/// <summary>
/// Extracts client-present cosmetic inventory/store pairs from Packages.bin. A record is emitted only
/// when both the real inventory type and its exact /Lotus/StoreItems wrapper exist, the effective
/// ProductCategory is a server cosmetic bin, and the pair has a localization tag. This avoids
/// manufacturing store paths for internal/dev types that the connecting client cannot present.
/// </summary>
public static class CosmeticModule
{
    static readonly HashSet<string> CosmeticCategories = new(StringComparer.Ordinal)
    {
        "WeaponSkins",
        "FlavourItems",
        "ShipDecorations"
    };

    const string StorePrefix = "/Lotus/StoreItems/";
    const string LotusPrefix = "/Lotus/";

    public static SortedDictionary<string, GeneratedCosmetic> Extract(
        PackagesBinDecoder.DecodeResult decoded,
        List<ValidationIssue> issues)
    {
        var catalog = MetadataCatalog.Build(decoded);
        var result = new SortedDictionary<string, GeneratedCosmetic>(StringComparer.Ordinal);

        // The store wrapper owns ProductCategory for many cosmetics, while other types own it on the
        // inventory object. Walk real types and wrappers so either authoritative layout is accepted.
        foreach (string typeName in decoded.Types.Keys.Order(StringComparer.Ordinal))
        {
            if (typeName.StartsWith(StorePrefix, StringComparison.Ordinal)) continue;
            string storeItem = ToStoreItem(typeName);
            if (!decoded.Types.ContainsKey(storeItem)) continue;
            AddPair(typeName, storeItem, catalog, result);
        }

        foreach (string storeItem in decoded.Types.Keys
                     .Where(x => x.StartsWith(StorePrefix, StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            string typeName = FromStoreItem(storeItem);
            if (!decoded.Types.ContainsKey(typeName)) continue;
            AddPair(typeName, storeItem, catalog, result);
        }

        Validate(decoded, result, issues);
        return result;
    }

    static void AddPair(
        string typeName,
        string storeItem,
        MetadataCatalog catalog,
        SortedDictionary<string, GeneratedCosmetic> result)
    {
        string? category = catalog.ProductCategory(typeName) ?? catalog.ProductCategory(storeItem);
        if (category == null || !CosmeticCategories.Contains(category)) return;

        string? localizeTag = catalog.LocalizeTag(typeName) ?? catalog.LocalizeTag(storeItem);
        // Real localized products use an absolute localization path. Bare identifiers such as
        // LotusSuitCustomization and HolsterCustomization are abstract/base type labels.
        if (string.IsNullOrWhiteSpace(localizeTag) || !localizeTag.StartsWith("/", StringComparison.Ordinal)) return;

        result.TryAdd(typeName, new GeneratedCosmetic
        {
            TypeName = typeName,
            StoreItem = storeItem,
            ProductCategory = category,
            LocalizeTag = localizeTag
        });
    }

    static void Validate(
        PackagesBinDecoder.DecodeResult decoded,
        SortedDictionary<string, GeneratedCosmetic> cosmetics,
        List<ValidationIssue> issues)
    {
        if (cosmetics.Count == 0)
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Code = "COSMETIC_CATALOG_EMPTY",
                Message = "Packages.bin produced no verified cosmetic inventory/store pairs."
            });
            return;
        }

        foreach (var (key, cosmetic) in cosmetics)
        {
            if (key != cosmetic.TypeName ||
                !decoded.Types.ContainsKey(cosmetic.TypeName) ||
                !decoded.Types.ContainsKey(cosmetic.StoreItem) ||
                !CosmeticCategories.Contains(cosmetic.ProductCategory) ||
                !cosmetic.LocalizeTag.StartsWith("/", StringComparison.Ordinal))
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Code = "COSMETIC_CATALOG_INVALID_PAIR",
                    Message = "A generated cosmetic is not an exact localized inventory/store pair.",
                    Path = key
                });
            }
        }

        string categoryCounts = string.Join(", ", cosmetics.Values
            .GroupBy(x => x.ProductCategory, StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => $"{x.Key}={x.Count()}"));
        issues.Add(new ValidationIssue
        {
            Severity = ValidationSeverity.Info,
            Code = "COSMETIC_CATALOG_SUMMARY",
            Message = $"Extracted {cosmetics.Count} exact localized cosmetic inventory/store pairs ({categoryCounts})."
        });
    }

    static string ToStoreItem(string typeName) => StorePrefix + typeName[LotusPrefix.Length..];
    static string FromStoreItem(string storeItem) => LotusPrefix + storeItem[StorePrefix.Length..];
}
