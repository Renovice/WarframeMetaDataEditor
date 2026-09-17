namespace OpenWFMetadataUpdater;

public static class SelfTests
{
    public static void Run()
    {
        int passed = 0;
        Test("nested fields and lists", () =>
        {
            const string source = """
                Root={
                {
                items={
                Titles/One,
                Titles/Two
                }
                challenge=/Lotus/Types/Challenges/Titles/Test
                nested={
                Value=7
                }
                },
                }
                """;
            var root = DeMetadataSyntax.Fields(source);
            Equal(true, root.ContainsKey("Root"));
            var records = DeMetadataSyntax.List(root["Root"]);
            Equal(1, records.Count);
            var fields = DeMetadataSyntax.Fields(records[0]);
            Equal("/Lotus/Types/Challenges/Titles/Test", DeMetadataSyntax.Scalar(fields, "challenge"));
            var items = DeMetadataSyntax.List(fields["items"]);
            Equal(2, items.Count);
            Equal("Titles/Two", items[1]);
        }, ref passed);

        Test("quoted values and empty blocks", () =>
        {
            var fields = DeMetadataSyntax.Fields("{name=\"hello world\"\nempty={}\nflag=1\n}");
            Equal("hello world", DeMetadataSyntax.Scalar(fields, "name"));
            Equal(0, DeMetadataSyntax.List(fields["empty"]).Count);
            Equal(true, DeMetadataSyntax.Boolean(fields, "flag"));
        }, ref passed);

        Test("path normalization", () =>
        {
            Equal("/Lotus/Types/Challenges/Titles/Test", ChallengeModule.NormalizeChallenge("Titles/Test"));
            Equal("/Lotus/Types/Challenges/Titles/Test", ChallengeModule.NormalizeChallenge("/Lotus/Types/Challenges/Titles/Test"));
            Equal("/Lotus/StoreItems/Types/Items/Titles/Test", ChallengeModule.ToStoreItem("Titles/Test"));
            Equal("/Lotus/StoreItems/Types/Items/Titles/Test", ChallengeModule.ToStoreItem("/Lotus/Types/Items/Titles/Test"));
        }, ref passed);

        Test("diff detects add remove change", () =>
        {
            var oldSnapshot = Snapshot("old", ("A", "X"), ("B", "Y"));
            var newSnapshot = Snapshot("new", ("B", "Z"), ("C", "Q"));
            var diff = UpdaterWorkflow.Compare(newSnapshot, oldSnapshot).GeneratedChallenges;
            Equal("C", diff.Added.Single());
            Equal("A", diff.Removed.Single());
            Equal("B", diff.Changed.Single());
        }, ref passed);

        Test("server reward JSON preserves OpenWF field casing", () =>
        {
            string json = System.Text.Json.JsonSerializer.Serialize(
                new CountedStoreReward { StoreItem = "/Lotus/StoreItems/Test", ItemCount = 2 },
                UpdaterFiles.Json);
            Equal(true, json.Contains("\"StoreItem\"", StringComparison.Ordinal));
            Equal(true, json.Contains("\"ItemCount\"", StringComparison.Ordinal));
        }, ref passed);

        Test("apply and rollback preserve verified baseline", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "openwf-metadata-updater-selftest-" + Guid.NewGuid().ToString("N"));
            try
            {
                string server = Path.Combine(root, "server");
                string run = Path.Combine(root, "run");
                Directory.CreateDirectory(server);
                Directory.CreateDirectory(run);
                File.WriteAllText(Path.Combine(server, "package.json"), "{}");
                string target = Path.Combine(server, UpdaterWorkflow.RelativeTargetPath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                const string baseline = "{\"baseline\":true}";
                File.WriteAllText(target, baseline);

                var candidate = Snapshot("candidate", ("A", "/Lotus/StoreItems/A"));
                string candidatePath = Path.Combine(run, UpdaterWorkflow.CandidateFileName);
                UpdaterFiles.WriteJson(candidatePath, candidate);
                UpdaterFiles.WriteJson(Path.Combine(run, "run-manifest.json"), new RunManifest
                {
                    RunDirectory = run,
                    CandidatePath = candidatePath,
                    CandidateSha256 = UpdaterFiles.Sha256File(candidatePath),
                    ServerDirectory = server,
                    TargetPath = target,
                    CreatedAtUtc = DateTime.UtcNow
                });

                UpdaterWorkflow.Apply(run, null, approveWarnings: false);
                Equal("candidate", UpdaterFiles.ReadJson<ChallengeMetadataSnapshot>(target).Source.SnapshotId);
                UpdaterWorkflow.Rollback(run);
                Equal(baseline, File.ReadAllText(target));
            }
            finally
            {
                if (Directory.Exists(root) && Path.GetFileName(root).StartsWith("openwf-metadata-updater-selftest-", StringComparison.Ordinal))
                    Directory.Delete(root, recursive: true);
            }
        }, ref passed);

        Test("client ownership classification is fail-safe", () =>
        {
            Equal(ClientFileOwnership.RenoviceOwned, ClientUpdateAudit.Classify("OpenWF/CustomScripts/test.lua_B"));
            Equal(ClientFileOwnership.VersionCoupledGenerated, ClientUpdateAudit.Classify("OpenWF/Content/0/UNMANAGED"));
            Equal(ClientFileOwnership.Diagnostic, ClientUpdateAudit.Classify("OpenWF/CustomScripts/test.log"));
            Equal(ClientFileOwnership.Diagnostic, ClientUpdateAudit.Classify("OpenWF/wtsapi32.bak-june-rva.dll"));
            Equal(ClientFileOwnership.OfficialBuild, ClientUpdateAudit.Classify("Tools/Launcher.exe"));
            Equal(ClientFileOwnership.OfficialBuild, ClientUpdateAudit.Classify("Cache.Windows/H.Misc.toc"));
            Equal(ClientFileOwnership.Unknown, ClientUpdateAudit.Classify("mystery.bin"));
        }, ref passed);

        Test("nightwave package contract is parsed without inventing vendor offers", () =>
        {
            const string tagPath = "/Lotus/Syndicates/RadioLegionIntermission16Syndicate";
            const string daily = "/Lotus/Types/Challenges/Seasons/Daily/TestDaily";
            const string weekly = "/Lotus/Types/Challenges/Seasons/Weekly/TestWeekly";
            const string vendor = "/Lotus/Types/Game/VendorManifests/Events/RadioLegionIntermission16VendorManifest";
            const string ownText = """
                SyndicateName=/Lotus/Language/Syndicates/RadioLegionTitle
                Titles={{titleLoc=/Rank/One
                level=1
                minXP=10000
                maxXP=20000
                rewards={{ItemCount=15
                ItemType=/Lotus/Types/Items/MiscItems/TestCreds
                }}
                storeItemReward=""
                }}
                SeasonChallenges={{
                dailyChallenges={/Lotus/Types/Challenges/Seasons/Daily/TestDaily}
                weeklyChallenges={/Lotus/Types/Challenges/Seasons/Weekly/TestWeekly}
                }}
                SeasonCurrency=/Lotus/Types/Items/MiscItems/TestCreds
                SeasonVendorManifest=/Lotus/Types/Game/VendorManifests/Events/RadioLegionIntermission16VendorManifest
                SeasonFeaturedRewards={{ItemCount=1
                ItemType=/Lotus/Types/Items/TestReward
                }}
                SeasonFeaturedStoreItemRewards={/Lotus/Types/StoreItems/Packages/TestBundle}
                """;
            var decoded = new MetadataPatchEditor.Core.PackagesBinDecoder.DecodeResult();
            decoded.Types[tagPath] = new(tagPath, "/Lotus/Syndicates/RadioLegionSyndicate", ownText);
            decoded.Types[daily] = new(daily, "", null);
            decoded.Types[weekly] = new(weekly, "", null);
            decoded.Types[vendor] = new(vendor, "/Lotus/Types/Game/VendorManifests/Events/VendorManifest", null);
            decoded.Types["/Lotus/Types/StoreItems/Packages/TestBundle"] = new(
                "/Lotus/Types/StoreItems/Packages/TestBundle",
                "/Lotus/Types/Game/StoreItemSpecializations/PackageStoreItem",
                """
                LocalizeTag=/Test/BundleName
                LocalizeDescTag=/Test/BundleDescription
                Icon=/Test/Bundle.png
                PackageComponents={{
                TypeName=/Lotus/StoreItems/Types/Items/Emotes/TestEmote
                PurchaseQuantity=1
                }}
                """);
            var issues = new List<ValidationIssue>();
            var values = NightwaveModule.Extract(decoded, issues);
            var bundles = NightwaveModule.ExtractReferencedBundles(decoded, values.Values, issues);
            var value = values["RadioLegionIntermission16Syndicate"];
            Equal("/Lotus/Types/Items/MiscItems/TestCreds", value.Currency);
            Equal(1, value.DailyChallenges.Count);
            Equal(1, value.WeeklyChallenges.Count);
            Equal(1, value.Titles.Count);
            Equal(false, value.VendorPayloadAvailable);
            Equal(1, bundles.Count);
            Equal("/Lotus/StoreItems/Types/Items/Emotes/TestEmote",
                bundles["/Lotus/Types/StoreItems/Packages/TestBundle"].Components.Single().TypeName);
            Equal(false, issues.Any(x => x.Code == "NIGHTWAVE_VENDOR_PAYLOAD_UNAVAILABLE"));
        }, ref passed);

        Test("nightwave vendor datafile preserves native bins prices and limits", () =>
        {
            const string currency = "/Lotus/Types/Items/MiscItems/NoraIntermissionSixteenCreds";
            const string json = """
                {
                  "IsDynamic": 1,
                  "ItemManifest": [
                    {
                      "StoreItem": "/Lotus/StoreItems/Types/Items/Test/Permanent",
                      "QuantityMultiplier": 1, "AlwaysOffered": 1, "Bin": "BIN_0",
                      "AppearanceFrequency": 0.8, "NumDuplicatesToAdd": 0,
                      "NumRandomCurrencies": 0, "DurationAvailable": [168,168],
                      "AllowMultipurchase": 1, "RotatedWeekly": 0, "PurchaseQuantityLimit": 0,
                      "RegularPrice": [0,0], "RegularPriceStep": 100, "PremiumPrice": [0,0],
                      "ItemPrices": [{"ItemCount":25,"ItemType":"/Lotus/Types/Items/MiscItems/NoraIntermissionSixteenCreds"}],
                      "Affiliation": "", "FocusXpCost": {"Polarity":"AP_UNIVERSAL","Cost":0}
                    },
                    {
                      "StoreItem": "/Lotus/StoreItems/Types/Items/Test/Rotating",
                      "QuantityMultiplier": 2, "AlwaysOffered": 0, "Bin": "BIN_0",
                      "AppearanceFrequency": 0.8, "NumDuplicatesToAdd": 1,
                      "NumRandomCurrencies": 0, "DurationAvailable": [168,168],
                      "AllowMultipurchase": 0, "RotatedWeekly": 0, "PurchaseQuantityLimit": 0,
                      "RegularPrice": [0,0], "RegularPriceStep": 100, "PremiumPrice": [0,0],
                      "ItemPrices": [{"ItemCount":35,"ItemType":"/Lotus/Types/Items/MiscItems/NoraIntermissionSixteenCreds"}],
                      "Affiliation": "", "FocusXpCost": {"Polarity":"AP_UNIVERSAL","Cost":0}
                    }
                  ],
                  "NumItemsAvailable": [1,1],
                  "NumItemsPerBin": {"BIN_0":1}
                }
                """;
            var vendor = VendorManifestModule.ParseJson(json, new string('a', 64));
            var syndicate = new GeneratedNightwaveSyndicate
            {
                Currency = currency,
                VendorManifest = "/Lotus/Types/Game/VendorManifests/Events/TestVendorManifest"
            };
            var issues = new List<ValidationIssue>();
            VendorManifestModule.ValidateCurrent(syndicate.VendorManifest, vendor, issues);
            VendorManifestModule.ValidateNightwave(syndicate, vendor, issues);
            Equal(0, issues.Count(x => x.Severity == ValidationSeverity.Error));
            Equal(2, vendor.Items.Count);
            Equal(1, vendor.NumItemsPerBin!.Single());
            Equal(1, vendor.Items.Single(x => !x.AlwaysOffered).PurchaseLimit);
            Equal(35, vendor.Items.Single(x => !x.AlwaysOffered).ItemPrices!.Single().ItemCount);
        }, ref passed);

        Test("static standing vendor preserves standing-only prices", () =>
        {
            const string path = "/Lotus/Types/Game/VendorManifests/TheHex/TestStandingVendorManifest";
            const string json = """
                {
                  "CyclesToSchedule": 20,
                  "ItemManifest": [{
                    "StoreItem": "/Lotus/StoreItems/Types/Items/Test/Decoration",
                    "QuantityMultiplier": 1, "AlwaysOffered": 0, "Bin": "BIN_0",
                    "AppearanceFrequency": 0.8, "NumDuplicatesToAdd": 0,
                    "NumRandomCurrencies": 0, "DurationAvailable": [24,24],
                    "AllowMultipurchase": 1, "RotatedWeekly": 0, "PurchaseQuantityLimit": 0,
                    "RegularPrice": [0,0], "RegularPriceStep": 100, "PremiumPrice": [0,0],
                    "ItemPrices": [], "Affiliation": "/Lotus/Syndicates/Hex/HexSyndicate",
                    "StandingCost": 15000, "MinAffiliationRank": 0,
                    "ReductionPerPositiveRank": 0, "IncreasePerNegativeRank": 0,
                    "FocusXpCost": {"Polarity":"AP_UNIVERSAL","Cost":0}
                  }],
                  "NumItemsAvailable": [1,1]
                }
                """;
            var vendor = VendorManifestModule.ParseJson(json, new string('b', 64));
            var issues = new List<ValidationIssue>();
            VendorManifestModule.ValidateCurrent(path, vendor, issues);
            Equal(0, issues.Count(x => x.Severity == ValidationSeverity.Error));
            Equal(false, vendor.IsDynamic);
            Equal("HexSyndicate", vendor.Items.Single().Syndicate!.Tag);
            Equal(15000, vendor.Items.Single().Syndicate!.StandingCost);
        }, ref passed);

        Test("cosmetic bundles resolve recursively and reject mixed packages", () =>
        {
            const string cosmetic = "/Lotus/Upgrades/Skins/Test/TestSkin";
            const string storeItem = "/Lotus/StoreItems/Upgrades/Skins/Test/TestSkin";
            const string pureBundle = "/Lotus/Types/StoreItems/Packages/PureCosmeticBundle";
            const string nestedBundle = "/Lotus/Types/StoreItems/Packages/NestedCosmeticBundle";
            const string mixedBundle = "/Lotus/Types/StoreItems/Packages/MixedBundle";
            var decoded = new MetadataPatchEditor.Core.PackagesBinDecoder.DecodeResult();
            decoded.Types[cosmetic] = new(cosmetic, "", "LocalizeTag=/Test/SkinName\nProductCategory=WeaponSkins");
            decoded.Types[storeItem] = new(storeItem, "", "LocalizeTag=/Test/SkinName\nProductCategory=WeaponSkins");
            decoded.Types[pureBundle] = new(pureBundle, "", $$"""
                LocalizeTag=/Test/PureBundleName
                PackageComponents={
                {
                TypeName={{storeItem}}
                PurchaseQuantity=1
                }
                }
                """);
            decoded.Types[nestedBundle] = new(nestedBundle, "", $$"""
                LocalizeTag=/Test/NestedBundleName
                PackageComponents={
                {
                TypeName={{pureBundle}}
                PurchaseQuantity=1
                }
                }
                """);
            decoded.Types[mixedBundle] = new(mixedBundle, "", $$"""
                LocalizeTag=/Test/MixedBundleName
                PackageComponents={
                {
                TypeName={{storeItem}}
                PurchaseQuantity=1
                },
                {
                TypeName=/Lotus/StoreItems/Types/Items/MiscItems/TestResource
                PurchaseQuantity=1
                }
                }
                """);

            var issues = new List<ValidationIssue>();
            var cosmetics = CosmeticModule.Extract(decoded, issues);
            var bundles = new SortedDictionary<string, GeneratedBundle>(StringComparer.Ordinal);
            CosmeticBundleModule.AddCosmeticBundles(decoded, cosmetics, bundles, issues);
            Equal(1, cosmetics.Count);
            Equal(2, bundles.Count);
            Equal(true, bundles[pureBundle].IsCosmetic);
            Equal(true, bundles[nestedBundle].IsCosmetic);
            Equal(false, bundles.ContainsKey(mixedBundle));
        }, ref passed);

        Test("item registry accounts for every store wrapper and preserves field ownership", () =>
        {
            const string generatedType = "/Lotus/Upgrades/Skins/Test/GeneratedSkin";
            const string generatedStore = "/Lotus/StoreItems/Upgrades/Skins/Test/GeneratedSkin";
            const string wrapperOwnedType = "/Lotus/Types/Items/Test/WrapperOwned";
            const string wrapperOwnedStore = "/Lotus/StoreItems/Types/Items/Test/WrapperOwned";
            const string missingTypeStore = "/Lotus/StoreItems/Types/Items/Test/MissingType";
            const string missingCategoryType = "/Lotus/Types/Items/Test/MissingCategory";
            const string missingCategoryStore = "/Lotus/StoreItems/Types/Items/Test/MissingCategory";
            const string bareLocalizationType = "/Lotus/Types/Items/Test/BareLocalization";
            const string bareLocalizationStore = "/Lotus/StoreItems/Types/Items/Test/BareLocalization";

            var decoded = new MetadataPatchEditor.Core.PackagesBinDecoder.DecodeResult();
            decoded.Types[generatedType] = new(generatedType, "", "ProductCategory=WeaponSkins\nLocalizeTag=/Test/GeneratedSkin");
            decoded.Types[generatedStore] = new(generatedStore, "", null);
            decoded.Types[wrapperOwnedType] = new(wrapperOwnedType, "", null);
            decoded.Types[wrapperOwnedStore] = new(wrapperOwnedStore, "", "ProductCategory=MiscItems\nLocalizeTag=/Test/WrapperOwned");
            decoded.Types[missingTypeStore] = new(missingTypeStore, "", "ProductCategory=MiscItems\nLocalizeTag=/Test/MissingType");
            decoded.Types[missingCategoryType] = new(missingCategoryType, "", "LocalizeTag=/Test/MissingCategory");
            decoded.Types[missingCategoryStore] = new(missingCategoryStore, "", null);
            decoded.Types[bareLocalizationType] = new(bareLocalizationType, "", "ProductCategory=MiscItems\nLocalizeTag=AbstractName");
            decoded.Types[bareLocalizationStore] = new(bareLocalizationStore, "", null);

            var cosmetics = new SortedDictionary<string, GeneratedCosmetic>(StringComparer.Ordinal)
            {
                [generatedType] = new()
                {
                    TypeName = generatedType,
                    StoreItem = generatedStore,
                    ProductCategory = "WeaponSkins",
                    LocalizeTag = "/Test/GeneratedSkin"
                }
            };
            var issues = new List<ValidationIssue>();
            var result = ItemRegistryModule.Extract(decoded, null, cosmetics, issues);
            Equal(5, result.Audit.Summary.StoreWrapperCount);
            Equal(4, result.Audit.Summary.ExactPairCount);
            Equal(2, result.Audit.Summary.AdmittedCount);
            Equal(3, result.Audit.Summary.RejectedCount);
            Equal(5, result.Audit.Summary.AdmittedCount + result.Audit.Summary.RejectedCount);
            Equal("GeneratedCosmetic", result.Items[generatedType].ServerRecordSource);
            Equal(wrapperOwnedStore, result.Items[wrapperOwnedType].CategorySourcePath);
            Equal(wrapperOwnedStore, result.Items[wrapperOwnedType].LocalizationSourcePath);
            Equal(1, result.Audit.Summary.RejectionCounts["MISSING_MIRRORED_TYPE"]);
            Equal(1, result.Audit.Summary.RejectionCounts["MISSING_PRODUCT_CATEGORY"]);
            Equal(1, result.Audit.Summary.RejectionCounts["MISSING_ABSOLUTE_LOCALIZATION"]);
            Equal(64, result.Audit.Summary.RejectionSha256.Length);
            Equal(0, issues.Count(x => x.Severity == ValidationSeverity.Error));
        }, ref passed);

        Console.WriteLine($"[self-test] PASS {passed}/12");
    }

    static ChallengeMetadataSnapshot Snapshot(string id, params (string Key, string Reward)[] values)
    {
        var snapshot = new ChallengeMetadataSnapshot { Source = new MetadataSource { SnapshotId = id } };
        foreach (var value in values)
            snapshot.GeneratedChallenges[value.Key] = new GeneratedChallenge
            {
                CountedRewards = [new CountedStoreReward { StoreItem = value.Reward, ItemCount = 1 }]
            };
        return snapshot;
    }

    static void Test(string name, Action action, ref int passed)
    {
        action();
        passed++;
        Console.WriteLine($"[self-test] PASS {name}");
    }

    static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}
