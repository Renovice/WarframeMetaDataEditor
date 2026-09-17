using MetadataPatchEditor.Core;

namespace OpenWFMetadataUpdater;

/// <summary>
/// Adds every localized package whose complete component graph resolves to exact cosmetics from the
/// same Packages.bin snapshot. Mixed gameplay/cosmetic packages and unresolved/internal packages are
/// excluded, so the server never advertises a bundle it cannot grant through its normal bundle path.
/// </summary>
public static class CosmeticBundleModule
{
    const string PackagePrefix = "/Lotus/Types/StoreItems/Packages/";
    const string StorePrefix = "/Lotus/StoreItems/";

    public static void AddCosmeticBundles(
        PackagesBinDecoder.DecodeResult decoded,
        IReadOnlyDictionary<string, GeneratedCosmetic> cosmetics,
        SortedDictionary<string, GeneratedBundle> bundles,
        List<ValidationIssue> issues)
    {
        var cosmeticStoreItems = cosmetics.Values
            .Select(x => x.StoreItem)
            .ToHashSet(StringComparer.Ordinal);
        var candidates = new SortedDictionary<string, GeneratedBundle>(StringComparer.Ordinal);

        foreach (var (path, package) in decoded.Types
                     .Where(x => x.Key.StartsWith(PackagePrefix, StringComparison.Ordinal))
                     .OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(package.OwnText)) continue;
            var fields = DeMetadataSyntax.Fields(package.OwnText);
            string? name = DeMetadataSyntax.Scalar(fields, "LocalizeTag");
            if (string.IsNullOrWhiteSpace(name) || !name.StartsWith("/", StringComparison.Ordinal)) continue;
            if (!fields.TryGetValue("PackageComponents", out string? block)) continue;

            var components = new List<GeneratedBundleComponent>();
            bool invalid = false;
            foreach (string raw in DeMetadataSyntax.List(block).Where(x => x.StartsWith('{')))
            {
                var componentFields = DeMetadataSyntax.Fields(raw);
                string? typeName = DeMetadataSyntax.Scalar(componentFields, "TypeName");
                int quantity = DeMetadataSyntax.Integer(componentFields, "PurchaseQuantity") ?? 1;
                if (string.IsNullOrWhiteSpace(typeName) || quantity <= 0 ||
                    (!typeName.StartsWith(StorePrefix, StringComparison.Ordinal) &&
                     !typeName.StartsWith(PackagePrefix, StringComparison.Ordinal)))
                {
                    invalid = true;
                    break;
                }
                components.Add(new GeneratedBundleComponent { TypeName = typeName, PurchaseQuantity = quantity });
            }
            if (invalid || components.Count == 0) continue;

            candidates[path] = new GeneratedBundle
            {
                Name = name,
                Description = DeMetadataSyntax.Scalar(fields, "LocalizeDescTag") ?? "",
                Icon = DeMetadataSyntax.Scalar(fields, "Icon") ?? "",
                Components = components
            };
        }

        // Resolve nested packages to a fixed point. A package is cosmetic only if every leaf is an
        // exact admitted cosmetic; merely having one cosmetic component is insufficient.
        var cosmeticBundles = new HashSet<string>(StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            foreach (var (path, bundle) in candidates)
            {
                if (cosmeticBundles.Contains(path)) continue;
                if (bundle.Components.All(component =>
                        cosmeticStoreItems.Contains(component.TypeName) || cosmeticBundles.Contains(component.TypeName)))
                {
                    cosmeticBundles.Add(path);
                    changed = true;
                }
            }
        } while (changed);

        foreach (string path in cosmeticBundles.Order(StringComparer.Ordinal))
        {
            GeneratedBundle bundle = candidates[path];
            bundle.IsCosmetic = true;
            bundles[path] = bundle;
        }

        issues.Add(new ValidationIssue
        {
            Severity = ValidationSeverity.Info,
            Code = "COSMETIC_BUNDLE_CATALOG_SUMMARY",
            Message = $"Extracted {cosmeticBundles.Count} localized all-cosmetic packages from {candidates.Count} valid package candidates."
        });
    }
}
