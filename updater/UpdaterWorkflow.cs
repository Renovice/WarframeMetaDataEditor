using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MetadataPatchEditor.Core;

namespace OpenWFMetadataUpdater;

public sealed class ScanOptions
{
    public required string GameDirectory { get; init; }
    public required string ServerDirectory { get; init; }
    public required string RunsDirectory { get; init; }
    public string? BuildLabel { get; init; }
    public string? OodlePath { get; init; }
    public string? PublicExportPath { get; init; }
    public string? ReferenceGameDirectory { get; init; }
    public string? VendorExporterPath { get; init; }
    public string? VendorBin2JsonPath { get; init; }
}

public static class UpdaterWorkflow
{
    public const string RelativeTargetPath = "static/generated/openwf-metadata/challenge-metadata.json";
    public const string CandidateFileName = "candidate.challenge-metadata.json";

    public static RunManifest Scan(ScanOptions options)
    {
        string? cacheDir = Cache.NormalizeToCacheWindows(Path.GetFullPath(options.GameDirectory));
        if (cacheDir == null) throw new DirectoryNotFoundException($"No Cache.Windows found below {options.GameDirectory}.");
        string serverDir = Path.GetFullPath(options.ServerDirectory);
        if (!File.Exists(Path.Combine(serverDir, "package.json")))
            throw new DirectoryNotFoundException($"OpenWF package.json not found below {serverDir}.");

        if (!string.IsNullOrWhiteSpace(options.OodlePath)) Oodle.DllPathOverride = Path.GetFullPath(options.OodlePath);

        Console.WriteLine($"[scan] extracting /Packages.bin from {cacheDir}");
        byte[] packages = Cache.ExtractPackagesBin(cacheDir);
        string packagesHash = UpdaterFiles.Sha256Bytes(packages);
        string runName = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{packagesHash[..16]}";
        string runDir = Path.Combine(Path.GetFullPath(options.RunsDirectory), runName);
        Directory.CreateDirectory(runDir);
        Console.WriteLine($"[scan] decoding {packages.Length:N0} bytes (sha256 {packagesHash[..16]}...)");
        var decoded = PackagesBinDecoder.DecodeBytes(packages);
        Console.WriteLine($"[scan] decoded {decoded.Types.Count:N0} types; aligned={decoded.Aligned}");

        PackagesBinDecoder.DecodeResult? baselineDecoded = null;
        if (!string.IsNullOrWhiteSpace(options.ReferenceGameDirectory))
        {
            string? baselineCache = Cache.NormalizeToCacheWindows(Path.GetFullPath(options.ReferenceGameDirectory));
            if (baselineCache == null)
                throw new DirectoryNotFoundException($"No reference Cache.Windows found below {options.ReferenceGameDirectory}.");
            Console.WriteLine($"[scan] extracting reference /Packages.bin from {baselineCache}");
            baselineDecoded = PackagesBinDecoder.DecodeBytes(Cache.ExtractPackagesBin(baselineCache));
            if (!baselineDecoded.Aligned) throw new InvalidDataException("Reference Packages.bin did not decode to an aligned frame boundary.");
        }
        var updateSignals = UpdateSignalDetector.Detect(decoded, baselineDecoded);

        var (primary, silent) = ChallengeModule.Extract(decoded);
        var generated = ChallengeModule.GenerateServerChallenges(silent);
        string? publicExportPath = ResolvePublicExportPath(options.PublicExportPath, serverDir);
        var coverage = ChallengeModule.ReadPublicExportCoverage(publicExportPath, primary, silent);
        var validation = ChallengeModule.Validate(
            decoded, primary, silent, generated, coverage, publicExportWasRequested: publicExportPath != null);
        var generatedNightwave = NightwaveModule.Extract(decoded, validation.Issues);
        SortedDictionary<string, GeneratedVendor> generatedVendors;
        var generatedSyndicateFavours = new SortedDictionary<string, GeneratedSyndicateFavours>(StringComparer.Ordinal);
        var generatedDojoRecipes = new GeneratedDojoRecipes();
        if (!string.IsNullOrWhiteSpace(options.VendorExporterPath) && File.Exists(options.VendorExporterPath) &&
            !string.IsNullOrWhiteSpace(options.VendorBin2JsonPath) && File.Exists(options.VendorBin2JsonPath))
        {
            string datafileSnapshot = CurrentDatafileSnapshot.Run(
                options.GameDirectory,
                Path.Combine(runDir, "current-datafiles"),
                options.VendorExporterPath,
                options.VendorBin2JsonPath);
            generatedVendors = VendorManifestModule.ExtractCurrentVendors(
                datafileSnapshot, generatedNightwave.Values, validation.Issues);
            generatedSyndicateFavours = CurrentSyndicateModule.Extract(
                decoded, datafileSnapshot, validation.Issues);
            generatedDojoRecipes = CurrentDojoRecipeModule.Extract(
                decoded, datafileSnapshot, publicExportPath, validation.Issues);
        }
        else
        {
            generatedVendors = VendorManifestModule.ExtractNightwaveVendors(
                cacheDir, generatedNightwave.Values, runDir,
                options.VendorExporterPath, options.VendorBin2JsonPath, validation.Issues);
        }
        var generatedCosmetics = CosmeticModule.Extract(decoded, validation.Issues);
        var generatedBundles = NightwaveModule.ExtractReferencedBundles(decoded, generatedNightwave.Values, validation.Issues);
        CosmeticBundleModule.AddCosmeticBundles(decoded, generatedCosmetics, generatedBundles, validation.Issues);
        var itemRegistry = ItemRegistryModule.Extract(
            decoded, publicExportPath, generatedCosmetics, validation.Issues);
        validation.ErrorCount = validation.Issues.Count(x => x.Severity == ValidationSeverity.Error);
        validation.WarningCount = validation.Issues.Count(x => x.Severity == ValidationSeverity.Warning);
        validation.InfoCount = validation.Issues.Count(x => x.Severity == ValidationSeverity.Info);

        var cacheFile = new FileInfo(Path.Combine(cacheDir, "H.Misc.cache"));
        var tocFile = Path.Combine(cacheDir, "H.Misc.toc");
        var source = new MetadataSource
        {
            SnapshotId = packagesHash[..16],
            BuildLabel = options.BuildLabel,
            ExecutableFileVersion = ReadExecutableVersion(Path.GetDirectoryName(cacheDir)!),
            PackagesSha256 = packagesHash,
            TocSha256 = UpdaterFiles.Sha256File(tocFile),
            CacheLength = cacheFile.Length,
            CacheLastWriteUtc = cacheFile.LastWriteTimeUtc,
            GeneratedAtUtc = DateTime.UtcNow
        };
        var snapshot = new ChallengeMetadataSnapshot
        {
            Source = source,
            PrimaryManifest = primary,
            SilentManifest = silent,
            GeneratedChallenges = generated,
            GeneratedNightwaveSyndicates = generatedNightwave,
            GeneratedVendors = generatedVendors,
            GeneratedSyndicateFavours = generatedSyndicateFavours,
            GeneratedDojoRecipes = generatedDojoRecipes,
            GeneratedBundles = generatedBundles,
            GeneratedCosmetics = generatedCosmetics,
            GeneratedItems = itemRegistry.Items,
            ItemRegistrySummary = itemRegistry.Audit.Summary,
            PublicExportCoverage = coverage,
            Validation = validation
        };

        string activePath = Path.Combine(serverDir, RelativeTargetPath.Replace('/', Path.DirectorySeparatorChar));
        ChallengeMetadataSnapshot? previous = TryReadSnapshot(activePath);
        var changes = Compare(snapshot, previous);
        changes.RequiresReview = validation.ErrorCount != 0 || validation.WarningCount != 0 ||
            HasChanges(changes.GeneratedChallenges) || HasChanges(changes.PrimaryManifest) ||
            HasChanges(changes.SilentManifest) || HasChanges(changes.NightwaveSyndicates) ||
            HasChanges(changes.GeneratedVendors) || HasChanges(changes.GeneratedSyndicateFavours) ||
            HasChanges(changes.GeneratedDojoRecipes) || HasChanges(changes.GeneratedBundles) ||
            HasChanges(changes.GeneratedCosmetics) || HasChanges(changes.GeneratedItems) ||
            HasChanges(changes.ItemRegistrySummary);

        string candidatePath = Path.Combine(runDir, CandidateFileName);
        UpdaterFiles.WriteJson(candidatePath, snapshot);
        UpdaterFiles.WriteJson(Path.Combine(runDir, "change-report.json"), changes);
        UpdaterFiles.WriteJson(Path.Combine(runDir, "validation.json"), validation);
        UpdaterFiles.WriteJson(Path.Combine(runDir, "update-signals.json"), updateSignals);
        UpdaterFiles.WriteJson(Path.Combine(runDir, "item-registry-audit.json"), itemRegistry.Audit);
        File.WriteAllText(Path.Combine(runDir, "CHANGE_REPORT.md"), BuildMarkdown(snapshot, changes), new UTF8Encoding(false));

        var manifest = new RunManifest
        {
            RunDirectory = runDir,
            CandidatePath = candidatePath,
            CandidateSha256 = UpdaterFiles.Sha256File(candidatePath),
            ServerDirectory = serverDir,
            TargetPath = activePath,
            CreatedAtUtc = DateTime.UtcNow
        };
        UpdaterFiles.WriteJson(Path.Combine(runDir, "run-manifest.json"), manifest);

        Console.WriteLine($"[scan] primary={primary.Entries.Count}; silent={silent.Entries.Count}; generated={generated.Count}");
        Console.WriteLine($"[scan] generated nightwave syndicates={generatedNightwave.Count}");
        Console.WriteLine($"[scan] generated vendor manifests={generatedVendors.Count}");
        Console.WriteLine($"[scan] generated syndicate favour sets={generatedSyndicateFavours.Count}; " +
                          $"active rows={generatedSyndicateFavours.Values.Sum(x => x.Favours.Count)}; " +
                          $"source rows={generatedSyndicateFavours.Values.Sum(x => x.SourceFavourCount)}; " +
                          $"rejected rows={generatedSyndicateFavours.Values.Sum(x => x.RejectedFavours.Count)}");
        Console.WriteLine($"[scan] generated current-only dojo recipes=" +
                          $"{generatedDojoRecipes.Research.Count + generatedDojoRecipes.Decos.Count}");
        Console.WriteLine($"[scan] generated cosmetics={generatedCosmetics.Count}");
        Console.WriteLine($"[scan] item registry admitted={itemRegistry.Audit.Summary.AdmittedCount}; " +
                          $"rejected={itemRegistry.Audit.Summary.RejectedCount}; " +
                          $"denominator={itemRegistry.Audit.Summary.StoreWrapperCount}; " +
                          $"no-explicit-server-record={itemRegistry.Audit.Summary.NoExplicitServerRecordCount}");
        Console.WriteLine($"[scan] public export primary={coverage.PrimaryCovered}/{coverage.PrimaryTotal}; silent={coverage.SilentCovered}/{coverage.SilentTotal}");
        Console.WriteLine($"[scan] validation errors={validation.ErrorCount}; warnings={validation.WarningCount}; info={validation.InfoCount}");
        Console.WriteLine($"[scan] update signals current={updateSignals.CurrentSignalCount}; baseline={updateSignals.BaselineSignalCount}; added={updateSignals.Added.Count}; removed={updateSignals.Removed.Count}");
        Console.WriteLine("[scan] review-required signal categories are never auto-applied");
        Console.WriteLine($"[scan] preview only; OpenWF was not changed");
        Console.WriteLine($"[scan] run={runDir}");
        return manifest;
    }

    public static void Apply(string runDirectory, string? serverOverride, bool approveWarnings)
    {
        string runDir = Path.GetFullPath(runDirectory);
        var run = UpdaterFiles.ReadJson<RunManifest>(Path.Combine(runDir, "run-manifest.json"));
        string serverDir = serverOverride == null ? run.ServerDirectory : Path.GetFullPath(serverOverride);
        string target = Path.Combine(serverDir, RelativeTargetPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(Path.Combine(serverDir, "package.json")))
            throw new DirectoryNotFoundException($"OpenWF package.json not found below {serverDir}.");
        if (!File.Exists(run.CandidatePath)) throw new FileNotFoundException("Candidate snapshot is missing.", run.CandidatePath);
        if (!string.Equals(UpdaterFiles.Sha256File(run.CandidatePath), run.CandidateSha256, StringComparison.Ordinal))
            throw new InvalidDataException("Candidate hash differs from run-manifest.json; refusing to apply.");

        var candidate = UpdaterFiles.ReadJson<ChallengeMetadataSnapshot>(run.CandidatePath);
        if (candidate.Validation.ErrorCount != 0)
            throw new InvalidOperationException($"Candidate has {candidate.Validation.ErrorCount} validation errors; refusing to apply.");
        if (candidate.Validation.WarningCount != 0 && !approveWarnings)
            throw new InvalidOperationException(
                $"Candidate has {candidate.Validation.WarningCount} warnings; inspect CHANGE_REPORT.md and pass --approve-warnings explicitly.");

        if (!File.Exists(target))
            throw new InvalidOperationException(
                "The generated target has no integrated baseline; refusing a first activation that could not be rolled back.");

        string backupDir = Path.Combine(runDir, "backup");
        Directory.CreateDirectory(backupDir);
        string backupPath = Path.Combine(backupDir, "challenge-metadata.previous.json");
        File.Copy(target, backupPath, overwrite: false);
        string previousHash = UpdaterFiles.Sha256File(backupPath);

        UpdaterFiles.AtomicCopy(run.CandidatePath, target);
        string appliedHash = UpdaterFiles.Sha256File(target);
        if (!string.Equals(appliedHash, run.CandidateSha256, StringComparison.Ordinal))
            throw new IOException("Post-apply hash does not match the validated candidate.");

        var state = new ApplyState
        {
            TargetPath = target,
            AppliedSha256 = appliedHash,
            PreviousSha256 = previousHash,
            BackupPath = backupPath,
            AppliedAtUtc = DateTime.UtcNow
        };
        UpdaterFiles.WriteJson(Path.Combine(runDir, "apply-state.json"), state);
        Console.WriteLine($"[apply] activated snapshot {candidate.Source.SnapshotId}");
        Console.WriteLine($"[apply] target={target}");
        Console.WriteLine($"[apply] backup={backupPath}");
        Console.WriteLine("[apply] OpenWF was not started or restarted");
    }

    public static void Rollback(string runDirectory)
    {
        string runDir = Path.GetFullPath(runDirectory);
        string statePath = Path.Combine(runDir, "apply-state.json");
        var state = UpdaterFiles.ReadJson<ApplyState>(statePath);
        if (state.RolledBackAtUtc != null) throw new InvalidOperationException("This run was already rolled back.");
        if (!File.Exists(state.TargetPath)) throw new FileNotFoundException("Applied target is missing.", state.TargetPath);
        string currentHash = UpdaterFiles.Sha256File(state.TargetPath);
        if (!string.Equals(currentHash, state.AppliedSha256, StringComparison.Ordinal))
            throw new InvalidOperationException("Active snapshot changed after this run; refusing to overwrite a newer activation.");
        if (state.BackupPath == null || !File.Exists(state.BackupPath))
            throw new InvalidOperationException("This was the first activation and has no previous snapshot to restore.");
        if (!string.Equals(UpdaterFiles.Sha256File(state.BackupPath), state.PreviousSha256, StringComparison.Ordinal))
            throw new InvalidDataException("Backup hash mismatch; refusing rollback.");

        UpdaterFiles.AtomicCopy(state.BackupPath, state.TargetPath);
        state.RolledBackAtUtc = DateTime.UtcNow;
        UpdaterFiles.WriteJson(statePath, state);
        Console.WriteLine($"[rollback] restored {state.PreviousSha256}");
        Console.WriteLine($"[rollback] target={state.TargetPath}");
        Console.WriteLine("[rollback] OpenWF was not started or restarted");
    }

    public static ChangeReport Compare(ChallengeMetadataSnapshot current, ChallengeMetadataSnapshot? previous)
    {
        var report = new ChangeReport
        {
            SnapshotId = current.Source.SnapshotId,
            ComparedWithSnapshotId = previous?.Source.SnapshotId
        };
        report.GeneratedChallenges = CompareMaps(
            current.GeneratedChallenges.ToDictionary(x => x.Key, x => JsonSerializer.Serialize(x.Value, UpdaterFiles.Json), StringComparer.Ordinal),
            previous?.GeneratedChallenges.ToDictionary(x => x.Key, x => JsonSerializer.Serialize(x.Value, UpdaterFiles.Json), StringComparer.Ordinal));
        report.PrimaryManifest = CompareMaps(
            current.PrimaryManifest.Entries.ToDictionary(x => x.ChallengePath, x => x.RecordSha256, StringComparer.Ordinal),
            previous?.PrimaryManifest.Entries.ToDictionary(x => x.ChallengePath, x => x.RecordSha256, StringComparer.Ordinal));
        report.SilentManifest = CompareMaps(
            current.SilentManifest.Entries.ToDictionary(x => x.ChallengePath, x => x.RecordSha256, StringComparer.Ordinal),
            previous?.SilentManifest.Entries.ToDictionary(x => x.ChallengePath, x => x.RecordSha256, StringComparer.Ordinal));
        report.NightwaveSyndicates = CompareMaps(
            current.GeneratedNightwaveSyndicates.ToDictionary(
                x => x.Key, x => JsonSerializer.Serialize(x.Value, UpdaterFiles.Json), StringComparer.Ordinal),
            previous?.GeneratedNightwaveSyndicates.ToDictionary(
                x => x.Key, x => JsonSerializer.Serialize(x.Value, UpdaterFiles.Json), StringComparer.Ordinal));
        report.GeneratedVendors = CompareMaps(
            current.GeneratedVendors.ToDictionary(
                x => x.Key, x => JsonSerializer.Serialize(x.Value, UpdaterFiles.Json), StringComparer.Ordinal),
            previous?.GeneratedVendors.ToDictionary(
                x => x.Key, x => JsonSerializer.Serialize(x.Value, UpdaterFiles.Json), StringComparer.Ordinal));
        report.GeneratedSyndicateFavours = CompareMaps(
            current.GeneratedSyndicateFavours.ToDictionary(
                x => x.Key, x => JsonSerializer.Serialize(x.Value, UpdaterFiles.Json), StringComparer.Ordinal),
            previous?.GeneratedSyndicateFavours.ToDictionary(
                x => x.Key, x => JsonSerializer.Serialize(x.Value, UpdaterFiles.Json), StringComparer.Ordinal));
        report.GeneratedDojoRecipes = CompareMaps(
            FlattenDojoRecipes(current.GeneratedDojoRecipes),
            previous == null ? null : FlattenDojoRecipes(previous.GeneratedDojoRecipes));
        report.GeneratedBundles = CompareMaps(
            current.GeneratedBundles.ToDictionary(
                x => x.Key, x => JsonSerializer.Serialize(x.Value, UpdaterFiles.Json), StringComparer.Ordinal),
            previous?.GeneratedBundles.ToDictionary(
                x => x.Key, x => JsonSerializer.Serialize(x.Value, UpdaterFiles.Json), StringComparer.Ordinal));
        report.GeneratedCosmetics = CompareMaps(
            current.GeneratedCosmetics.ToDictionary(
                x => x.Key, x => JsonSerializer.Serialize(x.Value, UpdaterFiles.Json), StringComparer.Ordinal),
            previous?.GeneratedCosmetics.ToDictionary(
                x => x.Key, x => JsonSerializer.Serialize(x.Value, UpdaterFiles.Json), StringComparer.Ordinal));
        report.GeneratedItems = CompareMaps(
            current.GeneratedItems.ToDictionary(
                x => x.Key, x => JsonSerializer.Serialize(x.Value, UpdaterFiles.Json), StringComparer.Ordinal),
            previous?.GeneratedItems.ToDictionary(
                x => x.Key, x => JsonSerializer.Serialize(x.Value, UpdaterFiles.Json), StringComparer.Ordinal));
        report.ItemRegistrySummary = CompareMaps(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["summary"] = JsonSerializer.Serialize(current.ItemRegistrySummary, UpdaterFiles.Json)
            },
            previous == null ? null : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["summary"] = JsonSerializer.Serialize(previous.ItemRegistrySummary, UpdaterFiles.Json)
            });
        return report;
    }

    static Dictionary<string, string> FlattenDojoRecipes(GeneratedDojoRecipes recipes)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["$manifest"] = JsonSerializer.Serialize(new
            {
                recipes.ManifestPath,
                recipes.SourceSha256,
                recipes.CurrentManifestCount
            }, UpdaterFiles.Json)
        };
        foreach (var entry in recipes.Research)
            result["research:" + entry.Key] = JsonSerializer.Serialize(entry.Value, UpdaterFiles.Json);
        foreach (var entry in recipes.Decos)
            result["decos:" + entry.Key] = JsonSerializer.Serialize(entry.Value, UpdaterFiles.Json);
        return result;
    }

    static ChangeSet CompareMaps(Dictionary<string, string> current, Dictionary<string, string>? previous)
    {
        previous ??= new Dictionary<string, string>(StringComparer.Ordinal);
        return new ChangeSet
        {
            Added = current.Keys.Except(previous.Keys, StringComparer.Ordinal).Order().ToList(),
            Removed = previous.Keys.Except(current.Keys, StringComparer.Ordinal).Order().ToList(),
            Changed = current.Keys.Intersect(previous.Keys, StringComparer.Ordinal)
                .Where(key => !string.Equals(current[key], previous[key], StringComparison.Ordinal)).Order().ToList()
        };
    }

    static ChallengeMetadataSnapshot? TryReadSnapshot(string path)
    {
        if (!File.Exists(path)) return null;
        try { return UpdaterFiles.ReadJson<ChallengeMetadataSnapshot>(path); }
        catch (Exception ex)
        {
            Console.WriteLine($"[scan] WARNING: active snapshot is unreadable and will not be used as a diff baseline: {ex.Message}");
            return null;
        }
    }

    static string? ResolvePublicExportPath(string? requested, string serverDir)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            string path = Path.GetFullPath(requested);
            if (!File.Exists(path)) throw new FileNotFoundException("Requested ExportChallenges.json does not exist.", path);
            return path;
        }
        string inferred = Path.Combine(serverDir, "node_modules", "warframe-public-export-plus", "ExportChallenges.json");
        return File.Exists(inferred) ? inferred : null;
    }

    static string? ReadExecutableVersion(string gameDir)
    {
        string exe = Path.Combine(gameDir, "Warframe.x64.exe");
        return File.Exists(exe) ? FileVersionInfo.GetVersionInfo(exe).FileVersion : null;
    }

    static bool HasChanges(ChangeSet changes) => changes.Added.Count != 0 || changes.Removed.Count != 0 || changes.Changed.Count != 0;

    static string BuildMarkdown(ChallengeMetadataSnapshot snapshot, ChangeReport changes)
    {
        var s = snapshot.Source;
        var v = snapshot.Validation;
        var sb = new StringBuilder();
        sb.AppendLine("# OpenWF metadata update preview").AppendLine();
        sb.AppendLine($"- Snapshot: `{s.SnapshotId}`");
        sb.AppendLine($"- Build label: `{s.BuildLabel ?? "not supplied"}`");
        sb.AppendLine($"- Warframe executable version: `{s.ExecutableFileVersion ?? "unavailable"}`");
        sb.AppendLine($"- Packages SHA-256: `{s.PackagesSha256}`");
        sb.AppendLine($"- Validation: **{v.ErrorCount} errors, {v.WarningCount} warnings, {v.InfoCount} info**");
        sb.AppendLine($"- Preview only: scanning did **not** modify or restart OpenWF.").AppendLine();
        sb.AppendLine("## Challenge coverage").AppendLine();
        sb.AppendLine($"- Primary manifest: {snapshot.PrimaryManifest.Entries.Count} entries; public export covers {snapshot.PublicExportCoverage.PrimaryCovered}/{snapshot.PublicExportCoverage.PrimaryTotal}.");
        sb.AppendLine($"- Silent manifest: {snapshot.SilentManifest.Entries.Count} entries; generated OpenWF rewards {snapshot.GeneratedChallenges.Count}; public export covers {snapshot.PublicExportCoverage.SilentCovered}/{snapshot.PublicExportCoverage.SilentTotal}.").AppendLine();
        sb.AppendLine($"- Package/datafile-backed Nightwave supplements: {snapshot.GeneratedNightwaveSyndicates.Count} syndicates and {snapshot.GeneratedVendors.Count} full vendor manifests.");
        sb.AppendLine($"- Current syndicate datafiles: {snapshot.GeneratedSyndicateFavours.Count} favour sets with " +
                      $"{snapshot.GeneratedSyndicateFavours.Values.Sum(x => x.Favours.Count)} active rows, " +
                      $"{snapshot.GeneratedSyndicateFavours.Values.Sum(x => x.SourceFavourCount)} source rows, and " +
                      $"{snapshot.GeneratedSyndicateFavours.Values.Sum(x => x.RejectedFavours.Count)} preserved rejections.");
        sb.AppendLine($"- Current Dojo manifest: {snapshot.GeneratedDojoRecipes.CurrentManifestCount} entries; {snapshot.GeneratedDojoRecipes.Research.Count + snapshot.GeneratedDojoRecipes.Decos.Count} current-only supplements.");
        sb.AppendLine($"- Client-present cosmetic catalog: {snapshot.GeneratedCosmetics.Count} exact localized inventory/store pairs and {snapshot.GeneratedBundles.Count(x => x.Value.IsCosmetic)} all-cosmetic bundles.").AppendLine();
        var ir = snapshot.ItemRegistrySummary;
        sb.AppendLine("## Complete item registry").AppendLine();
        sb.AppendLine($"- Denominator: {ir.StoreWrapperCount} decoded `/Lotus/StoreItems/` wrappers from {ir.DecodedTypeCount} decoded types.");
        sb.AppendLine($"- Partition: {ir.AdmittedCount} admitted + {ir.RejectedCount} rejected = {ir.StoreWrapperCount}.");
        sb.AppendLine($"- Exact mirrored inventory/store pairs before metadata gates: {ir.ExactPairCount}.");
        sb.AppendLine($"- Rejection SHA-256: `{ir.RejectionSha256}`.");
        sb.AppendLine($"- Explicit server records: {ir.AdmittedCount - ir.NoExplicitServerRecordCount}/{ir.AdmittedCount}; no explicit record: {ir.NoExplicitServerRecordCount}, SHA-256 `{ir.NoExplicitServerRecordSha256}`.");
        sb.AppendLine($"- Public Export package: `{ir.PublicExportPackageVersion ?? "unavailable"}`.");
        sb.AppendLine("- Full rejected and no-explicit-record path lists are in `item-registry-audit.json`. The registry does not claim acquisition or gameplay behavior for a path.").AppendLine();
        AppendChanges(sb, "Generated OpenWF challenge rewards", changes.GeneratedChallenges);
        AppendChanges(sb, "Primary manifest", changes.PrimaryManifest);
        AppendChanges(sb, "Silent manifest", changes.SilentManifest);
        AppendChanges(sb, "Nightwave syndicate", changes.NightwaveSyndicates);
        AppendChanges(sb, "Generated vendor manifest", changes.GeneratedVendors);
        AppendChanges(sb, "Generated syndicate favour set", changes.GeneratedSyndicateFavours);
        AppendChanges(sb, "Generated Dojo recipe", changes.GeneratedDojoRecipes);
        AppendChanges(sb, "Generated bundle", changes.GeneratedBundles);
        AppendChanges(sb, "Client cosmetic catalog", changes.GeneratedCosmetics);
        AppendChanges(sb, "Complete item registry", changes.GeneratedItems, 100);
        AppendChanges(sb, "Item registry summary", changes.ItemRegistrySummary);
        sb.AppendLine("## Validation findings").AppendLine();
        foreach (var issue in v.Issues)
            sb.AppendLine($"- **{issue.Severity} `{issue.Code}`**: {issue.Message}{(issue.Path == null ? "" : $" (`{issue.Path}`)")}");
        sb.AppendLine().AppendLine("## Activation").AppendLine();
        sb.AppendLine("Activation is a separate explicit command. It verifies the candidate hash and validation result, backs up the active snapshot, then performs an atomic replacement. It never starts or restarts OpenWF.");
        return sb.ToString();
    }

    static void AppendChanges(StringBuilder sb, string title, ChangeSet changes, int? listLimit = null)
    {
        sb.AppendLine($"## {title} changes").AppendLine();
        AppendList(sb, "Added", changes.Added, listLimit);
        AppendList(sb, "Removed", changes.Removed, listLimit);
        AppendList(sb, "Changed", changes.Changed, listLimit);
        sb.AppendLine();
    }

    static void AppendList(StringBuilder sb, string label, List<string> values, int? listLimit = null)
    {
        sb.AppendLine($"- {label}: {values.Count}");
        foreach (var value in values.Take(listLimit ?? values.Count)) sb.AppendLine($"  - `{value}`");
        if (listLimit != null && values.Count > listLimit)
            sb.AppendLine($"  - ... {values.Count - listLimit.Value} more; see `change-report.json` for the complete list.");
    }
}
