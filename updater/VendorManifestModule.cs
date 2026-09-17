using System.Diagnostics;
using System.Text.Json;

namespace OpenWFMetadataUpdater;

/// <summary>
/// Extracts the full vendor datafile from the live client cache. Packages.bin identifies a
/// Nightwave vendor type, but the actual offer pool lives in the vendor's binary datafile.
/// The conversion below mirrors warframe-public-export-plus' vendor mapping while retaining
/// the raw datafile SHA-256 as provenance.
/// </summary>
public static class VendorManifestModule
{
    public static SortedDictionary<string, GeneratedVendor> ExtractCurrentVendors(
        string datafileSnapshotDirectory,
        IEnumerable<GeneratedNightwaveSyndicate> syndicates,
        List<ValidationIssue> issues)
    {
        string manifestPath = Path.Combine(datafileSnapshotDirectory, "datafile-manifest.json");
        CurrentDatafileManifest manifest = UpdaterFiles.ReadJson<CurrentDatafileManifest>(manifestPath);
        var result = new SortedDictionary<string, GeneratedVendor>(StringComparer.Ordinal);
        var nightwaveByVendor = syndicates
            .Where(x => !string.IsNullOrWhiteSpace(x.VendorManifest))
            .ToDictionary(x => x.VendorManifest, StringComparer.Ordinal);

        foreach (CurrentDatafileRecord record in manifest.Records
                     .Where(x => x.Family == "VendorManifests" && x.NormalizationStatus == "Converted")
                     .OrderBy(x => x.InternalPath, StringComparer.Ordinal))
        {
            string jsonPath = Path.Combine(
                datafileSnapshotDirectory,
                record.JsonRelativePath.Replace('/', Path.DirectorySeparatorChar));
            GeneratedVendor vendor = ParseJson(File.ReadAllText(jsonPath), record.RawSha256);
            int errorsBefore = issues.Count(x => x.Severity == ValidationSeverity.Error);
            ValidateCurrent(record.InternalPath, vendor, issues);
            if (nightwaveByVendor.TryGetValue(record.InternalPath, out GeneratedNightwaveSyndicate? syndicate))
                ValidateNightwave(syndicate, vendor, issues);
            if (issues.Count(x => x.Severity == ValidationSeverity.Error) != errorsBefore) continue;

            result[record.InternalPath] = vendor;
            if (syndicate != null) syndicate.VendorPayloadAvailable = true;
        }

        foreach (GeneratedNightwaveSyndicate syndicate in syndicates.Where(x => !x.VendorPayloadAvailable))
            issues.Add(Issue(ValidationSeverity.Warning, "NIGHTWAVE_VENDOR_PAYLOAD_UNAVAILABLE",
                "The current client vendor offer payload was not generated. OpenWF must use its explicit " +
                "fallback until this warning is resolved.", syndicate.VendorManifest));

        issues.Add(Issue(ValidationSeverity.Info, "CURRENT_VENDOR_DATAFILES_EXTRACTED",
            $"Extracted and validated {result.Count} complete current-client vendor manifests from " +
            $"{manifest.Records.Count(x => x.Family == "VendorManifests")} hash-pinned datafiles."));
        return result;
    }

    public static SortedDictionary<string, GeneratedVendor> ExtractNightwaveVendors(
        string cacheDirectory,
        IEnumerable<GeneratedNightwaveSyndicate> syndicates,
        string runDirectory,
        string? exporterPath,
        string? bin2JsonPath,
        List<ValidationIssue> issues)
    {
        var result = new SortedDictionary<string, GeneratedVendor>(StringComparer.Ordinal);
        foreach (var syndicate in syndicates)
        {
            if (string.IsNullOrWhiteSpace(syndicate.VendorManifest)) continue;
            if (string.IsNullOrWhiteSpace(exporterPath) || !File.Exists(exporterPath) ||
                string.IsNullOrWhiteSpace(bin2JsonPath) || !File.Exists(bin2JsonPath))
            {
                issues.Add(Issue(ValidationSeverity.Warning, "NIGHTWAVE_VENDOR_TOOLS_MISSING",
                    "The current Nightwave offer datafile could not be extracted because the vendor exporter " +
                    "or bin2json converter is missing. The server will retain its explicit fallback.",
                    syndicate.VendorManifest));
                continue;
            }

            string safeName = Path.GetFileName(syndicate.VendorManifest);
            string work = Path.Combine(runDirectory, "vendor-datafiles", safeName);
            Directory.CreateDirectory(work);
            using var extraction = new TemporaryDirectory();
            string rawRoot = extraction.Path;

            // Warframe-Exporter resolves directory prefixes reliably; asking for the exact type path can
            // report success without writing its paired _H datafile. Extract the narrow vendor directory,
            // then select the exact expected file below.
            const string vendorRootMarker = "/VendorManifests";
            int vendorRootEnd = syndicate.VendorManifest.IndexOf(vendorRootMarker, StringComparison.Ordinal);
            string extractionPrefix = vendorRootEnd >= 0
                ? syndicate.VendorManifest[..(vendorRootEnd + vendorRootMarker.Length)]
                : syndicate.VendorManifest[..syndicate.VendorManifest.LastIndexOf('/')];
            RunProcess(exporterPath,
                ["--write-raw", "--game", "Warframe", "--package", "Misc", "--internal-path",
                    extractionPrefix.TrimStart('/'), "--cache-dir", cacheDirectory, "--output-path", rawRoot],
                "Warframe vendor datafile extraction");

            string expected = Path.Combine(rawRoot, "Debug",
                syndicate.VendorManifest.TrimStart('/').Replace('/', Path.DirectorySeparatorChar) + "_H");
            string rawPath = File.Exists(expected)
                ? expected
                : Directory.EnumerateFiles(rawRoot, safeName + "_H", SearchOption.AllDirectories).SingleOrDefault()
                    ?? throw new FileNotFoundException(
                        $"Exporter completed but did not produce {safeName}_H for {syndicate.VendorManifest}.");
            string normalizedPath = Path.Combine(work, safeName + ".json");
            File.Copy(rawPath, Path.Combine(work, safeName + ".bin"), overwrite: true);
            RunProcess(bin2JsonPath, [rawPath, normalizedPath], "vendor bin2json conversion");
            if (!File.Exists(normalizedPath) || new FileInfo(normalizedPath).Length == 0)
                throw new InvalidDataException($"bin2json produced no JSON for {syndicate.VendorManifest}.");

            string rawSha = UpdaterFiles.Sha256File(rawPath);
            GeneratedVendor vendor = ParseJson(File.ReadAllText(normalizedPath), rawSha);
            int errorsBefore = issues.Count(x => x.Severity == ValidationSeverity.Error);
            ValidateNightwave(syndicate, vendor, issues);
            if (issues.Count(x => x.Severity == ValidationSeverity.Error) != errorsBefore) continue;

            result[syndicate.VendorManifest] = vendor;
            syndicate.VendorPayloadAvailable = true;
            int always = vendor.Items.Count(x => x.AlwaysOffered);
            issues.Add(Issue(ValidationSeverity.Info, "NIGHTWAVE_VENDOR_DATAFILE_EXTRACTED",
                $"Extracted {vendor.Items.Count} offers ({always} permanent) from the current client vendor " +
                $"datafile; sha256={rawSha}.", syndicate.VendorManifest));
        }

        foreach (var syndicate in syndicates.Where(x => !x.VendorPayloadAvailable))
            issues.Add(Issue(ValidationSeverity.Warning, "NIGHTWAVE_VENDOR_PAYLOAD_UNAVAILABLE",
                "The current client vendor offer payload was not generated. OpenWF must use its explicit " +
                "fallback until this warning is resolved.", syndicate.VendorManifest));
        return result;
    }

    public static GeneratedVendor ParseJson(string json, string sourceSha256)
    {
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        var vendor = new GeneratedVendor
        {
            IsDynamic = Int(root, "IsDynamic") == 1,
            IsOneBinPerCycle = Int(root, "IsDynamic") == 1 ? Int(root, "ScheduleOneBinPerCycle") == 1 : null,
            RequiredGoalTag = EmptyToNull(String(root, "RequiredGoalTag")),
            RandomSeedType = EmptyToNull(String(root, "RandomSeedType")),
            RandomItemPricesPerBin = ReadRandomItemPrices(root, "RandomCurrencies"),
            NumItems = Range(root, "NumItemsAvailable"),
            NumItemsPerBin = ReadBins(root, "NumItemsPerBin"),
            SourceSha256 = sourceSha256
        };

        if (!root.TryGetProperty("ItemManifest", out var manifest) || manifest.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Vendor datafile has no ItemManifest array.");
        foreach (JsonElement raw in manifest.EnumerateArray()) vendor.Items.Add(ParseOffer(raw));
        return vendor;
    }

    static GeneratedVendorOffer ParseOffer(JsonElement raw)
    {
        bool always = Int(raw, "AlwaysOffered") == 1;
        bool rotated = Int(raw, "RotatedWeekly") == 1;
        int limit = Int(raw, "PurchaseQuantityLimit");
        bool allowMulti = Int(raw, "AllowMultipurchase", 1) == 1;
        string affiliation = String(raw, "Affiliation");
        var offer = new GeneratedVendorOffer
        {
            StoreItem = String(raw, "StoreItem"),
            Quantity = Int(raw, "QuantityMultiplier", 1),
            AlwaysOffered = always,
            Bin = ParseBin(String(raw, "Bin")),
            Probability = always ? null : Double(raw, "AppearanceFrequency"),
            RotatedWeekly = !always && rotated ? true : null,
            Duplicates = Int(raw, "NumDuplicatesToAdd"),
            Credits = RangeOrScalar(raw, "RegularPrice", omitZero: true, Int(raw, "RegularPriceStep", 1)),
            Platinum = RangeOrScalar(raw, "PremiumPrice", omitZero: true),
            ItemPrices = ReadItemPrices(raw, "ItemPrices"),
            NumRandomItemPrices = PositiveOrNull(Int(raw, "NumRandomCurrencies")),
            DurationHours = always || rotated ? null : RangeOrScalar(raw, "DurationAvailable", omitZero: false),
            PurchaseLimit = limit != 0 || !allowMulti ? (limit != 0 ? limit : 1) : null
        };
        int minAffiliationRank = Int(raw, "MinAffiliationRank");
        int standingCost = Int(raw, "StandingCost");
        int reductionPerPositiveRank = Int(raw, "ReductionPerPositiveRank");
        int increasePerNegativeRank = Int(raw, "IncreasePerNegativeRank");
        if (affiliation.Length != 0 &&
            (minAffiliationRank != 0 || standingCost != 0 || reductionPerPositiveRank != 0 ||
             increasePerNegativeRank != 0))
        {
            offer.Syndicate = new GeneratedVendorSyndicatePrice
            {
                Tag = affiliation.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? affiliation,
                MinRank = minAffiliationRank,
                StandingCost = standingCost,
                ReductionPerPositiveRank = reductionPerPositiveRank,
                IncreasePerNegativeRank = increasePerNegativeRank
            };
        }
        if (raw.TryGetProperty("FocusXpCost", out var focus) && focus.ValueKind == JsonValueKind.Object)
        {
            string polarity = String(focus, "Polarity");
            int cost = Int(focus, "Cost");
            if (polarity.Length != 0 && polarity != "AP_UNIVERSAL" && cost != 0)
                offer.FocusXpCost = new GeneratedVendorFocusPrice { Polarity = polarity, Cost = cost };
        }
        return offer;
    }

    public static void ValidateCurrent(
        string vendorPath,
        GeneratedVendor vendor,
        List<ValidationIssue> issues)
    {
        void Error(bool condition, string code, string message, string? path = null)
        {
            if (!condition) issues.Add(Issue(ValidationSeverity.Error, code, message, path ?? vendorPath));
        }

        Error(vendorPath.StartsWith("/Lotus/Types/Game/VendorManifests/", StringComparison.Ordinal),
            "VENDOR_PATH_INVALID", "Current vendor manifest path is outside the vendor hierarchy.");
        Error(vendor.SourceSha256.Length == 64, "VENDOR_SOURCE_HASH_INVALID",
            "Vendor datafile provenance hash is missing or invalid.");
        Error(vendor.Items.Count != 0, "VENDOR_ITEMS_EMPTY", "Vendor contains no offers.");
        if (vendor.NumItems is GeneratedVendorRange range)
            Error(range.MinValue >= 0 && range.MaxValue >= range.MinValue,
                "VENDOR_COUNT_INVALID", "Vendor NumItemsAvailable range is invalid.");

        foreach (GeneratedVendorOffer offer in vendor.Items)
        {
            Error(offer.StoreItem.StartsWith("/Lotus/StoreItems/", StringComparison.Ordinal) ||
                  offer.StoreItem.StartsWith("/Lotus/Types/StoreItems/", StringComparison.Ordinal),
                "VENDOR_STOREITEM_INVALID", "Vendor offer is not a StoreItem path.", offer.StoreItem);
            Error(offer.Quantity > 0, "VENDOR_QUANTITY_INVALID",
                "Vendor offer quantity is not positive.", offer.StoreItem);
            Error(offer.Bin >= 0, "VENDOR_BIN_INVALID", "Vendor offer has an invalid bin.", offer.StoreItem);
            if (!offer.AlwaysOffered)
                Error(offer.Probability is > 0, "VENDOR_PROBABILITY_INVALID",
                    "Rotating vendor offer has no positive appearance probability.", offer.StoreItem);
            bool hasPrice = offer.Credits != null || offer.Platinum != null || offer.ItemPrices is { Count: > 0 } ||
                offer.NumRandomItemPrices is > 0 || offer.Syndicate?.StandingCost > 0 || offer.FocusXpCost?.Cost > 0;
            Error(hasPrice, "VENDOR_PRICE_EMPTY", "Vendor offer has no positive price contract.", offer.StoreItem);
            foreach (GeneratedVendorItemPrice price in offer.ItemPrices ?? [])
                Error(price.ItemCount > 0 && price.ItemType.StartsWith("/Lotus/", StringComparison.Ordinal),
                    "VENDOR_ITEM_PRICE_INVALID", "Vendor item price is invalid.", offer.StoreItem);
            if (offer.Syndicate is { } standing)
            {
                Error(standing.Tag.Length != 0, "VENDOR_SYNDICATE_TAG_EMPTY",
                    "Standing-priced offer has no affiliation tag.", offer.StoreItem);
                Error(standing.StandingCost >= 0, "VENDOR_STANDING_COST_INVALID",
                    "Vendor standing cost is negative.", offer.StoreItem);
            }
            if (offer.NumRandomItemPrices is { } randomCount)
            {
                bool hasBin = vendor.RandomItemPricesPerBin != null &&
                    offer.Bin < vendor.RandomItemPricesPerBin.Count;
                Error(hasBin && vendor.RandomItemPricesPerBin![offer.Bin].Count >= randomCount,
                    "VENDOR_RANDOM_PRICE_UNFILLABLE",
                    $"Offer requests {randomCount} random prices but its bin cannot supply them.", offer.StoreItem);
            }
        }

        if (vendor.NumItemsPerBin is { } quotas)
        {
            for (int bin = 0; bin < quotas.Count; bin++)
            {
                int available = vendor.Items.Count(x => x.Bin == bin);
                Error(quotas[bin] >= 0 && quotas[bin] <= available,
                    "VENDOR_BIN_UNFILLABLE",
                    $"Bin {bin} requests {quotas[bin]} rotating offers but only {available} are available.");
            }
        }
    }

    public static void ValidateNightwave(
        GeneratedNightwaveSyndicate syndicate,
        GeneratedVendor vendor,
        List<ValidationIssue> issues)
    {
        void Error(bool condition, string code, string message, string? path = null)
        {
            if (!condition) issues.Add(Issue(ValidationSeverity.Error, code, message, path ?? syndicate.VendorManifest));
        }

        Error(vendor.IsDynamic, "NIGHTWAVE_VENDOR_NOT_DYNAMIC", "Nightwave vendor datafile is not marked dynamic.");
        Error(vendor.NumItems != null, "NIGHTWAVE_VENDOR_COUNT_EMPTY", "Nightwave vendor has no NumItemsAvailable contract.");
        Error(vendor.NumItemsPerBin is { Count: > 0 }, "NIGHTWAVE_VENDOR_BINS_EMPTY", "Nightwave vendor has no per-bin quotas.");

        foreach (var offer in vendor.Items)
        {
            Error(offer.ItemPrices is { Count: > 0 } || offer.NumRandomItemPrices is > 0,
                "NIGHTWAVE_VENDOR_PRICE_EMPTY",
                "Nightwave vendor offer has no item price.", offer.StoreItem);
            foreach (var price in offer.ItemPrices ?? [])
            {
                Error(price.ItemCount > 0, "NIGHTWAVE_VENDOR_PRICE_COUNT_INVALID",
                    "Nightwave vendor price is not positive.", offer.StoreItem);
                Error(price.ItemType == syndicate.Currency, "NIGHTWAVE_VENDOR_CURRENCY_MISMATCH",
                    $"Nightwave offer price uses {price.ItemType}, expected {syndicate.Currency}.", offer.StoreItem);
            }
            if (offer.NumRandomItemPrices is { } randomCount)
            {
                bool hasBin = vendor.RandomItemPricesPerBin != null &&
                    offer.Bin < vendor.RandomItemPricesPerBin.Count;
                Error(hasBin && vendor.RandomItemPricesPerBin![offer.Bin].Count >= randomCount,
                    "NIGHTWAVE_VENDOR_RANDOM_PRICE_UNFILLABLE",
                    $"Offer requests {randomCount} random prices but its bin cannot supply them.", offer.StoreItem);
            }
        }

        if (vendor.NumItemsPerBin is { } quotas)
        {
            for (int bin = 0; bin < quotas.Count; bin++)
            {
                int available = vendor.Items.Count(x => x.Bin == bin);
                Error(quotas[bin] >= 0 && quotas[bin] <= available,
                    "NIGHTWAVE_VENDOR_BIN_UNFILLABLE",
                    $"Bin {bin} requests {quotas[bin]} rotating offers but only {available} are available.");
            }
        }
    }

    static List<GeneratedVendorItemPrice>? ReadItemPrices(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array) return null;
        var result = value.EnumerateArray().Select(x => new GeneratedVendorItemPrice
        {
            ItemCount = Int(x, "ItemCount"),
            ItemType = String(x, "ItemType")
        }).ToList();
        return result.Count == 0 ? null : result;
    }

    static List<int>? ReadBins(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object) return null;
        var entries = value.EnumerateObject()
            .Select(x => (Bin: ParseBin(x.Name), Count: x.Value.GetInt32()))
            .OrderBy(x => x.Bin).ToList();
        if (entries.Count == 0) return null;
        if (entries.Select((x, i) => x.Bin == i).Any(x => !x))
            throw new InvalidDataException("Vendor NumItemsPerBin keys are not contiguous from BIN_0.");
        return entries.Select(x => x.Count).ToList();
    }

    static List<List<GeneratedVendorRandomItemPrice>>? ReadRandomItemPrices(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object) return null;
        var entries = value.EnumerateObject()
            .Select(x => (Bin: ParseBin(x.Name), Value: x.Value))
            .OrderBy(x => x.Bin).ToList();
        if (entries.Count == 0) return null;
        var result = new List<List<GeneratedVendorRandomItemPrice>>();
        int nextBin = 0;
        foreach (var entry in entries)
        {
            if (entry.Bin < nextBin) throw new InvalidDataException("Vendor RandomCurrencies bins are out of order.");
            while (nextBin < entry.Bin)
            {
                result.Add([]);
                nextBin++;
            }
            if (entry.Value.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"Vendor RandomCurrencies BIN_{entry.Bin} is not an array.");
            var prices = new List<GeneratedVendorRandomItemPrice>();
            foreach (JsonElement raw in entry.Value.EnumerateArray())
            {
                GeneratedVendorRange count = Range(raw, "ValueRange") ??
                    throw new InvalidDataException("Random vendor currency has no ValueRange.");
                prices.Add(new GeneratedVendorRandomItemPrice { Type = String(raw, "ItemType"), Count = count });
            }
            result.Add(prices);
            nextBin++;
        }
        return result;
    }

    static object? RangeOrScalar(JsonElement root, string name, bool omitZero, int step = 1)
    {
        if (!root.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number)
        {
            int scalar = value.GetInt32();
            return omitZero && scalar == 0 ? null : scalar;
        }
        if (value.ValueKind != JsonValueKind.Array) return null;
        int[] values = value.EnumerateArray().Select(x => x.GetInt32()).ToArray();
        if (values.Length != 2) throw new InvalidDataException($"Vendor {name} is not a two-value range.");
        if (omitZero && values[0] == 0 && values[1] == 0) return null;
        if (values[0] == values[1]) return values[0];
        return step == 1
            ? new GeneratedVendorRange { MinValue = values[0], MaxValue = values[1] }
            : new { minValue = values[0], maxValue = values[1], step };
    }

    static GeneratedVendorRange? Range(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array) return null;
        int[] values = value.EnumerateArray().Select(x => x.GetInt32()).ToArray();
        if (values.Length != 2) throw new InvalidDataException($"Vendor {name} is not a two-value range.");
        return new GeneratedVendorRange { MinValue = values[0], MaxValue = values[1] };
    }

    static int ParseBin(string value)
        => value.StartsWith("BIN_", StringComparison.Ordinal) && int.TryParse(value.AsSpan(4), out int bin) ? bin : -1;
    static int Int(JsonElement root, string name, int fallback = 0)
        => root.TryGetProperty(name, out var value) && value.TryGetInt32(out int parsed) ? parsed : fallback;
    static double Double(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.TryGetDouble(out double parsed) ? parsed : 0;
    static string String(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    static int? PositiveOrNull(int value) => value == 0 ? null : value;
    static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

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
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdoutTask, stderrTask);
        string stdout = stdoutTask.Result;
        string stderr = stderrTask.Result;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{operation} failed with exit code {process.ExitCode}: {stderr}{stdout}");
        if (!string.IsNullOrWhiteSpace(stdout)) Console.WriteLine($"[vendor] {stdout.Trim()}");
        if (!string.IsNullOrWhiteSpace(stderr)) Console.WriteLine($"[vendor] {stderr.Trim()}");
    }

    static ValidationIssue Issue(ValidationSeverity severity, string code, string message, string? path = null)
        => new() { Severity = severity, Code = code, Message = message, Path = path };

    sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "openwf-vendor-" + Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
