using System.Diagnostics;
using System.Text;

namespace OpenWFMetadataUpdater;

public enum ClientFileOwnership
{
    OfficialBuild,
    RenoviceOwned,
    VersionCoupledGenerated,
    Diagnostic,
    Unknown
}

public sealed class ClientFileEvidence
{
    public string RelativePath { get; set; } = "";
    public ClientFileOwnership Ownership { get; set; }
    public long Length { get; set; }
    public DateTime LastWriteUtc { get; set; }
    public string? Sha256 { get; set; }
}

public sealed class ClientDifference
{
    public string RelativePath { get; set; } = "";
    public ClientFileOwnership Ownership { get; set; }
    public string Kind { get; set; } = "";
    public string? ActiveSha256 { get; set; }
    public string? ReferenceSha256 { get; set; }
}

public sealed class HypothesisResult
{
    public string Hypothesis { get; set; } = "";
    public bool Result { get; set; }
    public string Evidence { get; set; } = "";
}

public sealed class ClientAuditManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string GameDirectory { get; set; } = "";
    public string? ReferenceDirectory { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public string ExecutableVersion { get; set; } = "";
    public string ExecutableSha256 { get; set; } = "";
    public string PackagesSha256 { get; set; } = "";
    public List<ClientFileEvidence> Files { get; set; } = [];
    public List<ClientDifference> Differences { get; set; } = [];
    public List<HypothesisResult> Hypotheses { get; set; } = [];
}

public static class ClientUpdateAudit
{
    static readonly string[] RenoviceRoots =
    [
        "OpenWF", "offline", "LotusLib"
    ];

    static readonly string[] OfficialRoots = ["Tools"];

    static readonly HashSet<string> RenoviceRootFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "wtsapi32.dll", "dwmapi.dll", "version.dll", "Launch with OpenWF.bat"
    };

    static readonly HashSet<string> HashedCacheFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "H.Misc.cache", "F.VideoTextureStreaming.cache"
    };

    public static string Run(string gameDirectory, string? referenceDirectory, string outputRoot)
    {
        string game = RequireGame(gameDirectory);
        string? reference = referenceDirectory == null ? null : RequireGame(referenceDirectory);
        string exe = Path.Combine(game, "Warframe.x64.exe");
        string cache = Path.Combine(game, "Cache.Windows");

        byte[] packages = MetadataPatchEditor.Core.Cache.ExtractPackagesBin(cache);
        var manifest = new ClientAuditManifest
        {
            GameDirectory = game,
            ReferenceDirectory = reference,
            GeneratedAtUtc = DateTime.UtcNow,
            ExecutableVersion = FileVersionInfo.GetVersionInfo(exe).ProductVersion ?? "unknown",
            ExecutableSha256 = UpdaterFiles.Sha256File(exe),
            PackagesSha256 = UpdaterFiles.Sha256Bytes(packages),
            Files = Inventory(game)
        };

        if (reference != null)
            manifest.Differences = Compare(manifest.Files, Inventory(reference));

        AddHypotheses(manifest);
        string runDirectory = Path.Combine(
            Path.GetFullPath(outputRoot),
            $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{manifest.ExecutableVersion.Replace('.', '_')}");
        Directory.CreateDirectory(runDirectory);
        UpdaterFiles.WriteJson(Path.Combine(runDirectory, "client-audit.json"), manifest);
        File.WriteAllText(
            Path.Combine(runDirectory, "CLIENT_UPDATE_REPORT.md"),
            BuildMarkdown(manifest),
            new UTF8Encoding(false));

        Console.WriteLine($"[client-audit] version={manifest.ExecutableVersion} exe={manifest.ExecutableSha256[..16]} packages={manifest.PackagesSha256[..16]}");
        Console.WriteLine($"[client-audit] files={manifest.Files.Count} differences={manifest.Differences.Count}");
        foreach (var hypothesis in manifest.Hypotheses)
            Console.WriteLine($"[client-audit] {(hypothesis.Result ? "TRUE" : "FALSE")} {hypothesis.Hypothesis}: {hypothesis.Evidence}");
        Console.WriteLine("[client-audit] read-only; no client file was changed");
        Console.WriteLine($"[client-audit] run={runDirectory}");
        return runDirectory;
    }

    internal static ClientFileOwnership Classify(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        if (normalized.StartsWith("OpenWF/Content/", StringComparison.OrdinalIgnoreCase))
            return ClientFileOwnership.VersionCoupledGenerated;
        if (normalized.Contains(".bak", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/backup", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/logs/", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/diagnostics/", StringComparison.OrdinalIgnoreCase))
            return ClientFileOwnership.Diagnostic;
        if (RenoviceRootFiles.Contains(normalized)) return ClientFileOwnership.RenoviceOwned;
        if (RenoviceRoots.Any(root => normalized.Equals(root, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)))
            return ClientFileOwnership.RenoviceOwned;
        if (OfficialRoots.Any(root => normalized.Equals(root, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)))
            return ClientFileOwnership.OfficialBuild;
        if (normalized.Equals("Warframe.x64.exe", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Cache.Windows/", StringComparison.OrdinalIgnoreCase))
            return ClientFileOwnership.OfficialBuild;
        return ClientFileOwnership.Unknown;
    }

    static string RequireGame(string directory)
    {
        string full = Path.GetFullPath(directory);
        if (!File.Exists(Path.Combine(full, "Warframe.x64.exe")))
            throw new DirectoryNotFoundException($"Warframe.x64.exe not found below {full}.");
        if (!Directory.Exists(Path.Combine(full, "Cache.Windows")))
            throw new DirectoryNotFoundException($"Cache.Windows not found below {full}.");
        return full;
    }

    static List<ClientFileEvidence> Inventory(string game)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(game, "*", SearchOption.TopDirectoryOnly)) paths.Add(file);

        string cache = Path.Combine(game, "Cache.Windows");
        foreach (string file in Directory.EnumerateFiles(cache, "*", SearchOption.TopDirectoryOnly)) paths.Add(file);

        foreach (string rootName in RenoviceRoots)
        {
            string root = Path.Combine(game, rootName);
            if (!Directory.Exists(root)) continue;
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) paths.Add(file);
        }

        var result = new List<ClientFileEvidence>(paths.Count);
        foreach (string path in paths.Order(StringComparer.OrdinalIgnoreCase))
        {
            var file = new FileInfo(path);
            string relative = Path.GetRelativePath(game, path).Replace('\\', '/');
            var ownership = Classify(relative);
            bool hash = ownership != ClientFileOwnership.OfficialBuild
                || relative.Equals("Warframe.x64.exe", StringComparison.OrdinalIgnoreCase)
                || relative.EndsWith(".toc", StringComparison.OrdinalIgnoreCase)
                || HashedCacheFiles.Contains(Path.GetFileName(relative));
            result.Add(new ClientFileEvidence
            {
                RelativePath = relative,
                Ownership = ownership,
                Length = file.Length,
                LastWriteUtc = file.LastWriteTimeUtc,
                Sha256 = hash ? UpdaterFiles.Sha256File(path) : null
            });
        }
        return result;
    }

    static List<ClientDifference> Compare(List<ClientFileEvidence> active, List<ClientFileEvidence> reference)
    {
        var left = active.ToDictionary(x => x.RelativePath, StringComparer.OrdinalIgnoreCase);
        var right = reference.ToDictionary(x => x.RelativePath, StringComparer.OrdinalIgnoreCase);
        var result = new List<ClientDifference>();
        foreach (string path in left.Keys.Union(right.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            bool haveLeft = left.TryGetValue(path, out var a);
            bool haveRight = right.TryGetValue(path, out var b);
            string? kind = !haveLeft ? "MISSING_FROM_ACTIVE"
                : !haveRight ? "NEW_IN_ACTIVE"
                : a!.Length != b!.Length ? "SIZE_CHANGED"
                : a.Sha256 != null && b.Sha256 != null && !string.Equals(a.Sha256, b.Sha256, StringComparison.OrdinalIgnoreCase) ? "HASH_CHANGED"
                : null;
            if (kind == null) continue;
            result.Add(new ClientDifference
            {
                RelativePath = path,
                Ownership = haveLeft ? a!.Ownership : b!.Ownership,
                Kind = kind,
                ActiveSha256 = a?.Sha256,
                ReferenceSha256 = b?.Sha256
            });
        }
        return result;
    }

    static void AddHypotheses(ClientAuditManifest manifest)
    {
        bool proxyPresent = manifest.Files.Any(x => x.RelativePath.Equals("wtsapi32.dll", StringComparison.OrdinalIgnoreCase));
        manifest.Hypotheses.Add(new HypothesisResult
        {
            Hypothesis = "The RENOVICE proxy DLL is present in the active client root",
            Result = proxyPresent,
            Evidence = proxyPresent ? "wtsapi32.dll is inventoried and hashed." : "wtsapi32.dll is absent. The custom bootstrapper cannot load."
        });

        bool previewsPresent = manifest.Files.Any(x => x.RelativePath.Equals("Cache.Windows/F.VideoTextureStreaming.cache", StringComparison.OrdinalIgnoreCase) && x.Length > 0);
        manifest.Hypotheses.Add(new HypothesisResult
        {
            Hypothesis = "Downloaded streamed ability-preview media is present",
            Result = previewsPresent,
            Evidence = previewsPresent ? "F.VideoTextureStreaming.cache exists and is non-empty." : "The streamed video cache is absent or empty."
        });

        var unmanaged = manifest.Files.SingleOrDefault(x => x.RelativePath.Equals("OpenWF/Content/0/UNMANAGED", StringComparison.OrdinalIgnoreCase));
        var generatedManifest = manifest.Files.SingleOrDefault(x => x.RelativePath.StartsWith("OpenWF/Content/0/H.Cache.bin!", StringComparison.OrdinalIgnoreCase)
            && !x.RelativePath.Contains(".bak", StringComparison.OrdinalIgnoreCase));
        var officialCache = manifest.Files.SingleOrDefault(x => x.RelativePath.Equals("Cache.Windows/H.Misc.cache", StringComparison.OrdinalIgnoreCase));
        bool staleUnmanaged = unmanaged != null && (generatedManifest == null || officialCache == null || generatedManifest.LastWriteUtc < officialCache.LastWriteUtc);
        manifest.Hypotheses.Add(new HypothesisResult
        {
            Hypothesis = "No stale unmanaged OpenWF cache-manifest risk is active",
            Result = !staleUnmanaged,
            Evidence = staleUnmanaged
                ? "The unmanaged marker exists but its active H.Cache manifest is missing or older than H.Misc.cache; regenerate it after the official update."
                : unmanaged == null
                    ? "No unmanaged marker is currently active. First launch may generate a current passthrough manifest."
                    : "The unmanaged manifest is newer than the current H.Misc.cache."
        });

        bool customLoss = manifest.Differences.Any(x => x.Ownership == ClientFileOwnership.RenoviceOwned && x.Kind == "MISSING_FROM_ACTIVE");
        manifest.Hypotheses.Add(new HypothesisResult
        {
            Hypothesis = "The active update did not lose a RENOVICE-owned file present in the reference client",
            Result = !customLoss,
            Evidence = customLoss
                ? "At least one reference RENOVICE-owned file is missing; inspect the difference table."
                : "No reference RENOVICE-owned file is classified as missing from the active client."
        });
    }

    static string BuildMarkdown(ClientAuditManifest manifest)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# RENOVICE client update audit").AppendLine();
        sb.AppendLine($"- Active client: `{manifest.GameDirectory}`");
        sb.AppendLine($"- Reference client: `{manifest.ReferenceDirectory ?? "not supplied"}`");
        sb.AppendLine($"- Executable version: `{manifest.ExecutableVersion}`");
        sb.AppendLine($"- Executable SHA-256: `{manifest.ExecutableSha256}`");
        sb.AppendLine($"- Packages SHA-256: `{manifest.PackagesSha256}`");
        sb.AppendLine("- Audit mode: **read-only**").AppendLine();
        sb.AppendLine("## Hypotheses").AppendLine();
        foreach (var hypothesis in manifest.Hypotheses)
            sb.AppendLine($"- **{(hypothesis.Result ? "TRUE" : "FALSE")}** — {hypothesis.Hypothesis}: {hypothesis.Evidence}");
        sb.AppendLine().AppendLine("## Difference summary").AppendLine();
        foreach (var group in manifest.Differences.GroupBy(x => new { x.Ownership, x.Kind }).OrderBy(x => x.Key.Ownership).ThenBy(x => x.Key.Kind))
            sb.AppendLine($"- `{group.Key.Ownership}` / `{group.Key.Kind}`: {group.Count()}");
        sb.AppendLine().AppendLine("## RENOVICE-owned differences").AppendLine();
        foreach (var difference in manifest.Differences.Where(x => x.Ownership == ClientFileOwnership.RenoviceOwned))
            sb.AppendLine($"- `{difference.Kind}` `{difference.RelativePath}`");
        sb.AppendLine().AppendLine("## Ownership policy").AppendLine();
        sb.AppendLine("Official build files may change with the launcher. RENOVICE-owned files are preserved and reviewed. `OpenWF/Content` is preserved for rollback but treated as version-coupled generated state. Logs are diagnostic. Unknown files are never deleted automatically.");
        return sb.ToString();
    }
}
