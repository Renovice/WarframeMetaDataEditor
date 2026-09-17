using System.Text.Json;
using MetadataPatchEditor.Core;

namespace OpenWFMetadataUpdater;

/// <summary>
/// Extracts each current syndicate's complete favour list from its datafile. Packages.bin owns
/// the syndicate-to-manifest relationship; the datafile owns offer paths and prices.
/// </summary>
public static class CurrentSyndicateModule
{
    public static SortedDictionary<string, GeneratedSyndicateFavours> Extract(
        PackagesBinDecoder.DecodeResult decoded,
        string datafileSnapshotDirectory,
        List<ValidationIssue> issues)
    {
        CurrentDatafileManifest manifest = UpdaterFiles.ReadJson<CurrentDatafileManifest>(
            Path.Combine(datafileSnapshotDirectory, "datafile-manifest.json"));
        var records = manifest.Records
            .Where(x => x.Family == "Syndicates" && x.NormalizationStatus == "Converted")
            .ToDictionary(x => x.InternalPath, StringComparer.Ordinal);
        var manifestOwners = new Dictionary<string, List<(string Tag, string UniqueName)>>(StringComparer.Ordinal);

        foreach (var pair in decoded.Types
                     .Where(x => x.Key.StartsWith("/Lotus/Syndicates/", StringComparison.Ordinal) &&
                                 x.Key.EndsWith("Syndicate", StringComparison.Ordinal)))
        {
            string typePath = pair.Key;
            var type = pair.Value;
            if (string.IsNullOrWhiteSpace(type.OwnText)) continue;
            var ownFields = DeMetadataSyntax.Fields(type.OwnText);
            string? relativeManifest = DeMetadataSyntax.Scalar(ownFields, "Manifest");
            if (string.IsNullOrWhiteSpace(relativeManifest)) continue;
            string manifestPath = ResolvePath(typePath, relativeManifest);
            if (!records.ContainsKey(manifestPath)) continue;
            string tag = typePath.Split('/').Last();
            if (!manifestOwners.TryGetValue(manifestPath, out var owners))
                manifestOwners[manifestPath] = owners = [];
            owners.Add((tag, typePath));
        }

        var result = new SortedDictionary<string, GeneratedSyndicateFavours>(StringComparer.Ordinal);
        foreach (CurrentDatafileRecord record in records.Values.OrderBy(x => x.InternalPath, StringComparer.Ordinal))
        {
            string jsonPath = Path.Combine(
                datafileSnapshotDirectory,
                record.JsonRelativePath.Replace('/', Path.DirectorySeparatorChar));
            using var document = JsonDocument.Parse(File.ReadAllBytes(jsonPath));
            if (!document.RootElement.TryGetProperty("Favors", out JsonElement favoursElement)) continue;
            if (favoursElement.ValueKind != JsonValueKind.Array)
            {
                issues.Add(Issue(ValidationSeverity.Error, "SYNDICATE_FAVOURS_NOT_ARRAY",
                    "Current syndicate Favors value is not an array.", record.InternalPath));
                continue;
            }
            if (!manifestOwners.TryGetValue(record.InternalPath, out var owners))
            {
                issues.Add(Issue(ValidationSeverity.Error, "SYNDICATE_MANIFEST_OWNER_MISSING",
                    "No exact Packages.bin syndicate owner was found for this current favour manifest.",
                    record.InternalPath));
                continue;
            }

            var parsedFavours = new List<GeneratedSyndicateFavour>();
            var rejectedFavours = new List<GeneratedSyndicateFavourRejection>();
            foreach (JsonElement raw in favoursElement.EnumerateArray())
            {
                string storeItem = String(raw, "storeItem");
                var favour = new GeneratedSyndicateFavour
                {
                    StoreItem = storeItem,
                    StandingCost = Int(raw, "standingCost"),
                    CreditsCost = Int(raw, "creditsCost"),
                    RequiredLevel = Int(raw, "requiredLevel"),
                    RankUpReward = Int(raw, "availableAsFreeFavor") == 1
                };
                if (storeItem.Length == 0 && favour.StandingCost == 0 && favour.CreditsCost == 0 &&
                    favour.RequiredLevel == 0)
                {
                    issues.Add(Issue(ValidationSeverity.Info, "SYNDICATE_EMPTY_FAVOUR_PLACEHOLDER_SKIPPED",
                        "Skipped a structurally empty current favour placeholder with no purchasable item.",
                        record.InternalPath));
                    continue;
                }
                if ((!storeItem.StartsWith("/Lotus/StoreItems/", StringComparison.Ordinal) &&
                     !storeItem.StartsWith("/Lotus/Types/StoreItems/", StringComparison.Ordinal)) ||
                    favour.StandingCost < 0 || favour.CreditsCost < 0 || favour.RequiredLevel < 0)
                {
                    issues.Add(Issue(ValidationSeverity.Error, "SYNDICATE_FAVOUR_INVALID",
                        "Current syndicate favour has an invalid StoreItem, cost, or required level.",
                        $"{record.InternalPath}:{storeItem}"));
                    continue;
                }
                if (!decoded.Types.ContainsKey(storeItem))
                {
                    rejectedFavours.Add(new GeneratedSyndicateFavourRejection
                    {
                        Favour = favour,
                        Reason = "MISSING_CURRENT_STORE_ITEM_DEFINITION"
                    });
                    issues.Add(Issue(ValidationSeverity.Info,
                        "SYNDICATE_FAVOUR_MISSING_STORE_ITEM_REJECTED",
                        "Preserved but did not activate a current favour whose StoreItem has no current Packages.bin definition.",
                        $"{record.InternalPath}:{storeItem}"));
                    continue;
                }
                parsedFavours.Add(favour);
            }
            foreach (var owner in owners.OrderBy(x => x.Tag, StringComparer.Ordinal))
            {
                result[owner.Tag] = new GeneratedSyndicateFavours
                {
                    UniqueName = owner.UniqueName,
                    ManifestPath = record.InternalPath,
                    SourceSha256 = record.RawSha256,
                    SourceFavourCount = parsedFavours.Count + rejectedFavours.Count,
                    Favours = parsedFavours.Select(x => new GeneratedSyndicateFavour
                    {
                        StoreItem = x.StoreItem,
                        StandingCost = x.StandingCost,
                        CreditsCost = x.CreditsCost,
                        RequiredLevel = x.RequiredLevel,
                        RankUpReward = x.RankUpReward
                    }).ToList(),
                    RejectedFavours = rejectedFavours.Select(x => new GeneratedSyndicateFavourRejection
                    {
                        Favour = new GeneratedSyndicateFavour
                        {
                            StoreItem = x.Favour.StoreItem,
                            StandingCost = x.Favour.StandingCost,
                            CreditsCost = x.Favour.CreditsCost,
                            RequiredLevel = x.Favour.RequiredLevel,
                            RankUpReward = x.Favour.RankUpReward
                        },
                        Reason = x.Reason
                    }).ToList()
                };
            }
        }

        issues.Add(Issue(ValidationSeverity.Info, "CURRENT_SYNDICATE_FAVOURS_EXTRACTED",
            $"Extracted {result.Values.Sum(x => x.Favours.Count)} exact current favour rows for " +
            $"{result.Count} syndicates from hash-pinned datafiles; " +
            $"preserved {result.Values.Sum(x => x.RejectedFavours.Count)} rejected rows without activating them."));
        return result;
    }

    static string ResolvePath(string ownerPath, string value)
    {
        if (value.StartsWith("/", StringComparison.Ordinal)) return value;
        int slash = ownerPath.LastIndexOf('/');
        return ownerPath[..(slash + 1)] + value;
    }

    static int Int(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int parsed) ? parsed : 0;
    static string String(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    static ValidationIssue Issue(ValidationSeverity severity, string code, string message, string? path = null)
        => new() { Severity = severity, Code = code, Message = message, Path = path };
}
