using System.Text.Json.Serialization;

namespace OpenWFMetadataUpdater;

public sealed class MetadataSource
{
    public string SnapshotId { get; set; } = "";
    public string? BuildLabel { get; set; }
    public string? ExecutableFileVersion { get; set; }
    public string PackagesSha256 { get; set; } = "";
    public string TocSha256 { get; set; } = "";
    public long CacheLength { get; set; }
    public DateTime CacheLastWriteUtc { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
}

public sealed class CountedStoreReward
{
    [JsonPropertyName("StoreItem")]
    public string StoreItem { get; set; } = "";
    [JsonPropertyName("ItemCount")]
    public int ItemCount { get; set; } = 1;
}

public sealed class GeneratedChallenge
{
    public List<CountedStoreReward> CountedRewards { get; set; } = [];
}

public sealed class NightwaveItemReward
{
    [JsonPropertyName("ItemCount")]
    public int ItemCount { get; set; } = 1;
    [JsonPropertyName("ItemType")]
    public string ItemType { get; set; } = "";
}

public sealed class NightwaveTitle
{
    public int Level { get; set; }
    public string Name { get; set; } = "";
    public int MinStanding { get; set; }
    public int MaxStanding { get; set; }
    public NightwaveItemReward? Reward { get; set; }
    public string? StoreItemReward { get; set; }
}

public sealed class GeneratedNightwaveSyndicate
{
    public string UniqueName { get; set; } = "";
    public string Parent { get; set; } = "";
    public string TemplateTag { get; set; } = "RadioLegionIntermission15Syndicate";
    public string Name { get; set; } = "";
    public string Currency { get; set; } = "";
    public string VendorManifest { get; set; } = "";
    public bool VendorPayloadAvailable { get; set; }
    public List<string> DailyChallenges { get; set; } = [];
    public List<string> WeeklyChallenges { get; set; } = [];
    public List<NightwaveTitle> Titles { get; set; } = [];
    public List<NightwaveItemReward> FeaturedRewards { get; set; } = [];
    public List<string> FeaturedStoreItemRewards { get; set; } = [];
}

public sealed class GeneratedVendorRange
{
    public int MinValue { get; set; }
    public int MaxValue { get; set; }
}

public sealed class GeneratedVendorItemPrice
{
    [JsonPropertyName("ItemCount")]
    public int ItemCount { get; set; }
    [JsonPropertyName("ItemType")]
    public string ItemType { get; set; } = "";
}

public sealed class GeneratedVendorRandomItemPrice
{
    public string Type { get; set; } = "";
    public GeneratedVendorRange Count { get; set; } = new();
}

public sealed class GeneratedVendorSyndicatePrice
{
    public string Tag { get; set; } = "";
    public int MinRank { get; set; }
    public int StandingCost { get; set; }
    public int ReductionPerPositiveRank { get; set; }
    public int IncreasePerNegativeRank { get; set; }
}

public sealed class GeneratedVendorFocusPrice
{
    public string Polarity { get; set; } = "";
    public int Cost { get; set; }
}

public sealed class GeneratedVendorOffer
{
    public string StoreItem { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public bool AlwaysOffered { get; set; }
    public int Bin { get; set; }
    public double? Probability { get; set; }
    public bool? RotatedWeekly { get; set; }
    public int Duplicates { get; set; }
    public object? Credits { get; set; }
    public object? Platinum { get; set; }
    public List<GeneratedVendorItemPrice>? ItemPrices { get; set; }
    public int? NumRandomItemPrices { get; set; }
    public object? DurationHours { get; set; }
    public int? PurchaseLimit { get; set; }
    public GeneratedVendorSyndicatePrice? Syndicate { get; set; }
    public GeneratedVendorFocusPrice? FocusXpCost { get; set; }
}

public sealed class GeneratedVendor
{
    public bool IsDynamic { get; set; }
    public bool? IsOneBinPerCycle { get; set; }
    public string? RequiredGoalTag { get; set; }
    public List<GeneratedVendorOffer> Items { get; set; } = [];
    public List<List<GeneratedVendorRandomItemPrice>>? RandomItemPricesPerBin { get; set; }
    public object? NumItems { get; set; }
    public List<int>? NumItemsPerBin { get; set; }
    public string? RandomSeedType { get; set; }
    public string SourceSha256 { get; set; } = "";
}

public sealed class GeneratedSyndicateFavour
{
    public string StoreItem { get; set; } = "";
    public int StandingCost { get; set; }
    public int CreditsCost { get; set; }
    public int RequiredLevel { get; set; }
    public bool RankUpReward { get; set; }
}

public sealed class GeneratedSyndicateFavours
{
    public string UniqueName { get; set; } = "";
    public string ManifestPath { get; set; } = "";
    public string SourceSha256 { get; set; } = "";
    public int SourceFavourCount { get; set; }
    public List<GeneratedSyndicateFavour> Favours { get; set; } = [];
    public List<GeneratedSyndicateFavourRejection> RejectedFavours { get; set; } = [];
}

public sealed class GeneratedSyndicateFavourRejection
{
    public GeneratedSyndicateFavour Favour { get; set; } = new();
    public string Reason { get; set; } = "";
}

public sealed class GeneratedDojoIngredient
{
    [JsonPropertyName("ItemType")]
    public string ItemType { get; set; } = "";
    [JsonPropertyName("ItemCount")]
    public int ItemCount { get; set; }
}

public sealed class GeneratedDojoResearch
{
    public string? ResultType { get; set; }
    public int Price { get; set; }
    public int Time { get; set; }
    public int SkipTimePrice { get; set; }
    public int ReplicatePrice { get; set; }
    public int? GuildXpValue { get; set; }
    public List<GeneratedDojoIngredient> Ingredients { get; set; } = [];
    public string? TechPrereq { get; set; }
}

public sealed class GeneratedDojoDeco
{
    public string ResultType { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "";
    public int Price { get; set; }
    public int Time { get; set; }
    public int SkipTimePrice { get; set; }
    public List<GeneratedDojoIngredient> Ingredients { get; set; } = [];
    public int? GuildXpValue { get; set; }
    public int? CapacityCost { get; set; }
    public bool? RequiredInVault { get; set; }
}

public sealed class GeneratedDojoRecipes
{
    public string ManifestPath { get; set; } = "";
    public string SourceSha256 { get; set; } = "";
    public int CurrentManifestCount { get; set; }
    public SortedDictionary<string, GeneratedDojoResearch> Research { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, GeneratedDojoDeco> Decos { get; set; } = new(StringComparer.Ordinal);
}

public sealed class GeneratedBundleComponent
{
    public string TypeName { get; set; } = "";
    public int PurchaseQuantity { get; set; } = 1;
}

public sealed class GeneratedBundle
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "";
    public bool IsCosmetic { get; set; }
    public List<GeneratedBundleComponent> Components { get; set; } = [];
}

public sealed class GeneratedCosmetic
{
    public string TypeName { get; set; } = "";
    public string StoreItem { get; set; } = "";
    public string ProductCategory { get; set; } = "";
    public string LocalizeTag { get; set; } = "";
}

public sealed class GeneratedItem
{
    public string TypeName { get; set; } = "";
    public string StoreItem { get; set; } = "";
    public string ProductCategory { get; set; } = "";
    public string LocalizeTag { get; set; } = "";
    public string CategorySourcePath { get; set; } = "";
    public string LocalizationSourcePath { get; set; } = "";
    public List<string> PublicExportDatasets { get; set; } = [];
    public string ServerRecordSource { get; set; } = "None";
    public SortedDictionary<string, string> EffectiveMetadata { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, string> EffectiveMetadataSourcePaths { get; set; } = new(StringComparer.Ordinal);
    public GeneratedBoosterPack? BoosterPack { get; set; }
}

public sealed class GeneratedBoosterPack
{
    public List<GeneratedBoosterPackComponent> Components { get; set; } = [];
    public List<SortedDictionary<string, double>> RarityWeightsPerRoll { get; set; } = [];
    public bool CanGiveDuplicates { get; set; }
}

public sealed class GeneratedBoosterPackComponent
{
    public string Item { get; set; } = "";
    public int Amount { get; set; } = 1;
    public double? Probability { get; set; }
    public double? PityIncreaseRate { get; set; }
    public string Rarity { get; set; } = "";
}

public sealed class ItemRegistrySummary
{
    public int DecodedTypeCount { get; set; }
    public int StoreWrapperCount { get; set; }
    public int ExactPairCount { get; set; }
    public int AdmittedCount { get; set; }
    public int RejectedCount { get; set; }
    public string RejectionSha256 { get; set; } = "";
    public int NoExplicitServerRecordCount { get; set; }
    public string NoExplicitServerRecordSha256 { get; set; } = "";
    public string? PublicExportPackageVersion { get; set; }
    public SortedDictionary<string, int> RejectionCounts { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> CategoryCounts { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> ServerRecordSourceCounts { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ItemRegistryRejection
{
    public string Reason { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string StoreItem { get; set; } = "";
}

public sealed class ItemRegistryAudit
{
    public ItemRegistrySummary Summary { get; set; } = new();
    public List<ItemRegistryRejection> Rejections { get; set; } = [];
    public List<string> NoExplicitServerRecordTypeNames { get; set; } = [];
}

public sealed class ConditionalReward
{
    public string? RequiredBaseUpgrade { get; set; }
    public List<string> Rewards { get; set; } = [];
    public List<CountedStoreReward> CountedRewards { get; set; } = [];
}

public sealed class AccountUpgradeReward
{
    public int? AccountVersionUpdate { get; set; }
    public List<string> Rewards { get; set; } = [];
}

public sealed class InboxReward
{
    public string? Sender { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
    public string? Icon { get; set; }
    public bool HighPriority { get; set; }
    public string? CustomData { get; set; }
    public List<string> Attachments { get; set; } = [];
    public List<CountedStoreReward> CountedAttachments { get; set; } = [];
}

public sealed class ChallengeManifestEntry
{
    public string ChallengePath { get; set; } = "";
    public string ChallengeKey { get; set; } = "";
    public string RecordSha256 { get; set; } = "";
    public bool? IsAutoregistered { get; set; }
    public bool? SilentReward { get; set; }
    public List<string> Items { get; set; } = [];
    public List<ConditionalReward> ConditionalRewards { get; set; } = [];
    public List<AccountUpgradeReward> AccountUpgradeRewards { get; set; } = [];
    public InboxReward? Message { get; set; }
}

public sealed class ChallengeManifest
{
    public string TypePath { get; set; } = "";
    public string FieldName { get; set; } = "";
    public string OwnTextSha256 { get; set; } = "";
    public List<ChallengeManifestEntry> Entries { get; set; } = [];
}

public sealed class PublicExportCoverage
{
    public string? PackageVersion { get; set; }
    public int PublicChallengeCount { get; set; }
    public int PrimaryCovered { get; set; }
    public int PrimaryTotal { get; set; }
    public List<string> PrimaryMissing { get; set; } = [];
    public int SilentCovered { get; set; }
    public int SilentTotal { get; set; }
    public List<string> SilentMissing { get; set; } = [];
}

[JsonConverter(typeof(JsonStringEnumConverter<ValidationSeverity>))]
public enum ValidationSeverity { Info, Warning, Error }

public sealed class ValidationIssue
{
    public ValidationSeverity Severity { get; set; }
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Path { get; set; }
}

public sealed class ValidationSummary
{
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int InfoCount { get; set; }
    public List<ValidationIssue> Issues { get; set; } = [];
}

public sealed class ChallengeMetadataSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public string Module { get; set; } = "challenges";
    public MetadataSource Source { get; set; } = new();
    public ChallengeManifest PrimaryManifest { get; set; } = new();
    public ChallengeManifest SilentManifest { get; set; } = new();
    public SortedDictionary<string, GeneratedChallenge> GeneratedChallenges { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, GeneratedNightwaveSyndicate> GeneratedNightwaveSyndicates { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, GeneratedVendor> GeneratedVendors { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, GeneratedSyndicateFavours> GeneratedSyndicateFavours { get; set; } = new(StringComparer.Ordinal);
    public GeneratedDojoRecipes GeneratedDojoRecipes { get; set; } = new();
    public SortedDictionary<string, GeneratedBundle> GeneratedBundles { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, GeneratedCosmetic> GeneratedCosmetics { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, GeneratedItem> GeneratedItems { get; set; } = new(StringComparer.Ordinal);
    public ItemRegistrySummary ItemRegistrySummary { get; set; } = new();
    public PublicExportCoverage PublicExportCoverage { get; set; } = new();
    public ValidationSummary Validation { get; set; } = new();
}

public sealed class ChangeSet
{
    public List<string> Added { get; set; } = [];
    public List<string> Removed { get; set; } = [];
    public List<string> Changed { get; set; } = [];
}

public sealed class ChangeReport
{
    public string SnapshotId { get; set; } = "";
    public string? ComparedWithSnapshotId { get; set; }
    public ChangeSet GeneratedChallenges { get; set; } = new();
    public ChangeSet PrimaryManifest { get; set; } = new();
    public ChangeSet SilentManifest { get; set; } = new();
    public ChangeSet NightwaveSyndicates { get; set; } = new();
    public ChangeSet GeneratedVendors { get; set; } = new();
    public ChangeSet GeneratedSyndicateFavours { get; set; } = new();
    public ChangeSet GeneratedDojoRecipes { get; set; } = new();
    public ChangeSet GeneratedBundles { get; set; } = new();
    public ChangeSet GeneratedCosmetics { get; set; } = new();
    public ChangeSet GeneratedItems { get; set; } = new();
    public ChangeSet ItemRegistrySummary { get; set; } = new();
    public bool RequiresReview { get; set; }
}

public sealed class RunManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string RunDirectory { get; set; } = "";
    public string CandidatePath { get; set; } = "";
    public string CandidateSha256 { get; set; } = "";
    public string ServerDirectory { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class ApplyState
{
    public string TargetPath { get; set; } = "";
    public string AppliedSha256 { get; set; } = "";
    public string? PreviousSha256 { get; set; }
    public string? BackupPath { get; set; }
    public DateTime AppliedAtUtc { get; set; }
    public DateTime? RolledBackAtUtc { get; set; }
}
