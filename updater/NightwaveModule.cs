using System.Text.RegularExpressions;
using MetadataPatchEditor.Core;

namespace OpenWFMetadataUpdater;

/// <summary>
/// Extracts the current Nightwave contract from the installed client's Packages.bin. Public Export
/// may lag a game update, so this module generates only the package-backed syndicate fields and
/// explicitly marks the rotating vendor payload unavailable when Packages.bin contains no offers.
/// </summary>
public static partial class NightwaveModule
{
    public static SortedDictionary<string, GeneratedNightwaveSyndicate> Extract(
        PackagesBinDecoder.DecodeResult decoded,
        List<ValidationIssue> issues)
    {
        var candidates = decoded.Types
            .Select(pair => (pair.Key, Type: pair.Value, Match: NightwaveTagPattern().Match(pair.Key)))
            .Where(x => x.Match.Success && !string.IsNullOrWhiteSpace(x.Type.OwnText))
            .OrderByDescending(x => int.Parse(x.Match.Groups[1].Value))
            .ToList();

        var result = new SortedDictionary<string, GeneratedNightwaveSyndicate>(StringComparer.Ordinal);
        if (candidates.Count == 0)
        {
            issues.Add(Issue(ValidationSeverity.Error, "NIGHTWAVE_SYNDICATE_MISSING",
                "No numbered RadioLegionIntermission syndicate with package metadata was found."));
            return result;
        }

        // Older seasons remain authoritative in Public Export. Generate only the newest package
        // contract so a stale export can be supplemented without shadowing known-good history.
        var current = candidates[0];
        var fields = DeMetadataSyntax.Fields(current.Type.OwnText!);
        string tag = current.Key.Split('/').Last();
        var challengeFields = ReadSeasonChallengeFields(fields);
        var generated = new GeneratedNightwaveSyndicate
        {
            UniqueName = current.Key,
            Parent = current.Type.Parent,
            Name = DeMetadataSyntax.Scalar(fields, "SyndicateName") ?? "",
            Currency = DeMetadataSyntax.Scalar(fields, "SeasonCurrency") ?? "",
            VendorManifest = DeMetadataSyntax.Scalar(fields, "SeasonVendorManifest") ?? "",
            DailyChallenges = ReadList(challengeFields, "dailyChallenges"),
            WeeklyChallenges = ReadList(challengeFields, "weeklyChallenges"),
            Titles = ReadTitles(fields),
            FeaturedRewards = ReadRewards(fields, "SeasonFeaturedRewards"),
            FeaturedStoreItemRewards = ReadList(fields, "SeasonFeaturedStoreItemRewards")
        };

        if (generated.VendorManifest.Length != 0 && decoded.Types.TryGetValue(generated.VendorManifest, out var vendor))
            generated.VendorPayloadAvailable = !string.IsNullOrWhiteSpace(vendor.OwnText);

        Validate(decoded, tag, generated, issues);
        result[tag] = generated;
        return result;
    }

    /// <summary>
    /// Extracts package component contracts for bundle rewards referenced by the generated Nightwave
    /// definition. This is deliberately reference-driven: it supplements stale Public Export data
    /// without turning the challenge updater into an unrelated full-market exporter.
    /// </summary>
    public static SortedDictionary<string, GeneratedBundle> ExtractReferencedBundles(
        PackagesBinDecoder.DecodeResult decoded,
        IEnumerable<GeneratedNightwaveSyndicate> syndicates,
        List<ValidationIssue> issues)
    {
        var references = syndicates
            .SelectMany(x => x.Titles.Select(title => title.StoreItemReward)
                .Concat(x.FeaturedStoreItemRewards.Select(item => (string?)item)))
            .Where(x => !string.IsNullOrWhiteSpace(x) && x.StartsWith("/Lotus/Types/StoreItems/Packages/", StringComparison.Ordinal))
            .Select(x => x!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        var result = new SortedDictionary<string, GeneratedBundle>(StringComparer.Ordinal);
        foreach (string path in references)
        {
            if (!decoded.Types.TryGetValue(path, out var package) || string.IsNullOrWhiteSpace(package.OwnText))
            {
                issues.Add(Issue(ValidationSeverity.Error, "NIGHTWAVE_BUNDLE_METADATA_MISSING",
                    "A referenced Nightwave bundle has no package metadata.", path));
                continue;
            }

            var fields = DeMetadataSyntax.Fields(package.OwnText);
            var components = new List<GeneratedBundleComponent>();
            if (fields.TryGetValue("PackageComponents", out string? block))
            {
                foreach (string raw in DeMetadataSyntax.List(block).Where(x => x.StartsWith('{')))
                {
                    var component = DeMetadataSyntax.Fields(raw);
                    string? typeName = DeMetadataSyntax.Scalar(component, "TypeName");
                    if (string.IsNullOrWhiteSpace(typeName)) continue;
                    components.Add(new GeneratedBundleComponent
                    {
                        TypeName = typeName,
                        PurchaseQuantity = DeMetadataSyntax.Integer(component, "PurchaseQuantity") ?? 1
                    });
                }
            }

            if (components.Count == 0)
            {
                issues.Add(Issue(ValidationSeverity.Error, "NIGHTWAVE_BUNDLE_COMPONENTS_EMPTY",
                    "A referenced Nightwave bundle has no extractable PackageComponents.", path));
                continue;
            }
            foreach (var component in components)
            {
                if (!component.TypeName.StartsWith("/Lotus/StoreItems/", StringComparison.Ordinal))
                    issues.Add(Issue(ValidationSeverity.Error, "NIGHTWAVE_BUNDLE_COMPONENT_INVALID",
                        "A referenced Nightwave bundle component is not a StoreItem path.", component.TypeName));
                if (component.PurchaseQuantity <= 0)
                    issues.Add(Issue(ValidationSeverity.Error, "NIGHTWAVE_BUNDLE_QUANTITY_INVALID",
                        "A referenced Nightwave bundle component has a non-positive quantity.", component.TypeName));
            }

            result[path] = new GeneratedBundle
            {
                Name = DeMetadataSyntax.Scalar(fields, "LocalizeTag") ?? "",
                Description = DeMetadataSyntax.Scalar(fields, "LocalizeDescTag") ?? "",
                Icon = DeMetadataSyntax.Scalar(fields, "Icon") ?? "",
                Components = components
            };
            issues.Add(Issue(ValidationSeverity.Info, "NIGHTWAVE_BUNDLE_EXTRACTED",
                $"Extracted referenced Nightwave bundle with {components.Count} components.", path));
        }
        return result;
    }

    static List<NightwaveTitle> ReadTitles(IReadOnlyDictionary<string, string> fields)
    {
        if (!fields.TryGetValue("Titles", out var block)) return [];
        var result = new List<NightwaveTitle>();
        foreach (var raw in DeMetadataSyntax.List(block).Where(x => x.StartsWith('{')))
        {
            var title = DeMetadataSyntax.Fields(raw);
            var itemRewards = ReadRewards(title, "rewards");
            result.Add(new NightwaveTitle
            {
                Level = DeMetadataSyntax.Integer(title, "level") ?? -1,
                Name = DeMetadataSyntax.Scalar(title, "titleLoc") ?? "",
                MinStanding = DeMetadataSyntax.Integer(title, "minXP") ?? -1,
                MaxStanding = DeMetadataSyntax.Integer(title, "maxXP") ?? -1,
                Reward = itemRewards.FirstOrDefault(x => x.ItemType.Length != 0),
                StoreItemReward = EmptyToNull(DeMetadataSyntax.Scalar(title, "storeItemReward"))
            });
        }
        result.Sort((a, b) => b.Level.CompareTo(a.Level));
        return result;
    }

    static IReadOnlyDictionary<string, string> ReadSeasonChallengeFields(IReadOnlyDictionary<string, string> fields)
    {
        if (!fields.TryGetValue("SeasonChallenges", out var block))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? first = DeMetadataSyntax.List(block).FirstOrDefault(x => x.StartsWith('{'));
        return first == null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : DeMetadataSyntax.Fields(first);
    }

    static List<NightwaveItemReward> ReadRewards(IReadOnlyDictionary<string, string> fields, string key)
    {
        if (!fields.TryGetValue(key, out var block)) return [];
        var result = new List<NightwaveItemReward>();
        foreach (var raw in DeMetadataSyntax.List(block).Where(x => x.StartsWith('{')))
        {
            var reward = DeMetadataSyntax.Fields(raw);
            string? itemType = DeMetadataSyntax.Scalar(reward, "ItemType");
            if (string.IsNullOrEmpty(itemType)) continue;
            result.Add(new NightwaveItemReward
            {
                ItemCount = DeMetadataSyntax.Integer(reward, "ItemCount") ?? 1,
                ItemType = itemType
            });
        }
        return result;
    }

    static List<string> ReadList(IReadOnlyDictionary<string, string> fields, string key)
        => fields.TryGetValue(key, out var block) ? DeMetadataSyntax.List(block) : [];

    static void Validate(
        PackagesBinDecoder.DecodeResult decoded,
        string tag,
        GeneratedNightwaveSyndicate value,
        List<ValidationIssue> issues)
    {
        void Required(bool condition, string code, string message, string? path = null)
        {
            if (!condition) issues.Add(Issue(ValidationSeverity.Error, code, message, path ?? value.UniqueName));
        }

        Required(value.Name.Length != 0, "NIGHTWAVE_NAME_MISSING", $"{tag} has no SyndicateName.");
        Required(value.Currency.Length != 0, "NIGHTWAVE_CURRENCY_MISSING", $"{tag} has no SeasonCurrency.");
        Required(value.VendorManifest.Length != 0, "NIGHTWAVE_VENDOR_MISSING", $"{tag} has no SeasonVendorManifest.");
        Required(value.DailyChallenges.Count != 0, "NIGHTWAVE_DAILY_EMPTY", $"{tag} has no daily challenge pool.");
        Required(value.WeeklyChallenges.Count != 0, "NIGHTWAVE_WEEKLY_EMPTY", $"{tag} has no weekly challenge pool.");
        Required(value.Titles.Count >= 30, "NIGHTWAVE_TITLES_SHORT", $"{tag} exposes only {value.Titles.Count} reward ranks.");
        Required(value.Titles.Select(x => x.Level).Distinct().Count() == value.Titles.Count,
            "NIGHTWAVE_TITLE_DUPLICATE", $"{tag} contains duplicate title levels.");

        foreach (string challenge in value.DailyChallenges.Concat(value.WeeklyChallenges))
            Required(decoded.Types.ContainsKey(challenge), "NIGHTWAVE_CHALLENGE_REFERENCE_MISSING",
                $"{tag} references a challenge absent from Packages.bin.", challenge);

        issues.Add(Issue(ValidationSeverity.Info, "NIGHTWAVE_EXTRACTED",
            $"Extracted {tag}: {value.Titles.Count} ranks, {value.DailyChallenges.Count} daily and " +
            $"{value.WeeklyChallenges.Count} weekly/elite challenge references.", value.UniqueName));
    }

    static ValidationIssue Issue(ValidationSeverity severity, string code, string message, string? path = null)
        => new() { Severity = severity, Code = code, Message = message, Path = path };

    static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    [GeneratedRegex(@"^/Lotus/Syndicates/RadioLegionIntermission(\d+)Syndicate$")]
    private static partial Regex NightwaveTagPattern();
}
