using MetadataPatchEditor.Core;

namespace OpenWFMetadataUpdater;

public sealed class MetadataSignal
{
    public string Category { get; set; } = "";
    public string TypePath { get; set; } = "";
    public string OwnTextSha256 { get; set; } = "";
    public bool BackendRelevant { get; set; }
    public string? OwnText { get; set; }
}

public sealed class UpdateSignalReport
{
    public int SchemaVersion { get; set; } = 1;
    public int CurrentSignalCount { get; set; }
    public int BaselineSignalCount { get; set; }
    public SortedDictionary<string, int> AddedCategoryCounts { get; set; } = new(StringComparer.Ordinal);
    public List<MetadataSignal> Added { get; set; } = [];
    public List<MetadataSignal> Removed { get; set; } = [];
    public List<string> ReviewRequiredCategories { get; set; } =
    [
        "AMIR_CAMPAIGN", "NIGHTWAVE", "DEEP_ARCHIMEDEA", "UNKNOWN_EVENT"
    ];
}

public static class UpdateSignalDetector
{
    static readonly (string Category, string[] Needles)[] Rules =
    [
        ("AMIR_CAMPAIGN", ["Amir", "Shockwave", "Fables", "Frontier"]),
        ("NIGHTWAVE", ["Nightwave", "RadioLegion", "SeasonChallenge"]),
        ("DEEP_ARCHIMEDEA", ["DeepArchimedea", "Archimedea"]),
        ("PRIME_RESURGENCE", ["RevenantBaruuk", "MPVRevenantBaruuk"])
    ];

    public static UpdateSignalReport Detect(
        PackagesBinDecoder.DecodeResult decoded,
        PackagesBinDecoder.DecodeResult? baseline = null)
    {
        var report = new UpdateSignalReport();
        var current = Find(decoded);
        var previous = baseline == null ? new Dictionary<string, MetadataSignal>(StringComparer.Ordinal) : Find(baseline);
        report.CurrentSignalCount = current.Count;
        report.BaselineSignalCount = previous.Count;
        report.Added = current.Keys.Except(previous.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(path => current[path]).ToList();
        report.Removed = previous.Keys.Except(current.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(path => previous[path]).ToList();
        foreach (var group in report.Added.GroupBy(x => x.Category, StringComparer.Ordinal))
            report.AddedCategoryCounts[group.Key] = group.Count();
        return report;
    }

    static Dictionary<string, MetadataSignal> Find(PackagesBinDecoder.DecodeResult decoded)
    {
        var result = new Dictionary<string, MetadataSignal>(StringComparer.Ordinal);
        foreach (var pair in decoded.Types.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            foreach (var rule in Rules)
            {
                if (!rule.Needles.Any(needle => pair.Key.Contains(needle, StringComparison.OrdinalIgnoreCase))) continue;
                bool backendRelevant = IsBackendRelevant(pair.Key);
                result[pair.Key] = new MetadataSignal
                {
                    Category = rule.Category,
                    TypePath = pair.Key,
                    OwnTextSha256 = ChallengeModule.HashText(pair.Value.OwnText ?? ""),
                    BackendRelevant = backendRelevant,
                    OwnText = backendRelevant && !string.IsNullOrWhiteSpace(pair.Value.OwnText) ? pair.Value.OwnText : null
                };
                break;
            }
        }
        return result;
    }

    static bool IsBackendRelevant(string path)
        => path.StartsWith("/Lotus/Syndicates/", StringComparison.Ordinal)
           || path.Contains("/VendorManifests/", StringComparison.Ordinal)
           || path.StartsWith("/Lotus/Types/Challenges/", StringComparison.Ordinal)
           || path.StartsWith("/Lotus/Types/Items/", StringComparison.Ordinal)
           || path.StartsWith("/Lotus/StoreItems/", StringComparison.Ordinal)
           || path.Contains("/MissionDecks/", StringComparison.Ordinal)
           || path.Contains("/GameModes/", StringComparison.Ordinal);
}
