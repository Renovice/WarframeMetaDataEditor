using MetadataPatchEditor.Core;
using OpenWFMetadataUpdater;

return ProgramMain.Run(args);

static class ProgramMain
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                PrintHelp();
                return 0;
            }

            string command = args[0].ToLowerInvariant();
            var parsed = Arguments.Parse(args[1..]);
            switch (command)
            {
                case "scan":
                    {
                        string projectDir = Locations.FindUpdaterProjectDirectory();
                        string game = parsed.Value("game") ?? Locations.FindDefaultGame()
                            ?? throw new DirectoryNotFoundException("Could not auto-detect Warframe; pass --game <folder>.");
                        string server = parsed.Value("server") ?? Locations.FindDefaultServer()
                            ?? throw new DirectoryNotFoundException("Could not auto-detect OpenWF; pass --server <folder>.");
                        string runs = parsed.Value("runs") ?? Path.Combine(projectDir, "runs");
                        string? oodle = parsed.Value("oodle") ?? Locations.FindBundledOodle(projectDir);
                        string? vendorExporter = parsed.Value("vendor-exporter") ??
                            Locations.FindVendorTool(projectDir, "extras", "Warframe-Exporter-CLI_Windows.exe");
                        string? vendorBin2Json = parsed.Value("vendor-bin2json") ??
                            Locations.FindVendorTool(projectDir, "extras", "datafiles", "bin2json.exe");
                        UpdaterWorkflow.Scan(new ScanOptions
                        {
                            GameDirectory = game,
                            ServerDirectory = server,
                            RunsDirectory = runs,
                            BuildLabel = parsed.Value("build-label"),
                            OodlePath = oodle,
                            PublicExportPath = parsed.Value("public-export"),
                            ReferenceGameDirectory = parsed.Value("reference-game"),
                            VendorExporterPath = vendorExporter,
                            VendorBin2JsonPath = vendorBin2Json
                        });
                        return 0;
                    }
                case "apply":
                    {
                        string run = parsed.Positional.SingleOrDefault()
                            ?? throw new ArgumentException("apply requires the run directory as its positional argument.");
                        UpdaterWorkflow.Apply(run, parsed.Value("server"), parsed.Flag("approve-warnings"));
                        return 0;
                    }
                case "rollback":
                    {
                        string run = parsed.Positional.SingleOrDefault()
                            ?? throw new ArgumentException("rollback requires the applied run directory as its positional argument.");
                        UpdaterWorkflow.Rollback(run);
                        return 0;
                    }
                case "client-audit":
                    {
                        string projectDir = Locations.FindUpdaterProjectDirectory();
                        string game = parsed.Value("game") ?? Locations.FindDefaultGame()
                            ?? throw new DirectoryNotFoundException("Could not auto-detect Warframe; pass --game <folder>.");
                        string output = parsed.Value("output") ?? Path.Combine(projectDir, "runs", "client-audits");
                        ClientUpdateAudit.Run(game, parsed.Value("reference"), output);
                        return 0;
                    }
                case "extract-datafiles":
                    {
                        string projectDir = Locations.FindUpdaterProjectDirectory();
                        string game = parsed.Value("game") ?? Locations.FindDefaultGame()
                            ?? throw new DirectoryNotFoundException("Could not auto-detect Warframe; pass --game <folder>.");
                        string output = parsed.Value("output") ?? Path.Combine(projectDir, "runs", "datafile-snapshots");
                        string? exporter = parsed.Value("vendor-exporter") ??
                            Locations.FindVendorTool(projectDir, "extras", "Warframe-Exporter-CLI_Windows.exe");
                        string? bin2Json = parsed.Value("vendor-bin2json") ??
                            Locations.FindVendorTool(projectDir, "extras", "datafiles", "bin2json.exe");
                        CurrentDatafileSnapshot.Run(game, output, exporter, bin2Json);
                        return 0;
                    }
                case "inspect-type":
                    {
                        string projectDir = Locations.FindUpdaterProjectDirectory();
                        string game = parsed.Value("game") ?? Locations.FindDefaultGame()
                            ?? throw new DirectoryNotFoundException("Could not auto-detect Warframe; pass --game <folder>.");
                        string typePath = parsed.Value("type")
                            ?? throw new ArgumentException("inspect-type requires --type </Lotus/...>.");
                        string? oodle = parsed.Value("oodle") ?? Locations.FindBundledOodle(projectDir);
                        InspectType(game, typePath, oodle);
                        return 0;
                    }
                case "self-test":
                    SelfTests.Run();
                    return 0;
                default:
                    throw new ArgumentException($"Unknown command '{args[0]}'. Use --help.");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[error] {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    static void PrintHelp()
    {
        Console.WriteLine("""
OpenWF Metadata Updater — safe, preview-first game metadata synchronizer

Commands:
  scan [--game <Warframe>] [--server <SpaceNinjaServer>] [--build-label <label>]
       [--oodle <oo2core_9.dll>] [--public-export <ExportChallenges.json>]
       [--reference-game <older-Warframe>] [--runs <output-folder>]
       [--vendor-exporter <Warframe-Exporter-CLI_Windows.exe>]
       [--vendor-bin2json <bin2json.exe>]

      Reads the game and server, creates a versioned candidate + diff + validation report,
      and DOES NOT modify or restart OpenWF.

  apply <run-folder> [--server <SpaceNinjaServer>] [--approve-warnings]

      Explicitly activates a validated candidate after backing up the current snapshot.
      Warnings require a second explicit acknowledgement. Does not restart OpenWF.

  rollback <run-folder>

      Restores that run's verified previous snapshot. Refuses if a newer snapshot is active.

  client-audit --game <Warframe> [--reference <older-Warframe>] [--output <folder>]

      Inventories the updated client, cache provenance, custom overlay, ability-preview media,
      and version-coupled OpenWF content. Produces an ownership-aware read-only report.

  extract-datafiles --game <Warframe> [--output <folder>]
       [--vendor-exporter <Warframe-Exporter-CLI_Windows.exe>]
       [--vendor-bin2json <bin2json.exe>]

      Extracts and hash-pins the current syndicate, dojo-recipe, and complete vendor datafile
      families as raw files plus validated JSON. Does not modify OpenWF.

  inspect-type --game <Warframe> --type </Lotus/...> [--oodle <oo2core_9.dll>]

      Prints the exact Packages.bin inheritance chain and fully composed metadata for one type.
      This is read-only and is intended for resolving update signals without guessing semantics.

  self-test

      Runs deterministic parser, normalization, and diff regression tests without game access.
""");
    }

    static void InspectType(string gameDirectory, string typePath, string? oodlePath)
    {
        string? cacheDir = Cache.NormalizeToCacheWindows(Path.GetFullPath(gameDirectory));
        if (cacheDir == null) throw new DirectoryNotFoundException($"No Cache.Windows found below {gameDirectory}.");
        if (!string.IsNullOrWhiteSpace(oodlePath)) Oodle.DllPathOverride = Path.GetFullPath(oodlePath);

        var decoded = PackagesBinDecoder.DecodeBytes(Cache.ExtractPackagesBin(cacheDir));
        if (!decoded.Types.ContainsKey(typePath)) throw new KeyNotFoundException($"Packages.bin has no type '{typePath}'.");

        Console.WriteLine("[inspect-type] inheritance (self to root):");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (string? current = typePath; !string.IsNullOrEmpty(current) && seen.Add(current);)
        {
            Console.WriteLine(current);
            current = decoded.Types.TryGetValue(current, out var type) ? type.Parent : null;
        }
        Console.WriteLine("[inspect-type] composed metadata:");
        Console.Write(MetadataCatalog.Build(decoded).ComposedText(typePath));
    }
}

sealed class Arguments
{
    readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Positional { get; } = [];

    public static Arguments Parse(string[] args)
    {
        var result = new Arguments();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                result.Positional.Add(arg);
                continue;
            }
            string name = arg[2..];
            if (name.Length == 0) throw new ArgumentException("Empty option name.");
            if (name == "approve-warnings")
            {
                result._options[name] = null;
                continue;
            }
            if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Option --{name} requires a value.");
            result._options[name] = args[i];
        }
        return result;
    }

    public string? Value(string name) => _options.GetValueOrDefault(name);
    public bool Flag(string name) => _options.ContainsKey(name);
}

static class Locations
{
    public static string FindUpdaterProjectDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "OpenWFMetadataUpdater.csproj"))) return dir.FullName;
        string cwd = Directory.GetCurrentDirectory();
        if (File.Exists(Path.Combine(cwd, "OpenWFMetadataUpdater.csproj"))) return cwd;
        throw new DirectoryNotFoundException("Could not locate updater/OpenWFMetadataUpdater.csproj.");
    }

    public static string? FindDefaultGame()
    {
        string? cache = Cache.FindCacheWindows(AppContext.BaseDirectory);
        return cache == null ? null : Directory.GetParent(cache)?.FullName;
    }

    public static string? FindDefaultServer()
    {
        foreach (var root in CandidateDocumentRoots())
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.EnumerateDirectories(root, "OpenWF Server*", SearchOption.TopDirectoryOnly))
            {
                string server = Path.Combine(dir, "SpaceNinjaServer");
                if (File.Exists(Path.Combine(server, "package.json"))) return server;
            }
        }
        return null;
    }

    public static string? FindBundledOodle(string projectDir)
    {
        string candidate = Path.GetFullPath(Path.Combine(projectDir, "..", "editor", "lib", "oo2core_9.dll"));
        return File.Exists(candidate) ? candidate : null;
    }

    public static string? FindVendorTool(string projectDir, params string[] relativeParts)
    {
        string renoviceRoot = Path.GetFullPath(Path.Combine(projectDir, "..", "..", "..", ".."));
        string candidate = relativeParts.Aggregate(
            Path.Combine(renoviceRoot, "vendor", "upstream", "warframe-public-export-plus-gen-senpai"),
            Path.Combine);
        return File.Exists(candidate) ? candidate : null;
    }

    static IEnumerable<string> CandidateDocumentRoots()
    {
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(docs)) yield return docs;
        string? repo = Directory.GetParent(FindUpdaterProjectDirectory())?.Parent?.FullName;
        if (repo != null) yield return repo;
    }
}
