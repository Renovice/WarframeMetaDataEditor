using System.Diagnostics;
using System.Text.Json;
using MetadataPatchEditor.Core;

namespace OpenWFMetadataUpdater;

public sealed class CurrentDatafileRecord
{
    public string Family { get; set; } = "";
    public string InternalPath { get; set; } = "";
    public string RawRelativePath { get; set; } = "";
    public long RawBytes { get; set; }
    public string RawSha256 { get; set; } = "";
    public string JsonRelativePath { get; set; } = "";
    public long JsonBytes { get; set; }
    public string JsonSha256 { get; set; } = "";
    public string JsonRootKind { get; set; } = "";
    public string NormalizationStatus { get; set; } = "Converted";
}

public sealed class CurrentDatafileManifest
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime GeneratedAtUtc { get; set; }
    public string CacheDirectory { get; set; } = "";
    public string MiscTocSha256 { get; set; } = "";
    public string ExporterSha256 { get; set; } = "";
    public string Bin2JsonSha256 { get; set; } = "";
    public List<string> RequestedFamilies { get; set; } = [];
    public List<CurrentDatafileRecord> Records { get; set; } = [];
}

/// <summary>
/// Takes a loss-accounted raw + JSON snapshot of the current client datafiles OpenWF uses for
/// syndicates, dojo construction, and vendors. It does not infer server behavior or edit OpenWF.
/// </summary>
public static class CurrentDatafileSnapshot
{
    static readonly (string Name, string InternalPath)[] Families =
    [
        ("Syndicates", "Lotus/Syndicates"),
        ("DojoRecipeManifest", "Lotus/Types/Game/Store/DojoRecipeManifest"),
        ("VendorManifests", "Lotus/Types/Game/VendorManifests")
    ];

    public static string Run(string gameDirectory, string outputRoot, string? exporterPath, string? bin2JsonPath)
    {
        string? cacheDirectory = Cache.NormalizeToCacheWindows(Path.GetFullPath(gameDirectory));
        if (cacheDirectory == null) throw new DirectoryNotFoundException($"No Cache.Windows found below {gameDirectory}.");
        if (string.IsNullOrWhiteSpace(exporterPath) || !File.Exists(exporterPath))
            throw new FileNotFoundException("Warframe-Exporter is required for current datafile extraction.", exporterPath);
        if (string.IsNullOrWhiteSpace(bin2JsonPath) || !File.Exists(bin2JsonPath))
            throw new FileNotFoundException("bin2json is required for current datafile normalization.", bin2JsonPath);

        string tocPath = Path.Combine(cacheDirectory, "H.Misc.toc");
        string tocHash = UpdaterFiles.Sha256File(tocPath);
        string runDirectory = Path.Combine(
            Path.GetFullPath(outputRoot), $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{tocHash[..16]}");
        string rawDirectory = Path.Combine(runDirectory, "raw");
        string jsonDirectory = Path.Combine(runDirectory, "json");
        Directory.CreateDirectory(rawDirectory);
        Directory.CreateDirectory(jsonDirectory);

        // Some versions of Warframe-Exporter silently omit deeply nested outputs when its output
        // path approaches the legacy Windows MAX_PATH boundary. Extract and normalize in a short
        // temporary path, then copy every hash-pinned artifact into the versioned run directory.
        using var temporary = new TemporaryDirectory();
        string workingRawDirectory = Path.Combine(temporary.Path, "raw");
        string workingJsonDirectory = Path.Combine(temporary.Path, "json");
        Directory.CreateDirectory(workingRawDirectory);
        Directory.CreateDirectory(workingJsonDirectory);

        foreach (var family in Families)
        {
            Console.WriteLine($"[datafiles] extracting {family.InternalPath}");
            RunProcess(exporterPath,
                ["--write-raw", "--game", "Warframe", "--package", "Misc", "--internal-path", family.InternalPath,
                    "--cache-dir", cacheDirectory, "--output-path", workingRawDirectory],
                $"Warframe datafile extraction for {family.Name}");
        }

        var manifest = new CurrentDatafileManifest
        {
            GeneratedAtUtc = DateTime.UtcNow,
            CacheDirectory = cacheDirectory,
            MiscTocSha256 = tocHash,
            ExporterSha256 = UpdaterFiles.Sha256File(exporterPath),
            Bin2JsonSha256 = UpdaterFiles.Sha256File(bin2JsonPath),
            RequestedFamilies = Families.Select(x => x.InternalPath).ToList()
        };

        string debugRoot = Path.Combine(workingRawDirectory, "Debug");
        if (!Directory.Exists(debugRoot))
            throw new InvalidDataException("Warframe-Exporter completed without producing a Debug datafile tree.");
        string[] rawFiles = Directory.EnumerateFiles(debugRoot, "*_H", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).ToArray();
        if (rawFiles.Length == 0)
            throw new InvalidDataException("Warframe-Exporter produced no _H datafiles for the requested families.");

        foreach (string rawPath in rawFiles)
        {
            string debugRelative = Path.GetRelativePath(debugRoot, rawPath);
            string internalPath = "/" + debugRelative.Replace(Path.DirectorySeparatorChar, '/')[..^2];
            string family = Families
                .FirstOrDefault(x => internalPath.StartsWith("/" + x.InternalPath, StringComparison.Ordinal)).Name
                ?? throw new InvalidDataException($"Extracted datafile is outside the requested families: {internalPath}");
            string jsonRelative = debugRelative[..^2] + ".json";
            string persistedRawPath = Path.Combine(rawDirectory, "Debug", debugRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(persistedRawPath)!);
            File.Copy(rawPath, persistedRawPath, overwrite: true);
            string workingJsonPath = Path.Combine(workingJsonDirectory, jsonRelative);
            string persistedJsonPath = Path.Combine(jsonDirectory, jsonRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(workingJsonPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(persistedJsonPath)!);
            if (internalPath == "/Lotus/Syndicates/Kahl/KahlChallengeRewards")
            {
                // The upstream generator explicitly excludes this one datafile because the pinned
                // bin2json build crashes on its format. Preserve and hash it as raw-only evidence;
                // every other requested _H file must still convert successfully.
                manifest.Records.Add(new CurrentDatafileRecord
                {
                    Family = family,
                    InternalPath = internalPath,
                    RawRelativePath = Path.GetRelativePath(runDirectory, persistedRawPath).Replace(Path.DirectorySeparatorChar, '/'),
                    RawBytes = new FileInfo(persistedRawPath).Length,
                    RawSha256 = UpdaterFiles.Sha256File(persistedRawPath),
                    JsonRootKind = "RawOnly",
                    NormalizationStatus = "RawOnlyKnownBin2JsonIncompatibility"
                });
                continue;
            }
            RunProcess(bin2JsonPath, [rawPath, workingJsonPath], $"bin2json conversion for {internalPath}");
            if (!File.Exists(workingJsonPath) || new FileInfo(workingJsonPath).Length == 0)
                throw new InvalidDataException($"bin2json produced no JSON for {internalPath}.");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(workingJsonPath));
            File.Copy(workingJsonPath, persistedJsonPath, overwrite: true);
            manifest.Records.Add(new CurrentDatafileRecord
            {
                Family = family,
                InternalPath = internalPath,
                RawRelativePath = Path.GetRelativePath(runDirectory, persistedRawPath).Replace(Path.DirectorySeparatorChar, '/'),
                RawBytes = new FileInfo(persistedRawPath).Length,
                RawSha256 = UpdaterFiles.Sha256File(persistedRawPath),
                JsonRelativePath = Path.GetRelativePath(runDirectory, persistedJsonPath).Replace(Path.DirectorySeparatorChar, '/'),
                JsonBytes = new FileInfo(persistedJsonPath).Length,
                JsonSha256 = UpdaterFiles.Sha256File(persistedJsonPath),
                JsonRootKind = document.RootElement.ValueKind.ToString()
            });
        }

        UpdaterFiles.WriteJson(Path.Combine(runDirectory, "datafile-manifest.json"), manifest);
        foreach (var group in manifest.Records.GroupBy(x => x.Family).OrderBy(x => x.Key, StringComparer.Ordinal))
            Console.WriteLine($"[datafiles] {group.Key}={group.Count()}");
        Console.WriteLine($"[datafiles] records={manifest.Records.Count}; errors=0");
        Console.WriteLine($"[datafiles] snapshot={runDirectory}");
        return runDirectory;
    }

    static void RunProcess(string executable, IReadOnlyList<string> arguments, string operation)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {operation}.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdout, stderr);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{operation} failed with exit code {process.ExitCode}: {stderr.Result}{stdout.Result}");
    }

    sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "owf-data-" + Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
