using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MetadataPatchEditor.Core;

public sealed record UpgradeIdentityValue(string Rarity, int FusionLimit);
public sealed record ServerPatchWriteResult(string PackageDirectory, string ManifestFile, string DefinitionsFile);

public static class UpgradeIdentity
{
    public static readonly string[] ValidRarities = ["COMMON", "UNCOMMON", "RARE", "LEGENDARY"];
    public static readonly string[] ValidFusionLimits = ["QA_NONE", "QA_LOW", "QA_MEDIUM", "QA_HIGH", "QA_VERY_HIGH"];
    public const string ClientFusionUnchanged = "INHERIT / no client patch";
    public const string EnabledPatchRelativePath = "Metadata Patches/Enabled";
    public const string DisabledPatchRelativePath = "Metadata Patches/Disabled";
    public const string ManifestFileName = "patch.json";
    public const string DefinitionsFileName = "upgrade-definitions.json";

    public static string? Validate(string uniqueName, string rarity, int fusionLimit)
    {
        if (!uniqueName.StartsWith("/Lotus/Upgrades/", StringComparison.Ordinal))
            return "The selected type is not a /Lotus/Upgrades/ object.";
        if (!ValidRarities.Contains(rarity, StringComparer.Ordinal))
            return $"Rarity must be one of: {string.Join(", ", ValidRarities)}.";
        if (fusionLimit is < 0 or > 10)
            return "Exact maximum rank must be an integer from 0 through 10.";
        return null;
    }

    public static string? ValidateClientFusionLimit(string value) =>
        value == ClientFusionUnchanged || ValidFusionLimits.Contains(value, StringComparer.Ordinal)
            ? null
            : $"Client FusionLimit must be {ClientFusionUnchanged} or one of: {string.Join(", ", ValidFusionLimits)}.";

    public static string? FindOpenWfServer(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir != null)
        {
            if (IsServerRoot(dir.FullName)) return dir.FullName;
            var parent = dir.Parent;
            if (parent != null)
            {
                foreach (var sibling in SafeDirectories(parent))
                {
                    if (IsServerRoot(sibling)) return sibling;
                    foreach (var child in SafeDirectories(new DirectoryInfo(sibling)))
                        if (IsServerRoot(child)) return child;
                }
            }
            dir = parent;
        }
        return null;
    }

    public static bool IsServerRoot(string path) =>
        File.Exists(Path.Combine(path, "package.json")) &&
        File.Exists(Path.Combine(path, "node_modules", "warframe-public-export-plus", "ExportUpgrades.json")) &&
        Directory.Exists(Path.Combine(path, "src"));

    public static UpgradeIdentityValue ReadVanillaValue(string serverRoot, string uniqueName)
    {
        var packagePath = Path.Combine(serverRoot, "node_modules", "warframe-public-export-plus", "ExportUpgrades.json");
        return ParsePackageValue(File.ReadAllText(packagePath), uniqueName)
            ?? throw new InvalidOperationException($"OpenWF's installed public export does not contain {uniqueName}.");
    }

    public static UpgradeIdentityValue ReadEffectiveValue(string serverRoot, string uniqueName)
    {
        var value = ReadVanillaValue(serverRoot, uniqueName);
        var seenFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in ReadPackages(serverRoot, enabled: true))
        {
            var overrideValue = ReadDefinition(package, uniqueName);
            if (overrideValue == null) continue;
            if (overrideValue.Value.Rarity != null)
            {
                if (!seenFields.Add("rarity")) throw new InvalidDataException($"Enabled server packages conflict on {uniqueName}.rarity.");
                value = value with { Rarity = overrideValue.Value.Rarity };
            }
            if (overrideValue.Value.FusionLimit != null)
            {
                if (!seenFields.Add("fusionLimit")) throw new InvalidDataException($"Enabled server packages conflict on {uniqueName}.fusionLimit.");
                value = value with { FusionLimit = overrideValue.Value.FusionLimit.Value };
            }
        }
        return value;
    }

    public static UpgradeIdentityValue? ParsePackageValue(string json, string uniqueName)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty(uniqueName, out var value)) return null;
        if (!value.TryGetProperty("rarity", out var rarityNode) || rarityNode.ValueKind != JsonValueKind.String ||
            !value.TryGetProperty("fusionLimit", out var rankNode) || !rankNode.TryGetInt32(out var rank))
            throw new InvalidDataException($"The public-export entry for {uniqueName} lacks rarity or fusionLimit.");
        var rarity = rarityNode.GetString()!;
        var warning = Validate(uniqueName, rarity, rank);
        if (warning != null) throw new InvalidDataException(warning);
        return new UpgradeIdentityValue(rarity, rank);
    }

    public static string BuildPatchId(string uniqueName)
    {
        var tail = uniqueName.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "upgrade";
        var slug = Regex.Replace(tail.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length == 0) slug = "upgrade";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uniqueName)))[..10].ToLowerInvariant();
        return $"renovice.upgrade.{slug}.{hash}";
    }

    public static string BuildPackageDirectoryName(string displayName, string uniqueName)
    {
        var safeName = Regex.Replace(displayName.Trim(), "[^A-Za-z0-9 _.-]+", "").Trim().TrimEnd('.');
        if (safeName.Length == 0) safeName = uniqueName.Split('/').LastOrDefault() ?? "Upgrade";
        var idSuffix = BuildPatchId(uniqueName).Split('.').Last();
        return $"{safeName} Server Definition [{idSuffix}]";
    }

    public static string BuildManifestJson(string displayName, string uniqueName, bool hasClientMetadata)
    {
        var root = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["id"] = BuildPatchId(uniqueName),
            ["name"] = $"{displayName} Server Definition",
            ["version"] = "1.0.0",
            ["components"] = new JsonObject
            {
                ["clientMetadata"] = hasClientMetadata,
                ["serverDefinitions"] = true,
                ["inventoryMigration"] = false,
            },
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    }

    public static string BuildDefinitionJson(string uniqueName, string rarity, int fusionLimit)
    {
        var warning = Validate(uniqueName, rarity, fusionLimit);
        if (warning != null) throw new InvalidDataException(warning);
        var root = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["upgrades"] = new JsonObject
            {
                [uniqueName] = new JsonObject
                {
                    ["rarity"] = rarity,
                    ["fusionLimit"] = fusionLimit,
                },
            },
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    }

    public static ServerPatchWriteResult WriteServerPackage(
        string serverRoot, string displayName, string uniqueName, string rarity, int fusionLimit, bool hasClientMetadata)
    {
        if (!IsServerRoot(serverRoot))
            throw new DirectoryNotFoundException("That folder is not a built SpaceNinjaServer root with warframe-public-export-plus installed.");
        var warning = Validate(uniqueName, rarity, fusionLimit);
        if (warning != null) throw new InvalidDataException(warning);

        var id = BuildPatchId(uniqueName);
        var manifestJson = BuildManifestJson(displayName, uniqueName, hasClientMetadata);
        var definitionJson = BuildDefinitionJson(uniqueName, rarity, fusionLimit);
        var enabledRoot = CombineRelative(serverRoot, EnabledPatchRelativePath);
        var disabledRoot = CombineRelative(serverRoot, DisabledPatchRelativePath);
        Directory.CreateDirectory(enabledRoot);
        Directory.CreateDirectory(disabledRoot);

        var enabledMatch = FindPackageById(enabledRoot, id);
        var disabledMatch = FindPackageById(disabledRoot, id);
        if (enabledMatch != null && disabledMatch != null)
            throw new InvalidDataException($"Patch id {id} exists in both Enabled and Disabled; resolve the duplicate first.");
        foreach (var otherPackage in ReadPackages(serverRoot, enabled: true))
        {
            if (otherPackage == enabledMatch) continue;
            var other = ReadDefinition(otherPackage, uniqueName);
            if (other != null)
                throw new InvalidDataException($"Another enabled package already defines {uniqueName}: {otherPackage}.");
        }

        var packageDirectory = enabledMatch;
        if (packageDirectory == null && disabledMatch != null)
        {
            packageDirectory = Path.Combine(enabledRoot, Path.GetFileName(disabledMatch));
            if (Directory.Exists(packageDirectory))
                throw new IOException($"Cannot enable {id}: destination already exists at {packageDirectory}.");
            Directory.Move(disabledMatch, packageDirectory);
        }
        packageDirectory ??= Path.Combine(enabledRoot, BuildPackageDirectoryName(displayName, uniqueName));
        if (Directory.Exists(packageDirectory) && enabledMatch == null)
            throw new IOException($"Refusing to overwrite an unrelated package directory: {packageDirectory}.");
        Directory.CreateDirectory(packageDirectory);

        var manifestFile = Path.Combine(packageDirectory, ManifestFileName);
        var definitionsFile = Path.Combine(packageDirectory, DefinitionsFileName);
        WriteAtomic(manifestFile, manifestJson);
        WriteAtomic(definitionsFile, definitionJson);
        return new ServerPatchWriteResult(packageDirectory, manifestFile, definitionsFile);
    }

    public static string DisableServerPackage(string serverRoot, string uniqueName)
    {
        if (!IsServerRoot(serverRoot))
            throw new DirectoryNotFoundException("Select the real SpaceNinjaServer repository folder first.");
        var id = BuildPatchId(uniqueName);
        var enabledRoot = CombineRelative(serverRoot, EnabledPatchRelativePath);
        var disabledRoot = CombineRelative(serverRoot, DisabledPatchRelativePath);
        var source = FindPackageById(enabledRoot, id)
            ?? throw new FileNotFoundException($"No enabled server package with id {id} was found.");
        Directory.CreateDirectory(disabledRoot);
        var destination = Path.Combine(disabledRoot, Path.GetFileName(source));
        if (Directory.Exists(destination))
            throw new IOException($"Cannot disable the package because {destination} already exists.");
        Directory.Move(source, destination);
        return destination;
    }

    public static bool HasEnabledServerPackage(string serverRoot, string uniqueName) =>
        IsServerRoot(serverRoot) && FindPackageById(CombineRelative(serverRoot, EnabledPatchRelativePath), BuildPatchId(uniqueName)) != null;

    static IEnumerable<string> ReadPackages(string serverRoot, bool enabled)
    {
        var root = CombineRelative(serverRoot, enabled ? EnabledPatchRelativePath : DisabledPatchRelativePath);
        if (!Directory.Exists(root)) return [];
        var packages = Directory.GetDirectories(root)
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();
        foreach (var package in packages) _ = ReadManifestId(package);
        return packages;
    }

    static (string? Rarity, int? FusionLimit)? ReadDefinition(string packageDirectory, string uniqueName)
    {
        var file = Path.Combine(packageDirectory, DefinitionsFileName);
        if (!File.Exists(file)) throw new InvalidDataException($"Server metadata package is missing {file}.");
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var root = doc.RootElement;
        if (!root.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() != 1 ||
            !root.TryGetProperty("upgrades", out var upgrades) || upgrades.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{file} must contain schemaVersion=1 and an upgrades object.");
        if (!upgrades.TryGetProperty(uniqueName, out var value)) return null;
        string? rarity = null;
        int? rank = null;
        if (value.TryGetProperty("rarity", out var rarityNode)) rarity = rarityNode.GetString();
        if (value.TryGetProperty("fusionLimit", out var rankNode)) rank = rankNode.GetInt32();
        if (rarity == null && rank == null) throw new InvalidDataException($"{file} defines no supported fields for {uniqueName}.");
        var warning = Validate(uniqueName, rarity ?? "COMMON", rank ?? 0);
        if (warning != null) throw new InvalidDataException(warning);
        return (rarity, rank);
    }

    static string? FindPackageById(string root, string id)
    {
        if (!Directory.Exists(root)) return null;
        string? match = null;
        foreach (var directory in Directory.GetDirectories(root).OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal))
        {
            if (ReadManifestId(directory) != id) continue;
            if (match != null) throw new InvalidDataException($"Duplicate patch id {id} in {root}.");
            match = directory;
        }
        return match;
    }

    static string ReadManifestId(string packageDirectory)
    {
        var manifestFile = Path.Combine(packageDirectory, ManifestFileName);
        if (!File.Exists(manifestFile)) throw new InvalidDataException($"Server metadata package is missing {manifestFile}.");
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestFile));
        var root = doc.RootElement;
        if (!root.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() != 1 ||
            !root.TryGetProperty("id", out var idNode) || idNode.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("components", out var components) || components.ValueKind != JsonValueKind.Object ||
            !components.TryGetProperty("serverDefinitions", out var serverDefinitions) || !serverDefinitions.GetBoolean() ||
            !components.TryGetProperty("inventoryMigration", out var inventoryMigration) || inventoryMigration.GetBoolean())
            throw new InvalidDataException($"{manifestFile} is not a supported non-inventory server-definition manifest.");
        return idNode.GetString()!;
    }

    static string CombineRelative(string root, string relative) =>
        Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    static void WriteAtomic(string output, string content)
    {
        var temp = output + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, output, true);
    }

    static IEnumerable<string> SafeDirectories(DirectoryInfo directory)
    {
        try { return Directory.GetDirectories(directory.FullName); }
        catch { return []; }
    }
}
