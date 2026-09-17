using System.Text.Json;
using MetadataPatchEditor.Core;

namespace OpenWFMetadataUpdater;

/// <summary>
/// Generates exact supplements for recipes present in the current DojoRecipeManifest but absent
/// from the installed warframe-public-export-plus snapshot. Package metadata owns recipe fields.
/// </summary>
public static class CurrentDojoRecipeModule
{
    public static GeneratedDojoRecipes Extract(
        PackagesBinDecoder.DecodeResult decoded,
        string datafileSnapshotDirectory,
        string? publicExportChallengesPath,
        List<ValidationIssue> issues)
    {
        CurrentDatafileManifest manifest = UpdaterFiles.ReadJson<CurrentDatafileManifest>(
            Path.Combine(datafileSnapshotDirectory, "datafile-manifest.json"));
        CurrentDatafileRecord record = manifest.Records.Single(x => x.Family == "DojoRecipeManifest");
        string jsonPath = Path.Combine(
            datafileSnapshotDirectory,
            record.JsonRelativePath.Replace('/', Path.DirectorySeparatorChar));
        using var document = JsonDocument.Parse(File.ReadAllBytes(jsonPath));
        JsonElement recipeItems = document.RootElement.GetProperty("RecipeItems");
        string[] currentPaths = recipeItems.EnumerateArray()
            .Select(x => x.GetString() ?? "")
            .Where(x => x.Length != 0)
            .ToArray();
        var knownPaths = ReadPublicExportPaths(publicExportChallengesPath);
        var catalog = MetadataCatalog.Build(decoded);
        var result = new GeneratedDojoRecipes
        {
            ManifestPath = record.InternalPath,
            SourceSha256 = record.RawSha256,
            CurrentManifestCount = currentPaths.Length
        };

        foreach (string recipePath in currentPaths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (knownPaths.Contains(recipePath)) continue;
            if (!decoded.Types.ContainsKey(recipePath))
            {
                issues.Add(Issue(ValidationSeverity.Error, "DOJO_RECIPE_TYPE_MISSING",
                    "Current DojoRecipeManifest entry has no Packages.bin type.", recipePath));
                continue;
            }
            var fields = DeMetadataSyntax.Fields(catalog.ComposedText(recipePath));
            bool classified = false;
            if (DeMetadataSyntax.Boolean(fields, "IsResearch") == true)
            {
                var research = new GeneratedDojoResearch
                {
                    ResultType = FromStoreItem(DeMetadataSyntax.Scalar(fields, "ResultItem")),
                    Price = RequiredInt(fields, "ResearchRegularPrice", recipePath, issues),
                    Time = RequiredInt(fields, "ResearchTime", recipePath, issues),
                    SkipTimePrice = RequiredInt(fields, "SkipResearchTimePrice", recipePath, issues),
                    ReplicatePrice = RequiredInt(fields, "ReplicateTechPrice", recipePath, issues),
                    GuildXpValue = DeMetadataSyntax.Integer(fields, "GuildXpValue"),
                    Ingredients = ReadIngredients(fields, "ResearchIngredients", recipePath, issues),
                    TechPrereq = ResolveOptional(recipePath, DeMetadataSyntax.Scalar(fields, "TechPrereq"))
                };
                result.Research[recipePath] = research;
                classified = true;
            }
            if (DeMetadataSyntax.Scalar(fields, "ResultDecoration") is { Length: > 0 } decoration)
            {
                var deco = new GeneratedDojoDeco
                {
                    ResultType = ResolvePath(recipePath, decoration),
                    Name = DeMetadataSyntax.Scalar(fields, "LocalizeTag") ?? "",
                    Description = DeMetadataSyntax.Scalar(fields, "LocalizeDescTag") ?? "",
                    Icon = DeMetadataSyntax.Scalar(fields, "Icon") ?? "",
                    Price = RequiredInt(fields, "BuildPrice", recipePath, issues),
                    Time = RequiredInt(fields, "BuildTime", recipePath, issues),
                    SkipTimePrice = RequiredInt(fields, "SkipBuildTimePrice", recipePath, issues),
                    Ingredients = ReadIngredients(fields, "Ingredients", recipePath, issues),
                    GuildXpValue = DeMetadataSyntax.Integer(fields, "GuildXpValue"),
                    CapacityCost = DeMetadataSyntax.Integer(fields, "DecoCapacityCost"),
                    RequiredInVault = DeMetadataSyntax.Boolean(fields, "RequiredInVault")
                };
                if (!deco.ResultType.StartsWith("/Lotus/", StringComparison.Ordinal) ||
                    !deco.Name.StartsWith("/", StringComparison.Ordinal) ||
                    !deco.Description.StartsWith("/", StringComparison.Ordinal) ||
                    !deco.Icon.StartsWith("/", StringComparison.Ordinal))
                    issues.Add(Issue(ValidationSeverity.Error, "DOJO_DECO_METADATA_INVALID",
                        "Generated Dojo decoration is missing an exact result, localization, or icon path.", recipePath));
                result.Decos[recipePath] = deco;
                classified = true;
            }
            if (!classified)
                issues.Add(Issue(ValidationSeverity.Error, "DOJO_RECIPE_UNCLASSIFIED",
                    "Current-only Dojo recipe is neither research nor a decoration recipe.", recipePath));
        }

        issues.Add(Issue(ValidationSeverity.Info, "CURRENT_DOJO_RECIPES_EXTRACTED",
            $"Current DojoRecipeManifest contains {currentPaths.Length} entries; generated " +
            $"{result.Research.Count} research and {result.Decos.Count} decoration supplements absent from Public Export.",
            record.InternalPath));
        return result;
    }

    static HashSet<string> ReadPublicExportPaths(string? publicExportChallengesPath)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(publicExportChallengesPath)) return result;
        string file = Path.Combine(Path.GetDirectoryName(publicExportChallengesPath)!, "ExportDojoRecipes.json");
        if (!File.Exists(file)) return result;
        using var document = JsonDocument.Parse(File.ReadAllBytes(file));
        foreach (JsonProperty category in document.RootElement.EnumerateObject())
        {
            if (category.Value.ValueKind != JsonValueKind.Object) continue;
            foreach (JsonProperty recipe in category.Value.EnumerateObject()) result.Add(recipe.Name);
        }
        return result;
    }

    static List<GeneratedDojoIngredient> ReadIngredients(
        IReadOnlyDictionary<string, string> fields,
        string key,
        string ownerPath,
        List<ValidationIssue> issues)
    {
        if (!fields.TryGetValue(key, out string? raw)) return [];
        var result = new List<GeneratedDojoIngredient>();
        foreach (string entry in DeMetadataSyntax.List(raw))
        {
            var item = DeMetadataSyntax.Fields(entry);
            string type = ResolveOptional(ownerPath, DeMetadataSyntax.Scalar(item, "ItemType")) ?? "";
            int count = DeMetadataSyntax.Integer(item, "ItemCount") ?? 0;
            if (!type.StartsWith("/Lotus/", StringComparison.Ordinal) || count <= 0)
                issues.Add(Issue(ValidationSeverity.Error, "DOJO_INGREDIENT_INVALID",
                    "Dojo recipe ingredient has an invalid type or count.", ownerPath));
            result.Add(new GeneratedDojoIngredient { ItemType = type, ItemCount = count });
        }
        return result;
    }

    static int RequiredInt(
        IReadOnlyDictionary<string, string> fields,
        string key,
        string path,
        List<ValidationIssue> issues)
    {
        int? value = DeMetadataSyntax.Integer(fields, key);
        if (value is >= 0) return value.Value;
        issues.Add(Issue(ValidationSeverity.Error, "DOJO_INTEGER_MISSING",
            $"Current-only Dojo recipe has no valid {key}.", path));
        return 0;
    }

    static string? FromStoreItem(string? path)
        => path == null ? null : path.StartsWith("/Lotus/StoreItems/", StringComparison.Ordinal)
            ? "/Lotus/" + path["/Lotus/StoreItems/".Length..]
            : path;
    static string? ResolveOptional(string ownerPath, string? value)
        => string.IsNullOrWhiteSpace(value) ? null : ResolvePath(ownerPath, value);
    static string ResolvePath(string ownerPath, string value)
    {
        if (value.StartsWith("/", StringComparison.Ordinal)) return value;
        int slash = ownerPath.LastIndexOf('/');
        return ownerPath[..(slash + 1)] + value;
    }
    static ValidationIssue Issue(ValidationSeverity severity, string code, string message, string? path = null)
        => new() { Severity = severity, Code = code, Message = message, Path = path };
}
