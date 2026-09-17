using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MetadataPatchEditor.Core;

public sealed record ServerDatasetMatch(string Dataset, IReadOnlyList<object> Path, JsonObject Entry)
{
    public string DisplayPath => Dataset + " → " + string.Join(" → ", Path.Select(segment => segment.ToString()));
}

public static class ServerMetadataPatches
{
    public const string DefinitionsFileName = "server-definitions.json";
    static readonly HashSet<string> ForbiddenKeys = new(StringComparer.Ordinal) { "__proto__", "prototype", "constructor" };

    public static IReadOnlyList<ServerDatasetMatch> FindMatches(string serverRoot, string uniqueName)
    {
        if (!UpgradeIdentity.IsServerRoot(serverRoot))
            throw new DirectoryNotFoundException("Select the real SpaceNinjaServer root first.");
        if (string.IsNullOrWhiteSpace(uniqueName)) throw new ArgumentException("The selected item has no internal path.");

        var packageRoot = Path.Combine(serverRoot, "node_modules", "warframe-public-export-plus");
        var matches = new List<ServerDatasetMatch>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(packageRoot, "Export*.json").OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            var dataset = Path.GetFileNameWithoutExtension(file);
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            FindInElement(document.RootElement, uniqueName, dataset, [], matches, seen);
        }
        return matches;
    }

    static void FindInElement(
        JsonElement element, string uniqueName, string dataset, List<object> path,
        List<ServerDatasetMatch> matches, HashSet<string> seen)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            JsonElement? direct = null;
            string? embeddedUniqueName = null;
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name == uniqueName && property.Value.ValueKind == JsonValueKind.Object) direct = property.Value;
                if (property.Name == "uniqueName" && property.Value.ValueKind == JsonValueKind.String)
                    embeddedUniqueName = property.Value.GetString();
            }
            if (direct != null) AddMatch(dataset, [.. path, uniqueName], direct.Value, matches, seen);
            if (embeddedUniqueName == uniqueName && path.Count > 0) AddMatch(dataset, path, element, matches, seen);

            foreach (var property in element.EnumerateObject())
            {
                if (property.Name == uniqueName) continue;
                path.Add(property.Name);
                FindInElement(property.Value, uniqueName, dataset, path, matches, seen);
                path.RemoveAt(path.Count - 1);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                path.Add(index);
                FindInElement(item, uniqueName, dataset, path, matches, seen);
                path.RemoveAt(path.Count - 1);
                index++;
            }
        }
    }

    static void AddMatch(
        string dataset, IReadOnlyList<object> path, JsonElement entry,
        List<ServerDatasetMatch> matches, HashSet<string> seen)
    {
        var key = dataset + "\0" + JsonSerializer.Serialize(path);
        if (!seen.Add(key)) return;
        matches.Add(new ServerDatasetMatch(dataset, path.ToArray(), NormalizeObject(entry)));
    }

    static JsonObject NormalizeObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected an object entry.");
        var result = new JsonObject();
        foreach (var property in element.EnumerateObject()) result[property.Name] = Normalize(property.Value);
        return result;
    }

    static JsonNode? Normalize(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => NormalizeObject(element),
        JsonValueKind.Array => new JsonArray(element.EnumerateArray().Select(Normalize).ToArray()),
        JsonValueKind.String => JsonValue.Create(element.GetString()),
        JsonValueKind.Number when element.TryGetInt64(out var integer) => JsonValue.Create(integer),
        JsonValueKind.Number when element.TryGetDecimal(out var number) => JsonValue.Create(number),
        JsonValueKind.Number => JsonValue.Create(element.GetDouble()),
        JsonValueKind.True => JsonValue.Create(true),
        JsonValueKind.False => JsonValue.Create(false),
        JsonValueKind.Null => null,
        _ => throw new InvalidDataException($"Unsupported JSON token {element.ValueKind}.")
    };

    public static string BuildPatchId(string dataset, IReadOnlyList<object> entryPath)
    {
        var identity = dataset + "\0" + JsonSerializer.Serialize(entryPath);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..12].ToLowerInvariant();
        return $"renovice.server.{dataset.ToLowerInvariant()}.{hash}";
    }

    public static string BuildPackageDirectoryName(string displayName, string dataset, IReadOnlyList<object> entryPath)
    {
        var safeName = Regex.Replace(displayName.Trim(), "[^A-Za-z0-9 _.-]+", "").Trim().TrimEnd('.');
        if (safeName.Length == 0) safeName = "Server Metadata";
        var suffix = BuildPatchId(dataset, entryPath).Split('.').Last();
        return $"{safeName} {dataset} [{suffix}]";
    }

    public static string BuildDefinitionJson(
        string dataset, IReadOnlyList<object> entryPath, IReadOnlyDictionary<string, JsonNode?> values)
    {
        if (!Regex.IsMatch(dataset, "^Export[A-Za-z0-9]+$")) throw new InvalidDataException("Invalid dataset name.");
        if (entryPath.Count == 0) throw new InvalidDataException("Server entry path must not be empty.");
        if (values.Count == 0) throw new InvalidDataException("At least one server field must be changed.");

        var pathArray = new JsonArray();
        foreach (var segment in entryPath)
        {
            if (segment is string text && text.Length > 0 && !ForbiddenKeys.Contains(text)) pathArray.Add(text);
            else if (segment is int index && index >= 0) pathArray.Add(index);
            else throw new InvalidDataException($"Unsupported server entry path segment: {segment}.");
        }
        var valuesObject = new JsonObject();
        foreach (var (field, value) in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(field) || ForbiddenKeys.Contains(field))
                throw new InvalidDataException($"Invalid server field name: {field}.");
            valuesObject[field] = value?.DeepClone();
        }
        var root = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["overrides"] = new JsonArray
            {
                new JsonObject
                {
                    ["dataset"] = dataset,
                    ["path"] = pathArray,
                    ["values"] = valuesObject,
                },
            },
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    }

    public static ServerPatchWriteResult WriteServerPackage(
        string serverRoot, string displayName, string dataset, IReadOnlyList<object> entryPath,
        IReadOnlyDictionary<string, JsonNode?> values, bool hasClientMetadata)
    {
        if (!UpgradeIdentity.IsServerRoot(serverRoot))
            throw new DirectoryNotFoundException("That folder is not a built SpaceNinjaServer root.");
        var definitionJson = BuildDefinitionJson(dataset, entryPath, values);
        var id = BuildPatchId(dataset, entryPath);
        var manifestJson = BuildManifestJson(displayName, id, hasClientMetadata);
        var enabledRoot = CombineRelative(serverRoot, UpgradeIdentity.EnabledPatchRelativePath);
        var disabledRoot = CombineRelative(serverRoot, UpgradeIdentity.DisabledPatchRelativePath);
        Directory.CreateDirectory(enabledRoot);
        Directory.CreateDirectory(disabledRoot);

        var enabledMatch = FindPackageById(enabledRoot, id);
        var disabledMatch = FindPackageById(disabledRoot, id);
        if (enabledMatch != null && disabledMatch != null)
            throw new InvalidDataException($"Patch id {id} exists in both Enabled and Disabled.");

        var packageDirectory = enabledMatch;
        if (packageDirectory == null && disabledMatch != null)
        {
            packageDirectory = Path.Combine(enabledRoot, Path.GetFileName(disabledMatch));
            if (Directory.Exists(packageDirectory)) throw new IOException($"Destination already exists: {packageDirectory}.");
            Directory.Move(disabledMatch, packageDirectory);
        }
        packageDirectory ??= Path.Combine(enabledRoot, BuildPackageDirectoryName(displayName, dataset, entryPath));
        if (Directory.Exists(packageDirectory) && enabledMatch == null)
            throw new IOException($"Refusing to overwrite an unrelated package directory: {packageDirectory}.");
        Directory.CreateDirectory(packageDirectory);

        var manifestFile = Path.Combine(packageDirectory, UpgradeIdentity.ManifestFileName);
        var definitionsFile = Path.Combine(packageDirectory, DefinitionsFileName);
        WriteAtomic(manifestFile, manifestJson);
        WriteAtomic(definitionsFile, definitionJson);
        return new ServerPatchWriteResult(packageDirectory, manifestFile, definitionsFile);
    }

    public static string DisableServerPackage(string serverRoot, string dataset, IReadOnlyList<object> entryPath)
    {
        if (!UpgradeIdentity.IsServerRoot(serverRoot))
            throw new DirectoryNotFoundException("Select the real SpaceNinjaServer repository folder first.");
        var id = BuildPatchId(dataset, entryPath);
        var enabledRoot = CombineRelative(serverRoot, UpgradeIdentity.EnabledPatchRelativePath);
        var source = FindPackageById(enabledRoot, id)
            ?? throw new FileNotFoundException($"No enabled server package with id {id} was found.");
        var disabledRoot = CombineRelative(serverRoot, UpgradeIdentity.DisabledPatchRelativePath);
        Directory.CreateDirectory(disabledRoot);
        var destination = Path.Combine(disabledRoot, Path.GetFileName(source));
        if (Directory.Exists(destination)) throw new IOException($"Disabled destination already exists: {destination}.");
        Directory.Move(source, destination);
        return destination;
    }

    public static bool HasEnabledServerPackage(string serverRoot, string dataset, IReadOnlyList<object> entryPath) =>
        UpgradeIdentity.IsServerRoot(serverRoot) &&
        FindPackageById(CombineRelative(serverRoot, UpgradeIdentity.EnabledPatchRelativePath), BuildPatchId(dataset, entryPath)) != null;

    static string BuildManifestJson(string displayName, string id, bool hasClientMetadata)
    {
        var root = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["id"] = id,
            ["name"] = $"{displayName} Server Metadata",
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

    static string? FindPackageById(string root, string id)
    {
        if (!Directory.Exists(root)) return null;
        string? match = null;
        foreach (var directory in Directory.GetDirectories(root).OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            var manifestFile = Path.Combine(directory, UpgradeIdentity.ManifestFileName);
            if (!File.Exists(manifestFile)) throw new InvalidDataException($"Package is missing {manifestFile}.");
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestFile));
            if (!doc.RootElement.TryGetProperty("id", out var idNode) || idNode.GetString() != id) continue;
            if (match != null) throw new InvalidDataException($"Duplicate patch id {id} in {root}.");
            match = directory;
        }
        return match;
    }

    static string CombineRelative(string root, string relative) =>
        Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    static void WriteAtomic(string output, string content)
    {
        var temp = output + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, output, true);
    }
}
