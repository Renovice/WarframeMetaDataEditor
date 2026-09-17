using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MetadataPatchEditor.Core;

namespace OpenWFMetadataUpdater;

public static class ChallengeModule
{
    public const string PrimaryPath = "/Lotus/Types/Challenges/PrimaryChallengeManifest";
    public const string SilentPath = "/Lotus/Types/Items/SilentChallengeRewardManifest";

    public static (ChallengeManifest Primary, ChallengeManifest Silent) Extract(PackagesBinDecoder.DecodeResult decoded)
    {
        var primaryText = RequiredOwnText(decoded, PrimaryPath);
        var silentText = RequiredOwnText(decoded, SilentPath);
        return (
            ParseManifest(PrimaryPath, "ChallengeRewardManifest", primaryText, primary: true),
            ParseManifest(SilentPath, "Rewards", silentText, primary: false));
    }

    public static SortedDictionary<string, GeneratedChallenge> GenerateServerChallenges(ChallengeManifest silent)
    {
        var generated = new SortedDictionary<string, GeneratedChallenge>(StringComparer.Ordinal);
        foreach (var entry in silent.Entries)
        {
            var rewards = entry.Items.Select(item => new CountedStoreReward
            {
                StoreItem = ToStoreItem(item),
                ItemCount = 1
            }).ToList();
            generated.Add(entry.ChallengePath, new GeneratedChallenge { CountedRewards = rewards });
        }
        return generated;
    }

    public static PublicExportCoverage ReadPublicExportCoverage(
        string? exportChallengesPath,
        ChallengeManifest primary,
        ChallengeManifest silent)
    {
        var result = new PublicExportCoverage
        {
            PrimaryTotal = primary.Entries.Count,
            SilentTotal = silent.Entries.Count
        };
        if (string.IsNullOrWhiteSpace(exportChallengesPath) || !File.Exists(exportChallengesPath))
        {
            result.PrimaryMissing = primary.Entries.Select(x => x.ChallengePath).ToList();
            result.SilentMissing = silent.Entries.Select(x => x.ChallengePath).ToList();
            return result;
        }

        using var doc = JsonDocument.Parse(File.ReadAllBytes(exportChallengesPath));
        var keys = doc.RootElement.EnumerateObject().Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        result.PublicChallengeCount = keys.Count;
        result.PrimaryMissing = primary.Entries.Select(x => x.ChallengePath).Where(x => !keys.Contains(x)).ToList();
        result.SilentMissing = silent.Entries.Select(x => x.ChallengePath).Where(x => !keys.Contains(x)).ToList();
        result.PrimaryCovered = result.PrimaryTotal - result.PrimaryMissing.Count;
        result.SilentCovered = result.SilentTotal - result.SilentMissing.Count;

        var packageJson = Path.Combine(Path.GetDirectoryName(exportChallengesPath)!, "package.json");
        if (File.Exists(packageJson))
        {
            using var pkg = JsonDocument.Parse(File.ReadAllBytes(packageJson));
            if (pkg.RootElement.TryGetProperty("version", out var version))
                result.PackageVersion = version.GetString();
        }
        return result;
    }

    public static ValidationSummary Validate(
        PackagesBinDecoder.DecodeResult decoded,
        ChallengeManifest primary,
        ChallengeManifest silent,
        SortedDictionary<string, GeneratedChallenge> generated,
        PublicExportCoverage coverage,
        bool publicExportWasRequested)
    {
        var issues = new List<ValidationIssue>();
        void Add(ValidationSeverity severity, string code, string message, string? path = null)
            => issues.Add(new ValidationIssue { Severity = severity, Code = code, Message = message, Path = path });

        if (!decoded.Aligned)
            Add(ValidationSeverity.Error, "PACKAGES_MISALIGNED", "Packages.bin did not decode to an aligned frame boundary.");
        if (primary.Entries.Count == 0)
            Add(ValidationSeverity.Error, "PRIMARY_EMPTY", "PrimaryChallengeManifest contains no entries.", PrimaryPath);
        if (silent.Entries.Count == 0)
            Add(ValidationSeverity.Error, "SILENT_EMPTY", "SilentChallengeRewardManifest contains no entries.", SilentPath);

        ValidateUnique(primary, "PRIMARY_DUPLICATE", Add);
        ValidateUnique(silent, "SILENT_DUPLICATE", Add);

        foreach (var entry in primary.Entries)
        {
            if (!decoded.Types.ContainsKey(entry.ChallengePath))
                Add(ValidationSeverity.Error, "PRIMARY_CHALLENGE_PATH_MISSING",
                    "The challenge referenced by PrimaryChallengeManifest does not exist in Packages.bin.", entry.ChallengePath);

            foreach (var reward in EnumeratePrimaryRewards(entry))
                if (!decoded.Types.ContainsKey(reward))
                    Add(ValidationSeverity.Error, "PRIMARY_REWARD_PATH_MISSING",
                        "A PrimaryChallengeManifest reward path does not exist in Packages.bin.", reward);
        }

        foreach (var entry in silent.Entries)
        {
            if (!decoded.Types.ContainsKey(entry.ChallengePath))
                Add(ValidationSeverity.Error, "SILENT_CHALLENGE_PATH_MISSING",
                    "The challenge referenced by SilentChallengeRewardManifest does not exist in Packages.bin.", entry.ChallengePath);
            if (entry.Items.Count == 0)
                Add(ValidationSeverity.Error, "SILENT_REWARD_EMPTY", "Silent challenge has no reward items.", entry.ChallengePath);

            foreach (var item in entry.Items)
            {
                var storeItem = ToStoreItem(item);
                if (!decoded.Types.ContainsKey(storeItem))
                    Add(ValidationSeverity.Error, "SILENT_STORE_ITEM_MISSING",
                        "The generated StoreItem path does not exist in Packages.bin.", storeItem);
            }
        }

        if (generated.Count != silent.Entries.Count)
            Add(ValidationSeverity.Error, "GENERATED_COUNT_MISMATCH",
                $"Generated challenge count {generated.Count} does not match Silent manifest count {silent.Entries.Count}.");

        if (publicExportWasRequested && coverage.PublicChallengeCount == 0)
            Add(ValidationSeverity.Error, "PUBLIC_EXPORT_UNREADABLE",
                "ExportChallenges.json was requested but could not be read.");
        foreach (var path in coverage.PrimaryMissing)
            Add(ValidationSeverity.Warning, "PUBLIC_EXPORT_PRIMARY_GAP",
                "Primary challenge is absent from warframe-public-export-plus; its full message/conditional semantics require review.", path);

        Add(ValidationSeverity.Info, "PRIMARY_COVERAGE",
            $"warframe-public-export-plus covers {coverage.PrimaryCovered}/{coverage.PrimaryTotal} Primary manifest entries.");
        Add(ValidationSeverity.Info, "SILENT_GENERATED",
            $"Generated {generated.Count} server challenge rewards from SilentChallengeRewardManifest; public export covers {coverage.SilentCovered}/{coverage.SilentTotal}.");

        return new ValidationSummary
        {
            ErrorCount = issues.Count(x => x.Severity == ValidationSeverity.Error),
            WarningCount = issues.Count(x => x.Severity == ValidationSeverity.Warning),
            InfoCount = issues.Count(x => x.Severity == ValidationSeverity.Info),
            Issues = issues
        };
    }

    static ChallengeManifest ParseManifest(string typePath, string fieldName, string ownText, bool primary)
    {
        var root = DeMetadataSyntax.Fields(ownText);
        if (!root.TryGetValue(fieldName, out var block))
            throw new InvalidDataException($"{typePath} has no {fieldName} field.");

        var manifest = new ChallengeManifest
        {
            TypePath = typePath,
            FieldName = fieldName,
            OwnTextSha256 = HashText(ownText)
        };
        foreach (var rawRecord in DeMetadataSyntax.List(block))
        {
            if (!rawRecord.StartsWith('{'))
                throw new InvalidDataException($"{typePath}.{fieldName} contains a non-record element: {rawRecord}");
            var fields = DeMetadataSyntax.Fields(rawRecord);
            string challengeKey = DeMetadataSyntax.Scalar(fields, primary ? "Challenge" : "challenge")
                ?? throw new InvalidDataException($"A record in {typePath} has no challenge field.");
            var entry = new ChallengeManifestEntry
            {
                ChallengeKey = challengeKey,
                ChallengePath = NormalizeChallenge(challengeKey),
                RecordSha256 = HashText(CanonicalRecord(rawRecord)),
                IsAutoregistered = DeMetadataSyntax.Boolean(fields, "isAutoregistered"),
                SilentReward = DeMetadataSyntax.Boolean(fields, "SilentReward")
            };

            if (fields.TryGetValue(primary ? "Items" : "items", out var items))
                entry.Items = DeMetadataSyntax.List(items);
            if (fields.TryGetValue("ConditionalRewards", out var conditional))
                entry.ConditionalRewards = ParseConditionalRewards(conditional);
            if (fields.TryGetValue(primary ? "AccountUpgradeRewards" : "accountUpgradeRewards", out var account))
                entry.AccountUpgradeRewards = ParseAccountRewards(account);
            if (fields.TryGetValue("MsgBoxEntry", out var message))
                entry.Message = ParseMessage(message);
            manifest.Entries.Add(entry);
        }
        manifest.Entries.Sort((a, b) => string.CompareOrdinal(a.ChallengePath, b.ChallengePath));
        return manifest;
    }

    static List<ConditionalReward> ParseConditionalRewards(string block)
    {
        var result = new List<ConditionalReward>();
        foreach (var raw in DeMetadataSyntax.List(block).Where(x => x.StartsWith('{')))
        {
            var fields = DeMetadataSyntax.Fields(raw);
            result.Add(new ConditionalReward
            {
                RequiredBaseUpgrade = DeMetadataSyntax.Scalar(fields, "RequiredBaseUpgrade"),
                Rewards = fields.TryGetValue("Rewards", out var rewards) ? DeMetadataSyntax.List(rewards) : [],
                CountedRewards = fields.TryGetValue("CountedRewards", out var counted) ? ParseCounted(counted) : []
            });
        }
        return result;
    }

    static List<AccountUpgradeReward> ParseAccountRewards(string block)
    {
        var result = new List<AccountUpgradeReward>();
        foreach (var raw in DeMetadataSyntax.List(block).Where(x => x.StartsWith('{')))
        {
            var fields = DeMetadataSyntax.Fields(raw);
            result.Add(new AccountUpgradeReward
            {
                AccountVersionUpdate = DeMetadataSyntax.Integer(fields, "AccountVersionUpdate"),
                Rewards = fields.TryGetValue("Rewards", out var rewards) ? DeMetadataSyntax.List(rewards) : []
            });
        }
        return result;
    }

    static InboxReward ParseMessage(string block)
    {
        var fields = DeMetadataSyntax.Fields(block);
        return new InboxReward
        {
            Sender = EmptyToNull(DeMetadataSyntax.Scalar(fields, "sndr")),
            Title = EmptyToNull(DeMetadataSyntax.Scalar(fields, "sub")),
            Body = EmptyToNull(DeMetadataSyntax.Scalar(fields, "msg")),
            Icon = EmptyToNull(DeMetadataSyntax.Scalar(fields, "icon")),
            HighPriority = DeMetadataSyntax.Boolean(fields, "highPriority") ?? false,
            CustomData = EmptyToNull(DeMetadataSyntax.Scalar(fields, "customData")),
            Attachments = fields.TryGetValue("att", out var attachments) ? DeMetadataSyntax.List(attachments) : [],
            CountedAttachments = fields.TryGetValue("countedAtt", out var counted) ? ParseCounted(counted) : []
        };
    }

    static List<CountedStoreReward> ParseCounted(string block)
    {
        var result = new List<CountedStoreReward>();
        foreach (var raw in DeMetadataSyntax.List(block).Where(x => x.StartsWith('{')))
        {
            var fields = DeMetadataSyntax.Fields(raw);
            var path = DeMetadataSyntax.Scalar(fields, "StoreItem") ?? DeMetadataSyntax.Scalar(fields, "ItemType");
            if (path == null) continue;
            result.Add(new CountedStoreReward
            {
                StoreItem = path.StartsWith("/Lotus/StoreItems/", StringComparison.Ordinal) ? path : ToStoreItem(path),
                ItemCount = DeMetadataSyntax.Integer(fields, "ItemCount") ?? 1
            });
        }
        return result;
    }

    static IEnumerable<string> EnumeratePrimaryRewards(ChallengeManifestEntry entry)
    {
        if (entry.Message != null)
            foreach (var item in entry.Message.Attachments) yield return item;
        foreach (var conditional in entry.ConditionalRewards)
            foreach (var item in conditional.Rewards) yield return item;
        foreach (var account in entry.AccountUpgradeRewards)
            foreach (var item in account.Rewards) yield return item;
    }

    static void ValidateUnique(
        ChallengeManifest manifest,
        string code,
        Action<ValidationSeverity, string, string, string?> add)
    {
        foreach (var duplicate in manifest.Entries.GroupBy(x => x.ChallengePath, StringComparer.Ordinal).Where(x => x.Count() > 1))
            add(ValidationSeverity.Error, code, $"Challenge path occurs {duplicate.Count()} times in the manifest.", duplicate.Key);
    }

    static string RequiredOwnText(PackagesBinDecoder.DecodeResult decoded, string path)
    {
        if (!decoded.Types.TryGetValue(path, out var type))
            throw new InvalidDataException($"Required metadata type is absent: {path}");
        if (string.IsNullOrWhiteSpace(type.OwnText))
            throw new InvalidDataException($"Required metadata type has no own text: {path}");
        return type.OwnText;
    }

    public static string NormalizeChallenge(string challenge)
        => challenge.StartsWith("/", StringComparison.Ordinal)
            ? challenge
            : "/Lotus/Types/Challenges/" + challenge.TrimStart('/');

    public static string ToStoreItem(string item)
    {
        if (item.StartsWith("/Lotus/StoreItems/", StringComparison.Ordinal)) return item;
        if (item.StartsWith("/Lotus/", StringComparison.Ordinal))
            return "/Lotus/StoreItems/" + item["/Lotus/".Length..];
        return "/Lotus/StoreItems/Types/Items/" + item.TrimStart('/');
    }

    static string CanonicalRecord(string raw)
        => string.Join('\n', raw.Replace("\r", "").Split('\n').Select(x => x.Trim()).Where(x => x.Length != 0));

    public static string HashText(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    static string? EmptyToNull(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
