// Dev harness for the Core decoder — decodes the cache and prints a catalog summary.
// Usage:  dev [<path to your Warframe folder, or its Cache.Windows>]
using MetadataPatchEditor.Core;
using System.Text.Json;
using System.Text.Json.Nodes;
if (args.Length > 0 && args[0] == "--inspect-type")
{
    if (args.Length < 3) throw new ArgumentException("Usage: mpe-dev --inspect-type <Warframe folder or Cache.Windows> <exact /Lotus path>");
    string? typeCache = Cache.NormalizeToCacheWindows(args[1]) ?? Cache.FindCacheWindows(args[1]);
    if (typeCache == null) throw new DirectoryNotFoundException("No Cache.Windows found under: " + args[1]);
    var typePackage = PackagesBinDecoder.DecodeBytes(Cache.ExtractPackagesBin(typeCache));
    string path = args[2];
    var seen = new HashSet<string>(StringComparer.Ordinal);
    while (path.Length > 0 && seen.Add(path) && typePackage.Types.TryGetValue(path, out var type))
    {
        Console.WriteLine($"--- {type.Path}\nPARENT={type.Parent}\nOWN:\n{type.OwnText ?? "(none)"}");
        path = type.Parent;
    }
    return;
}
if (args.Length > 0 && args[0] == "--inspect-references")
{
    if (args.Length < 3) throw new ArgumentException("Usage: mpe-dev --inspect-references <Warframe folder or Cache.Windows> <text>");
    string? referenceCache = Cache.NormalizeToCacheWindows(args[1]) ?? Cache.FindCacheWindows(args[1]);
    if (referenceCache == null) throw new DirectoryNotFoundException("No Cache.Windows found under: " + args[1]);
    var referencePackage = PackagesBinDecoder.DecodeBytes(Cache.ExtractPackagesBin(referenceCache));
    string query = args[2];
    var matches = referencePackage.Types.Values
        .Where(type => type.Path.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || type.Parent.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || (type.OwnText?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
        .OrderBy(type => type.Path, StringComparer.Ordinal)
        .ToList();
    foreach (var type in matches)
    {
        Console.WriteLine($"--- {type.Path}\nPARENT={type.Parent}");
        if (!string.IsNullOrWhiteSpace(type.OwnText)) Console.WriteLine(type.OwnText);
    }
    Console.WriteLine($"SUMMARY references={matches.Count}");
    return;
}
if (args.Length > 0 && args[0] == "--inspect-status")
{
    string searchRoot = args.Length >= 2 ? args[1] : AppContext.BaseDirectory;
    string query = args.Length >= 3 ? args[2] : "Corrosive";
    string? statusCache = Cache.NormalizeToCacheWindows(searchRoot) ?? Cache.FindCacheWindows(searchRoot);
    if (statusCache == null) throw new DirectoryNotFoundException("No Cache.Windows found under: " + searchRoot);
    var statusPackage = PackagesBinDecoder.DecodeBytes(Cache.ExtractPackagesBin(statusCache));
    var statusNames = LanguagesBin.Decode(Cache.ExtractLanguagesBin(statusCache, "en"));
    var statusCatalog = MetadataCatalog.Build(statusPackage, statusNames);
    var matches = statusCatalog.ByCategory.Where(kv => kv.Key.StartsWith("Status Effects", StringComparison.Ordinal))
        .SelectMany(kv => kv.Value.Select(item => (Category: kv.Key, Item: item)))
        .Where(x => x.Item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                 || x.Item.Path.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    foreach (var match in matches)
    {
        string textValue = statusCatalog.ComposedText(match.Item.Path);
        var parsedValue = DumpParser.ParseText(textValue);
        var guidedValue = GameplayMetadata.BuildStatusFields(textValue);
        Console.WriteLine($"--- {match.Category} | {match.Item.Name} | {match.Item.Path}");
        Console.WriteLine($"PARSED path={parsedValue.Path}; top={parsedValue.TopLevel.Count}; guided={guidedValue.Count}; query={Extract.QueryableText(textValue).Count}");
        Console.WriteLine(textValue);
    }
    Console.WriteLine($"SUMMARY matches={matches.Count}");
    return;
}
if (args.Length > 0 && args[0] == "--inspect-highest-dot")
{
    string searchRoot = args.Length >= 2 ? args[1] : AppContext.BaseDirectory;
    string? dotCache = Cache.NormalizeToCacheWindows(searchRoot) ?? Cache.FindCacheWindows(searchRoot);
    if (dotCache == null) throw new DirectoryNotFoundException("No Cache.Windows found under: " + searchRoot);
    var dotPackage = PackagesBinDecoder.DecodeBytes(Cache.ExtractPackagesBin(dotCache));
    var dotNames = LanguagesBin.Decode(Cache.ExtractLanguagesBin(dotCache, "en"));
    var dotCatalog = MetadataCatalog.Build(dotPackage, dotNames);
    int statusCount = 0, explicitCount = 0, offeredCount = 0;
    foreach (var category in dotCatalog.ByCategory.Where(kv => kv.Key.StartsWith("Status Effects", StringComparison.Ordinal)))
    {
        Console.WriteLine($"## {category.Key}");
        foreach (var item in category.Value)
        {
            statusCount++;
            string textValue = dotCatalog.ComposedText(item.Path);
            bool hasDot = textValue.Contains("DamageOverTime=", StringComparison.Ordinal);
            bool explicitSwitch = textValue.Contains("UseHighestDamageOverTime=", StringComparison.Ordinal);
            bool offered = GameplayMetadata.BuildStatusFields(textValue).Any(f => f.Key == "UseHighestDamageOverTime");
            if (explicitSwitch) explicitCount++;
            if (offered) offeredCount++;
            if (hasDot || explicitSwitch || offered)
                Console.WriteLine($"{item.Name} | DOT={hasDot} | explicit={explicitSwitch} | editorOffers={offered} | {item.Path}");
        }
    }
    Console.WriteLine($"SUMMARY status={statusCount} explicit={explicitCount} editorOffers={offeredCount}");
    return;
}
if (args.Length > 0 && args[0] == "--inspect-multi-mod")
{
    string searchRoot = args.Length >= 2 ? args[1] : AppContext.BaseDirectory;
    string? multiCache = Cache.NormalizeToCacheWindows(searchRoot) ?? Cache.FindCacheWindows(searchRoot);
    if (multiCache == null) throw new DirectoryNotFoundException("No Cache.Windows found under: " + searchRoot);
    var multiPackage = PackagesBinDecoder.DecodeBytes(Cache.ExtractPackagesBin(multiCache));
    var multiNames = LanguagesBin.Decode(Cache.ExtractLanguagesBin(multiCache, "en"));
    var multiCatalog = MetadataCatalog.Build(multiPackage, multiNames);
    int shown = 0;
    foreach (var item in multiCatalog.ByCategory.Where(kv => kv.Key.StartsWith("Mods", StringComparison.Ordinal)).SelectMany(kv => kv.Value))
    {
        string textValue = multiCatalog.ComposedText(item.Path);
        var upgradeField = DumpParser.ParseText(textValue).TopLevel.FirstOrDefault(f => f.Key == "Upgrades");
        if (upgradeField == null) continue;
        int count = Extract.Upgrades(upgradeField.RawValue, "Upgrades")
            .Select(u => u.Path.Split('.')[1]).Distinct(StringComparer.Ordinal).Count();
        if (count < 2) continue;
        Console.WriteLine($"--- {item.Name} | entries={count} | {item.Path}");
        Console.WriteLine(textValue);
        if (++shown == 3) break;
    }
    Console.WriteLine($"SUMMARY shown={shown}");
    return;
}
if (args.Length > 0 && args[0] == "--inspect-mod")
{
    string searchRoot = args.Length >= 2 ? args[1] : AppContext.BaseDirectory;
    string query = args.Length >= 3 ? args[2] : "Stretch";
    string? inspectCache = Cache.NormalizeToCacheWindows(searchRoot) ?? Cache.FindCacheWindows(searchRoot);
    if (inspectCache == null) throw new DirectoryNotFoundException("No Cache.Windows found under: " + searchRoot);
    Console.WriteLine($"Decoding current cache read-only: {inspectCache}");
    var inspectPackage = PackagesBinDecoder.DecodeBytes(Cache.ExtractPackagesBin(inspectCache));
    var inspectNames = LanguagesBin.Decode(Cache.ExtractLanguagesBin(inspectCache, "en"));
    var inspectCatalog = MetadataCatalog.Build(inspectPackage, inspectNames);
    var matches = inspectCatalog.ByCategory.Where(kv => kv.Key.StartsWith("Mods", StringComparison.Ordinal))
        .SelectMany(kv => kv.Value)
        .Where(item => item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || item.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
        .Take(20).ToList();
    foreach (var match in matches)
    {
        Console.WriteLine($"--- {match.Name} | {match.Category} | {match.Path}");
        Console.WriteLine(inspectCatalog.ComposedText(match.Path));
    }
    Console.WriteLine($"ENUM FusionLimit: {string.Join(", ", inspectCatalog.EnumOptions("FusionLimit", "QA_HIGH"))}");
    Console.WriteLine($"ENUM UpgradeType/AVATAR: {string.Join(", ", inspectCatalog.EnumOptions("UpgradeType", "AVATAR_ABILITY_RANGE"))}");
    Console.WriteLine($"SUMMARY matches={matches.Count}");
    return;
}
if (args.Length > 0 && args[0] == "--inspect-weapon-elements")
{
    string searchRoot = args.Length >= 2 ? args[1] : AppContext.BaseDirectory;
    string? elementCache = Cache.NormalizeToCacheWindows(searchRoot) ?? Cache.FindCacheWindows(searchRoot);
    if (elementCache == null) throw new DirectoryNotFoundException("No Cache.Windows found under: " + searchRoot);
    var elementPackage = PackagesBinDecoder.DecodeBytes(Cache.ExtractPackagesBin(elementCache));
    var elementNames = LanguagesBin.Decode(Cache.ExtractLanguagesBin(elementCache, "en"));
    var elementCatalog = MetadataCatalog.Build(elementPackage, elementNames);
    int profiles = 0;
    foreach (var item in elementCatalog.ByCategory.Where(kv => GameplayMetadata.IsWeaponCategory(kv.Key)).SelectMany(kv => kv.Value))
    {
        var fields = GameplayMetadata.BuildWeaponFields(elementCatalog.ComposedText(item.Path));
        foreach (var group in fields.GroupBy(field => field.Group, StringComparer.Ordinal))
        {
            var shares = group.Where(field => field.Key.StartsWith("DT_", StringComparison.Ordinal)
                    && field.Key is not "DT_IMPACT" and not "DT_PUNCTURE" and not "DT_SLASH"
                    && decimal.TryParse(field.Original, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out decimal value) && value > 0)
                .Select(field => $"{field.Key}={field.Original}").ToList();
            if (shares.Count < 2) continue;
            Console.WriteLine($"{item.Category} | {item.Name} | {group.Key} | {string.Join(", ", shares)} | {item.Path}");
            profiles++;
            if (profiles >= 25) break;
        }
        if (profiles >= 25) break;
    }
    Console.WriteLine($"SUMMARY multi-element-profiles={profiles}");
    return;
}
if (args.Length > 0 && args[0] == "--self-test")
{
    int passed = 0;
    void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL " + name);
        Console.WriteLine("PASS " + name);
        passed++;
    }
    const string fixture = ">/Lotus/Powersuits/Bard/OctaviaPrime\nArtifactSlots={\nAP_UNIVERSAL,\nAP_TACTIC,\nAP_TACTIC,\nAP_UNIVERSAL,\nAP_UNIVERSAL,\nAP_UNIVERSAL,\nAP_UNIVERSAL,\nAP_DEFENSE,\nAP_TACTIC,\nAP_UNIVERSAL,\nAP_UNIVERSAL,\nAP_UNIVERSAL\n}\n";
    const string octaviaPrime = "/Lotus/Powersuits/Bard/OctaviaPrime";
    Check(EquipmentExperiments.ReadSet(fixture, "ArtifactSlots").Count == 12, "reads stock Octavia slot count");
    Check(EquipmentExperiments.ValidateArtifactSlots("{AP_ANY,NOT_A_POLARITY}") != null, "rejects malformed polarity token");
    Check(EquipmentExperiments.ValidateArtifactSlots("{}") != null, "rejects empty slot layout");
    Check(EquipmentExperiments.ValidateAbsolutePathSet("AdditionalBaseModTypes", "{/Lotus/Types/Game/LotusAuraUpgrade,bad}") != null, "rejects malformed additional-base path");
    Check(EquipmentExperiments.ValidateAbsolutePath("ItemCompatibility", "Lotus/Weapons/Tenno/Rifle/LotusRifle") != null, "rejects relative compatibility path");
    const string singleUpgradeFixture = """
{
{
UpgradeType=AVATAR_ABILITY_RANGE
OperationType=STACKING_MULTIPLY
Value=0.075
DamageType=DT_ANY
}
}
""";
    string twoEffects = ModEffects.Append(singleUpgradeFixture, ModEffects.VanillaFireResistance);
    Check(ModEffects.EntryCount(twoEffects) == 2, "native mod-effect composer preserves the original and appends exactly one entry");
    Check(twoEffects.Contains("UpgradeType=AVATAR_ABILITY_RANGE", StringComparison.Ordinal)
          && twoEffects.Contains("UpgradeType=AVATAR_DAMAGE_TAKEN", StringComparison.Ordinal)
          && twoEffects.Contains("DamageType=DT_FIRE", StringComparison.Ordinal),
        "Fire-resistance preset uses the verified vanilla Flame Repellent gameplay shape");
    Check(twoEffects.Contains("AvatarDamageResistanceFirePercentModDesc", StringComparison.Ordinal),
        "Fire-resistance preset carries the vanilla per-effect localization key");
    var fireResistanceWithoutDescription = ModEffects.VanillaFireResistance with { DescriptionLocTag = "" };
    string noDescriptionEntry = ModEffects.BuildEntry(fireResistanceWithoutDescription);
    Check(ModEffects.IsFireResistance(fireResistanceWithoutDescription)
          && noDescriptionEntry.Contains("OverrideLocalization=0", StringComparison.Ordinal)
          && noDescriptionEntry.Contains("LocTag=\"\"", StringComparison.Ordinal),
        "Fire-resistance gameplay can be added without requesting a visible card-description line");
    Check(ModEffects.Validate(ModEffects.VanillaFireResistance) == null, "verified vanilla mod-effect preset passes strict validation");
    Check(ModEffects.Validate(ModEffects.VanillaFireResistance with { UpgradeType = "NOT VALID" }) != null,
        "mod-effect composer rejects malformed enum tokens");

    const string weaponMetadataFixture = """
>/Lotus/Weapons/Test/TwoModeWeapon
Behaviors={
{
impact:LotusWeaponImpactBehavior={
AttackData={
Type=DT_ANY
DT_IMPACT=0.6
DT_SLASH=0.4
Amount=100
ProcChance=0.25
ForcedProcs={
PT_IMPACT,
PT_SLASH
}
}
}
criticalHitChance=0.2
criticalHitDamageMultiplier=2
state:Type=/EE/Types/Game/WeaponAutomaticStateBehavior
state:WeaponAutomaticStateBehavior={
reloadTime=2
fireRate=300
}
},
{
impact:LotusWeaponImpactBehavior={
AttackData={
Type=DT_FIRE
Amount=240
ProcChance=0.5
}
}
criticalHitChance=0.1
criticalHitDamageMultiplier=3
state:Type=/EE/Types/Game/WeaponBurstStateBehavior
state:WeaponBurstStateBehavior={
reloadTime=1.5
fireRate=120
BurstDelay=0.25
}
}
}
""";
    var weaponQuery = Extract.QueryableText(weaponMetadataFixture);
    Check(weaponQuery.Any(f => f.Path == "Behaviors.0.impact:LotusWeaponImpactBehavior.AttackData.Amount" && f.Value == "100"),
        "query parser preserves first weapon fire-mode index");
    Check(weaponQuery.Any(f => f.Path == "Behaviors.1.impact:LotusWeaponImpactBehavior.AttackData.Amount" && f.Value == "240"),
        "query parser preserves second weapon fire-mode index");
    Check(weaponQuery.Any(f => f.Path == "Behaviors.0.impact:LotusWeaponImpactBehavior.AttackData.ForcedProcs" && f.Value == "{PT_IMPACT,PT_SLASH}"),
        "query parser preserves forced-proc sets as one exact value");
    Check(weaponQuery.Any(f => f.Path == "Behaviors.0.impact:LotusWeaponImpactBehavior.AttackData.ForcedProcs.1" && f.Value == "PT_SLASH" && !f.IsSet),
        "query parser exposes safe scalar children inside simple proc sets");
    var guidedWeapon = GameplayMetadata.BuildWeaponFields(weaponMetadataFixture);
    Check(guidedWeapon.Count(f => f.Display == "Base damage") == 2,
        "guided weapon editor exposes damage independently for both fire modes");
    Check(guidedWeapon.Select(f => f.Group).Distinct().Count() == 2,
        "guided weapon editor assigns independent fire-mode sections");
    Check(guidedWeapon.All(f => !f.IsSet),
        "guided editor excludes collection nodes that scalar query assignment cannot replace safely");
    Check(guidedWeapon.Count(f => f.Key == "ForcedProcs") == 2,
        "guided weapon editor exposes existing forced proc types as scalar indexed entries");
    var damageComposition = WeaponDamageComposition.Parse(weaponMetadataFixture);
    Check(damageComposition.Profiles.Count == 2
          && damageComposition.FireControls.Count == 2
          && damageComposition.FireControls[0].ShotsPerSecond == 5
          && damageComposition.Profiles[0].TotalDamage == 100
          && damageComposition.Profiles[0].Components.First(component => component.Key == "DT_IMPACT").Damage == 60,
        "weapon damage composer converts legacy fractions into readable absolute damage");
    damageComposition.FireControls[0].FireRateRpm = "360";
    Check(damageComposition.FireControls[0].ShotsPerSecond == 6
          && damageComposition.FireControls[0].Validate() == null,
        "visual weapon editor exposes exact per-mode RPM and a readable shots-per-second conversion");
    var statusOnlyComposition = WeaponDamageComposition.Parse(weaponMetadataFixture);
    var statusOnlyProfile = statusOnlyComposition.Profiles[0];
    statusOnlyProfile.StatusChancePercent = "40";
    string statusOnlyBlock = statusOnlyComposition.BuildChangedTopLevelBlocks()["Behaviors"];
    Check(statusOnlyProfile.HasStatusChance && statusOnlyProfile.NativeStatusChance == "0.4"
          && statusOnlyProfile.ValidateStatusChance() == null
          && statusOnlyBlock.Contains("ProcChance=0.4", StringComparison.Ordinal)
          && statusOnlyBlock.Contains("DT_IMPACT=0.6", StringComparison.Ordinal)
          && statusOnlyBlock.Contains("Amount=100", StringComparison.Ordinal)
          && !statusOnlyBlock.Contains("UseNewFormat=1", StringComparison.Ordinal),
        "status-only weapon edit writes the selected profile chance without converting or changing its damage encoding");
    statusOnlyProfile.StatusChancePercent = "250";
    bool aboveOneHundredAccepted = statusOnlyProfile.ValidateStatusChance() == null
        && statusOnlyProfile.NativeStatusChance == "2.5";
    statusOnlyProfile.StatusChancePercent = "-1";
    Check(aboveOneHundredAccepted && statusOnlyProfile.ValidateStatusChance() != null,
        "visual weapon editor accepts status chance above 100 percent but rejects negative chance");
    const string fireRateOnlyFixture = ">/Lotus/Weapons/Test/FireRateOnly\nBehaviors={{state:FireOnly={fireRate=60}}}\n";
    var fireRateOnly = WeaponDamageComposition.Parse(fireRateOnlyFixture);
    fireRateOnly.FireControls[0].FireRateRpm = "120";
    Check(fireRateOnly.FireControls[0].Validate() == null
          && !fireRateOnly.BuildChangedTopLevelBlocks()["Behaviors"].Contains("reloadTime", StringComparison.Ordinal),
        "fire-rate editing preserves a firing state that legitimately has no reload scalar");
    var composedProfile = damageComposition.Profiles[0];
    foreach (string physical in new[] { "DT_IMPACT", "DT_PUNCTURE", "DT_SLASH" }) composedProfile.SetDamage(physical, 0);
    composedProfile.SetDamage("DT_MAGNETIC", 40);
    composedProfile.SetDamage("DT_VIRAL", 60);
    string composedBehaviors = damageComposition.BuildChangedTopLevelBlocks()["Behaviors"];
    Check(composedBehaviors.Contains("UseNewFormat=1", StringComparison.Ordinal)
          && composedBehaviors.Contains("DT_MAGNETIC=40", StringComparison.Ordinal)
          && composedBehaviors.Contains("DT_VIRAL=60", StringComparison.Ordinal)
          && composedBehaviors.Contains("Amount=100", StringComparison.Ordinal)
          && composedBehaviors.Contains("fireRate=360", StringComparison.Ordinal)
          && composedBehaviors.Contains("criticalHitChance=0.2", StringComparison.Ordinal),
        "weapon composer emits elements plus fire rate while preserving surrounding fire-mode metadata");

    const string warframeMetadataFixture = """
>/Lotus/Powersuits/Test/TestPowerSuit
ProductCategory=Suits
MaxHealthOverride=100
MaxShieldOverride=75
ArmourRatingOverride=125
MaxEnergy=150
InitialEnergy=50
MovementSpeedMultiplier=1.15
LevelUpgrades={
{
UpgradeType=AVATAR_HEALTH_MAX
OperationType=ADD_BASE
Value=10
},
{
UpgradeType=AVATAR_HEALTH_MAX
OperationType=ADD_BASE
Value=20
},
{
UpgradeType=AVATAR_SHIELD_MAX
OperationType=ADD_BASE
Value=15
},
{
UpgradeType=AVATAR_POWER_MAX
OperationType=ADD_BASE
Value=5
}
}
""";
    var warframeStats = WarframeStatsComposition.Parse(warframeMetadataFixture);
    Check(warframeStats.BaseStats.Count == 6
          && warframeStats.BaseValue("Base Health") == 100
          && warframeStats.RankTotal("AVATAR_HEALTH_MAX") == 30,
        "visual Warframe model exposes six friendly base stats and aggregate native rank gains");
    warframeStats.BaseStats.Single(stat => stat.Label == "Base Health").Value = "200";
    warframeStats.RankStats.Single(stat => stat.UpgradeType == "AVATAR_HEALTH_MAX").Value = "60";
    var warframeChanges = warframeStats.BuildChanges();
    Check(warframeChanges.Any(change => change.TopLevel && change.Key == "MaxHealthOverride" && change.Value == "200")
          && warframeChanges.Count(change => !change.TopLevel && change.Key == "AVATAR_HEALTH_MAX") == 2
          && warframeChanges.Any(change => change.Path == "LevelUpgrades.0.Value" && change.Value == "20")
          && warframeChanges.Any(change => change.Path == "LevelUpgrades.1.Value" && change.Value == "40"),
        "Warframe rank-total editing scales existing milestone values proportionally without creating new events");

    const string statusMetadataFixture = """
>/Lotus/Types/Enemies/BaseInjuryHandlers/BaseFireDamageProc
Duration=6
MaxStacks=10
StackStyle=OneActiveInstance
ConsolidateDamageOverTime=1
UseHighestDamageOverTime=0
DamageOverTime={
Type=DT_FIRE
Amount=0.5
ProcChance=1
}
DOTPercentOfBaseDamage={
ValueRange={0.5,0.5}
}
IncrementalUpgrades={
{
UpgradeType=AVATAR_ARMOUR
DamageType=DT_ANY
Value=0.1
}
}
""";
    var guidedStatus = GameplayMetadata.BuildStatusFields(statusMetadataFixture);
    Check(guidedStatus.Any(f => f.Key == "UseHighestDamageOverTime" && f.TopLevel),
        "guided status editor exposes native highest-DOT-basis switch");
    Check(guidedStatus.First(f => f.Key == "UseHighestDamageOverTime").Description
              .Contains("Independent per-hit DOT instances remain separate", StringComparison.Ordinal),
        "highest-DOT-basis help does not falsely claim independent Toxin instances replace each other");
    var absentSwitchStatus = GameplayMetadata.BuildStatusFields(statusMetadataFixture.Replace("UseHighestDamageOverTime=0\n", ""));
    Check(absentSwitchStatus.Any(f => f.Key == "UseHighestDamageOverTime"
              && f.Original == GameplayMetadata.OriginalOrInherited),
        "guided status editor offers the optional native DOT-basis switch when metadata inherits it");
    Check(guidedStatus.Any(f => f.Path == "DamageOverTime.Type" && f.Original == "DT_FIRE"),
        "guided status editor exposes exact DOT damage type");
    var upgradeDamageFilter = guidedStatus.First(f => f.Path == "IncrementalUpgrades.0.DamageType");
    Check(upgradeDamageFilter.Display == "Upgrade damage filter"
          && upgradeDamageFilter.Description.Contains("does not redefine", StringComparison.Ordinal),
        "guided status editor distinguishes nested DT_ANY upgrade filter from status identity");
    var badDamageType = guidedStatus.First(f => f.Path == "DamageOverTime.Type");
    Check(GameplayMetadata.Validate(badDamageType, "DT_NOT_REAL") != null,
        "guided status validation rejects unknown damage types");
    Check(guidedStatus.First(f => f.Key == "StackStyle").Options.SequenceEqual(GameplayMetadata.StackStyles)
          && guidedStatus.First(f => f.Key == "ConsolidateDamageOverTime").Options.SequenceEqual(GameplayMetadata.DotConsolidationModes)
          && guidedStatus.First(f => f.Key == "UseHighestDamageOverTime").Options.SequenceEqual(GameplayMetadata.HighestDotBasisModes),
        "guided status editor exposes inherited plus every observed storage, consolidation, and highest-basis mode");
    var absentStackStyleStatus = GameplayMetadata.BuildStatusFields(
        statusMetadataFixture.Replace("StackStyle=OneActiveInstance\n", ""));
    var optionalStackStyle = absentStackStyleStatus.Single(f => f.Key == "StackStyle");
    Check(optionalStackStyle.Original == GameplayMetadata.OriginalOrInherited
          && optionalStackStyle.TopLevel
          && optionalStackStyle.Options.Contains("OneInstancePerInstigator", StringComparer.Ordinal),
        "guided status editor synthesizes a reversible StackStyle control when metadata inherits the field");
    const string nonDotStatusFixture = """
>/Lotus/Types/Enemies/BaseInjuryHandlers/BaseImpactDamageProc
Duration=6
MaxStacks=5
""";
    var nonDotFields = GameplayMetadata.BuildStatusFields(nonDotStatusFixture);
    Check(new[] { "StackStyle", "ConsolidateDamageOverTime", "UseHighestDamageOverTime" }
            .All(key => nonDotFields.Any(f => f.Key == key && f.Original == GameplayMetadata.OriginalOrInherited)),
        "all optional DOT/storage controls are universal even when a status has no DOT payload yet");
    Check(GameplayMetadata.Validate(optionalStackStyle, "OneActiveInstance") == null
          && GameplayMetadata.Validate(optionalStackStyle, "OneInstancePerInstigator") == null
          && GameplayMetadata.Validate(optionalStackStyle, "OneInstancePerHit") == null
          && GameplayMetadata.Validate(optionalStackStyle, "NotAStackMode") != null,
        "StackStyle validation accepts only the inherited sentinel and three native values");
    var optionalConsolidation = nonDotFields.Single(f => f.Key == "ConsolidateDamageOverTime");
    var optionalHighest = nonDotFields.Single(f => f.Key == "UseHighestDamageOverTime");
    Check(GameplayMetadata.Validate(optionalConsolidation, GameplayMetadata.OriginalOrInherited) == null
          && GameplayMetadata.Validate(optionalConsolidation, "10") == null
          && GameplayMetadata.Validate(optionalConsolidation, "2") != null
          && GameplayMetadata.Validate(optionalHighest, GameplayMetadata.OriginalOrInherited) == null
          && GameplayMetadata.Validate(optionalHighest, "1") == null
          && GameplayMetadata.Validate(optionalHighest, "2") != null,
        "universal consolidation and highest-basis controls reject unsupported values without forcing an override");
    var sharedHelp = MetadataOptionHelp.Describe("StackStyle", "OneActiveInstance");
    var sourceHelp = MetadataOptionHelp.Describe("StackStyle", "OneInstancePerInstigator");
    var hitHelp = MetadataOptionHelp.Describe("StackStyle", "OneInstancePerHit");
    var fallbackHelp = MetadataOptionHelp.Describe("UpgradeType", "AVATAR_ABILITY_RANGE",
        "Gameplay stat modified by this entry.");
    Check(sharedHelp.Label == "One shared status instance"
          && sharedHelp.Tooltip.Contains("one active status record", StringComparison.OrdinalIgnoreCase)
          && sourceHelp.Label == "One instance per source"
          && sourceHelp.Tooltip.Contains("not guaranteed to distinguish two weapons", StringComparison.OrdinalIgnoreCase)
          && hitHelp.Label == "One instance per hit"
          && hitHelp.Tooltip.Contains("damage basis", StringComparison.OrdinalIgnoreCase)
          && fallbackHelp.Label == "Ability Range"
          && fallbackHelp.Tooltip.Contains("AVATAR_ABILITY_RANGE", StringComparison.Ordinal),
        "shared option-help system provides precise known explanations and safe decoded-enum fallbacks");
    var heatOverview = GameplayMetadata.BuildStatusOverview(
        "Heat — Enemy Base · ordinary shared default",
        "/Lotus/Types/Enemies/BaseInjuryHandlers/BaseFireDamageProc", guidedStatus);
    Check(heatOverview.Summary.Contains("armor reduction", StringComparison.OrdinalIgnoreCase)
          && heatOverview.Rules.Contains("Damage over time: yes (Heat)", StringComparison.Ordinal)
          && heatOverview.Steps.Any(step => step.Title.Contains("Armor reduction", StringComparison.Ordinal))
          && heatOverview.Steps.Any(step => step.Title == "Proc storage and source ownership"
              && step.Value == "One shared record")
          && heatOverview.Scope.Contains("shared ordinary-enemy base", StringComparison.Ordinal),
        "status overview explains Heat, source ownership, DOT, and inherited scope");
    const string coldMetadataFixture = """
>/Lotus/Types/Enemies/BaseInjuryHandlers/BaseFreezeDamageProc
Duration=6
MaxStacks=9
MaxStacksWithOverguard=4
StackStyle=OneInstancePerHit
RepeatFreezeModifier=0.05
StackedUpgrades={
{
BaseValue=0.1
RepeatValue=0.05
UpgradeType=AVATAR_CRIT_DAMAGE_VULNERABILITY
OperationType=ADD
Value=0
DamageType=DT_ANY
}
}
""";
    var coldFields = GameplayMetadata.BuildStatusFields(coldMetadataFixture);
    var coldOverview = GameplayMetadata.BuildStatusOverview(
        "Cold — Enemy Base · ordinary shared default",
        "/Lotus/Types/Enemies/BaseInjuryHandlers/BaseFreezeDamageProc", coldFields);
    Check(coldOverview.Summary.Contains("freezing", StringComparison.OrdinalIgnoreCase)
          && coldOverview.Steps.Any(step => step.Title.Contains("Slow", StringComparison.Ordinal))
          && coldOverview.Steps.Any(step => step.Title.Contains("Critical", StringComparison.Ordinal))
          && coldOverview.Rules.Contains("Damage over time: not enabled", StringComparison.Ordinal),
        "status overview explicitly separates Cold slow, freeze, critical vulnerability, and absent DOT");
    Check(coldOverview.Metrics.Any(metric => metric.Label == "Full freeze" && metric.Value == "10 procs")
          && coldOverview.Steps.Any(step => step.Title == "Slow buildup" && step.Value == "+5% per added stack")
          && coldOverview.Steps.Any(step => step.Title == "Full freeze"
              && step.Description.Contains("trigger is the cap", StringComparison.Ordinal)),
        "visual Cold editor derives ten total procs, five-percent repeated buildup, and the cap-based freeze rule");
    string[] visualStatusFamilies =
    [
        "Impact", "Puncture", "Slash", "Heat", "Cold", "Electricity", "Toxin", "Blast",
        "Radiation", "Gas", "Magnetic", "Viral", "Corrosive", "Void", "Tau"
    ];
    Check(visualStatusFamilies.All(status =>
        {
            var overview = GameplayMetadata.BuildStatusOverview(status + " — Enemy Base · ordinary shared default",
                "/Lotus/Types/Enemies/BaseInjuryHandlers/Base" + status + "DamageProc", coldFields);
            return overview.Status == status && overview.Accent.StartsWith('#')
                   && overview.Metrics.Count == 4 && overview.Steps.Count > 0
                   && overview.Steps.All(step => step.Icon != "effect");
        }),
        "all base physical, elemental, combined, Void, and Tau status families have visual metrics and explicit icons");
    Check(coldFields.Any(field => field.Key == "MaxStacksWithOverguard")
          && coldFields.Any(field => field.Key == "RepeatFreezeModifier"),
        "guided Cold editor exposes subclass-specific Overguard and slow fields");
    var coldDot = StatusDotConfiguration.Parse(coldMetadataFixture,
        "Cold — Enemy Base · ordinary shared default");
    Check(!coldDot.Existed && coldDot.DamageType == "DT_FREEZE"
          && coldDot.ConsolidationMode == GameplayMetadata.OriginalOrInherited
          && coldDot.StackStyle == "OneInstancePerHit",
        "DOT builder selects Cold damage while preserving the handler's explicit proc-storage mode");
    coldDot.PercentOfSourceHit = "0.35";
    coldDot.ConsolidationMode = "10";
    coldDot.HighestDamageBasisMode = "1";
    coldDot.StackStyle = "OneInstancePerInstigator";
    var coldDotChanges = coldDot.BuildChangedTopLevelFields();
    Check(coldDotChanges["DamageOverTime"].Contains("Type=DT_FREEZE", StringComparison.Ordinal)
          && coldDotChanges["DamageOverTime"].Contains("ProcChance=0", StringComparison.Ordinal)
          && coldDotChanges["DOTPercentOfBaseDamage"].Contains("ValueRange={0.35,0.35}", StringComparison.Ordinal)
          && coldDotChanges["ConsolidateDamageOverTime"] == "10"
          && coldDotChanges["UseHighestDamageOverTime"] == "1"
          && coldDotChanges["StackStyle"] == "OneInstancePerInstigator",
        "DOT builder emits a complete native payload plus independently selected consolidation, basis, and storage modes");
    var existingDot = StatusDotConfiguration.Parse(statusMetadataFixture,
        "Heat — Enemy Base · ordinary shared default");
    Check(existingDot.Existed && existingDot.BuildChangedTopLevelFields().Count == 0,
        "DOT builder preserves an unchanged existing DOT payload without emitting redundant fields");
    Check(GameplayMetadata.StatusDisplayName("/Lotus/Types/Enemies/BaseInjuryHandlers/BaseFireDamageProc") == "Heat — Enemy Base · ordinary shared default",
        "status catalog labels the normal enemy Heat handler and its inherited scope clearly");
    Check(GameplayMetadata.StatusDisplayName("/Lotus/Types/Enemies/Grineer/InjuryHandlers/TeshinFireDamageProc") == "Heat — Teshin · Grineer override",
        "status catalog does not mislabel a specialized Teshin handler as every Grineer enemy");
    Check(GameplayMetadata.StatusDisplayName("/Lotus/Types/Enemies/SpaceBattles/SpaceFireDamageProc").Contains("Railjack / Space"),
        "status catalog keeps Railjack handlers visibly separate");
    Check(GameplayMetadata.StatusDisplayName("/Lotus/Types/Enemies/Entrati/InjuryHandler/TriangleFireDamageProc")
              == "Heat — Triangle unit · Man in the Wall override"
          && GameplayMetadata.StatusDisplayName("/Lotus/Types/Enemies/BaseInjuryHandlers/NokkoVIPFireDamageProc")
              == "Heat — Nokko Colony VIP override",
        "status catalog identifies Triangle and Nokko as specialized inheriting child handlers");
    Check(GameplayMetadata.StatusDisplayName("/Lotus/Types/Game/InjuryHandlers/BaseEnemyRadiantDamageProc")
              == "Void — Enemy Base · ordinary shared default",
        "status catalog presents internal Radiant status as player-facing Void");
    Check(GameplayMetadata.StatusDisplayName("/Lotus/Types/Enemies/BaseInjuryHandlers/BaseSentientDamageProc")
              == "Tau — Enemy Base · ordinary shared default",
        "status catalog presents internal Sentient status as player-facing Tau");
    Check(GameplayMetadata.StatusDisplayName("/Lotus/Types/Enemies/Sentient/InjuryHandlers/VipTauDamageProc")
              .StartsWith("Tau —", StringComparison.Ordinal),
        "status catalog keeps Tau VIP overrides under the Tau name");
    var wfLayout = ModSlotLayouts.Build(fixture, new ModSlotLayoutRequest(
        octaviaPrime, "Warframes", 1, 2));
    Check(wfLayout.Kind == ModEquipmentKind.Warframe, "classifies Octavia as Warframe");
    Check(wfLayout.OriginalOrdinarySlots == 8, "maps stock Octavia to eight ordinary slots");
    Check(wfLayout.ResultOrdinarySlots == 9, "generates nine ordinary Warframe slots");
    Check(wfLayout.ResultAuraSlots == 2 && wfLayout.ResultUtilitySlots == 1, "preserves native two-Aura plus Exilus classification");
    Check(wfLayout.ModifiedSlots.Count == 14, "uses exact 14-entry Warframe layout for twelve visible cards");
    Check(wfLayout.ModifiedSlots.Take(12).SequenceEqual(wfLayout.OriginalSlots), "generic layout preserves all stock Octavia indices");
    Check(wfLayout.ModifiedSlots.Skip(12).All(v => v == "AP_ANY"), "generic layout appends only requested polarity");
    Check(wfLayout.ModifiedAdditionalBaseModTypes.SequenceEqual(ModSlotLayouts.JadeSecondAuraBaseTypes), "second Aura emits Jade's native accepted base types");
    Check(wfLayout.Patch.Contains("AdditionalBaseModTypes={/Lotus/Types/Game/LotusAuraUpgrade,/Lotus/Upgrades/Mods/Aura/FairyQuest/FairyQuestBaseAuraMod}"), "second Aura patch contains native compatibility declaration");
    Check(wfLayout.Patch == ModSlotLayouts.Build(fixture, new ModSlotLayoutRequest(
        octaviaPrime, "Warframes", 1, 2)).Patch, "generic layout generation is deterministic");
    Check(ModSlotLayouts.WarframeOrdinaryCountFromArtifactCount(12) == 8, "12-entry Warframe baseline maps to 8 ordinary");
    Check(ModSlotLayouts.WarframeAuraCountFromArtifactCount(12) == 1, "12-entry Warframe baseline maps to 1 Aura");
    Check(ModSlotLayouts.WarframeOrdinaryCountFromArtifactCount(13) == 8, "Jade 13-entry layout retains 8 ordinary");
    Check(ModSlotLayouts.WarframeAuraCountFromArtifactCount(13) == 2, "Jade 13-entry layout maps to 2 Aura");
    bool rejectedImpossible = false;
    try { _ = ModSlotLayouts.Build(fixture, new ModSlotLayoutRequest(octaviaPrime, "Warframes", 1, 1)); }
    catch (InvalidOperationException) { rejectedImpossible = true; }
    Check(rejectedImpossible, "rejects impossible extra-ordinary plus one-Aura Warframe request");

    const string mechFixture = ">/Lotus/Powersuits/EntratiMech/NechroTech\nArtifactSlots={\nAP_ATTACK,\nAP_DEFENSE,\nAP_UNIVERSAL,\nAP_TACTIC,\nAP_UNIVERSAL,\nAP_UNIVERSAL,\nAP_UNIVERSAL,\nAP_UNIVERSAL,\nAP_UNIVERSAL,\nAP_UNIVERSAL,\nAP_UNIVERSAL,\nAP_UNIVERSAL\n}\n";
    var mechLayout = ModSlotLayouts.Build(mechFixture, new ModSlotLayoutRequest(
        "/Lotus/Powersuits/EntratiMech/NechroTech", "Necramechs", 0, 0));
    Check(mechLayout.Kind == ModEquipmentKind.Necramech, "classifies Necramech profile");
    Check(mechLayout.OriginalOrdinarySlots == 12 && mechLayout.ResultOrdinarySlots == 12, "preserves Necramech native 3x4 ordinary grid");
    Check(mechLayout.ModifiedSlots.Take(12).SequenceEqual(mechLayout.OriginalSlots), "Necramech layout preserves stock indices");
    Check(ModSlotLayouts.Classify("Operator Suits", "/Lotus/Powersuits/Operator/OperatorSuit") == ModEquipmentKind.Generic,
        "does not misclassify Operator powersuits as Warframes");
    bool rejectedClippedWarframe = false;
    try { _ = ModSlotLayouts.Build(fixture, new ModSlotLayoutRequest(octaviaPrime, "Warframes", 2, 2)); }
    catch (InvalidOperationException) { rejectedClippedWarframe = true; }
    Check(rejectedClippedWarframe, "rejects Warframe layouts clipped beyond native 3x4 grid");
    bool rejectedClippedMech = false;
    try { _ = ModSlotLayouts.Build(mechFixture, new ModSlotLayoutRequest("/Lotus/Powersuits/EntratiMech/NechroTech", "Necramechs", 1, 0)); }
    catch (InvalidOperationException) { rejectedClippedMech = true; }
    Check(rejectedClippedMech, "rejects Necramech layouts clipped beyond native 3x4 grid");

    const string stretch = "/Lotus/Upgrades/Mods/Warframe/AvatarAbilityRangeMod";
    const string packageFixture = "{\"/Lotus/Upgrades/Mods/Warframe/AvatarAbilityRangeMod\":{\"rarity\":\"UNCOMMON\",\"fusionLimit\":5}}";
    var identity = UpgradeIdentity.ParsePackageValue(packageFixture, stretch);
    Check(identity == new UpgradeIdentityValue("UNCOMMON", 5), "reads authoritative rarity and numeric rank from public export");
    Check(UpgradeIdentity.Validate(stretch, "LEGENDARY", 10) == null, "accepts coordinated legendary rank-10 edit");
    Check(UpgradeIdentity.Validate(stretch, "MYTHIC", 10) != null, "rejects unknown visible rarity");
    Check(UpgradeIdentity.Validate(stretch, "RARE", 11) != null, "rejects exact rank above ten");
    Check(UpgradeIdentity.ValidFusionLimits.SequenceEqual(["QA_NONE", "QA_LOW", "QA_MEDIUM", "QA_HIGH", "QA_VERY_HIGH"]),
        "exposes all five client FusionLimit enum values");
    Check(UpgradeIdentity.ValidateClientFusionLimit(UpgradeIdentity.ClientFusionUnchanged) == null,
        "supports client-only inherit/no-patch mode");
    Check(UpgradeIdentity.ValidateClientFusionLimit("QA_IMPOSSIBLE") != null,
        "rejects unknown client FusionLimit enum values");
    var manifestJson = UpgradeIdentity.BuildManifestJson("Stretch", stretch, hasClientMetadata: true);
    using (var manifest = JsonDocument.Parse(manifestJson))
    {
        Check(manifest.RootElement.GetProperty("components").GetProperty("serverDefinitions").GetBoolean(),
            "server package manifest declares server definition component");
        Check(!manifest.RootElement.GetProperty("components").GetProperty("inventoryMigration").GetBoolean(),
            "server package manifest forbids implicit inventory migration");
    }
    var definitionJson = UpgradeIdentity.BuildDefinitionJson(stretch, "LEGENDARY", 10);
    Check(definitionJson.Contains(stretch) && definitionJson.Contains("LEGENDARY") && definitionJson.Contains("10"),
        "builds standalone server definition JSON");

    var testRoot = Path.Combine(Path.GetTempPath(), "renovice-metadata-editor-selftest-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(Path.Combine(testRoot, "src"));
        Directory.CreateDirectory(Path.Combine(testRoot, "node_modules", "warframe-public-export-plus"));
        File.WriteAllText(Path.Combine(testRoot, "package.json"), "{}");
        File.WriteAllText(Path.Combine(testRoot, "node_modules", "warframe-public-export-plus", "ExportUpgrades.json"), packageFixture);
        var installed = UpgradeIdentity.WriteServerPackage(testRoot, "Stretch", stretch, "LEGENDARY", 10, hasClientMetadata: true);
        Check(File.Exists(installed.ManifestFile) && File.Exists(installed.DefinitionsFile),
            "installs a two-file package under Metadata Patches/Enabled");
        Check(UpgradeIdentity.HasEnabledServerPackage(testRoot, stretch), "detects installed enabled server package");
        Check(UpgradeIdentity.ReadEffectiveValue(testRoot, stretch) == new UpgradeIdentityValue("LEGENDARY", 10),
            "enabled package overlays vanilla server definition");
        var disabled = UpgradeIdentity.DisableServerPackage(testRoot, stretch);
        Check(Directory.Exists(disabled) && !UpgradeIdentity.HasEnabledServerPackage(testRoot, stretch),
            "disables package by moving it out of Enabled");
        Check(UpgradeIdentity.ReadEffectiveValue(testRoot, stretch) == new UpgradeIdentityValue("UNCOMMON", 5),
            "disabled package restores vanilla effective definition");
        var matches = ServerMetadataPatches.FindMatches(testRoot, stretch);
        Check(matches.Count == 1 && matches[0].Dataset == "ExportUpgrades" && matches[0].Path.Count == 1,
            "generic mapper locates a selected item in its Public Export dataset");
        var genericValues = new Dictionary<string, JsonNode?> { ["baseDrain"] = JsonValue.Create(99) };
        var genericJson = ServerMetadataPatches.BuildDefinitionJson(
            matches[0].Dataset, matches[0].Path, genericValues);
        Check(genericJson.Contains("ExportUpgrades") && genericJson.Contains("baseDrain"),
            "builds dataset/path/value generic server definition JSON");
        var genericPackage = ServerMetadataPatches.WriteServerPackage(
            testRoot, "Stretch", matches[0].Dataset, matches[0].Path, genericValues, hasClientMetadata: false);
        Check(File.Exists(genericPackage.DefinitionsFile) &&
              Path.GetFileName(genericPackage.DefinitionsFile) == ServerMetadataPatches.DefinitionsFileName,
            "installs generic server-definitions.json package");
        Check(ServerMetadataPatches.HasEnabledServerPackage(testRoot, matches[0].Dataset, matches[0].Path),
            "detects installed generic server package");
        var genericDisabled = ServerMetadataPatches.DisableServerPackage(testRoot, matches[0].Dataset, matches[0].Path);
        Check(Directory.Exists(genericDisabled) &&
              !ServerMetadataPatches.HasEnabledServerPackage(testRoot, matches[0].Dataset, matches[0].Path),
            "disables generic server package by folder move");
    }
    finally
    {
        if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
    }
    Console.WriteLine($"SUMMARY passed={passed} failed=0");
    return;
}
if (args.Length > 0 && args[0] == "--verify-gameplay")
{
    string searchRoot = args.Length >= 2 ? args[1] : AppContext.BaseDirectory;
    string? gameplayCache = Cache.NormalizeToCacheWindows(searchRoot) ?? Cache.FindCacheWindows(searchRoot);
    if (gameplayCache == null) throw new DirectoryNotFoundException("No Cache.Windows found under: " + searchRoot);
    Console.WriteLine($"Decoding current cache read-only: {gameplayCache}");
    var gameplayPackage = PackagesBinDecoder.DecodeBytes(Cache.ExtractPackagesBin(gameplayCache));
    var gameplayNames = LanguagesBin.Decode(Cache.ExtractLanguagesBin(gameplayCache, "en"));
    var gameplayCatalog = MetadataCatalog.Build(gameplayPackage, gameplayNames);
    var statuses = gameplayCatalog.ByCategory.Where(kv => kv.Key.StartsWith("Status Effects", StringComparison.Ordinal))
        .SelectMany(kv => kv.Value).ToList();
    if (statuses.Count == 0)
        throw new Exception("FAIL current cache exposes no Status Effects category");
    int statusGroups = gameplayCatalog.ByCategory.Count(kv => kv.Key.StartsWith("Status Effects —", StringComparison.Ordinal));
    if (statusGroups < 4) throw new Exception($"FAIL current cache produced only {statusGroups} status subcategories");
    var dotScopeCounts = gameplayCatalog.ByCategory.Where(kv => kv.Key.StartsWith("Status Effects —", StringComparison.Ordinal))
        .ToDictionary(kv => kv.Key, kv => kv.Value.Count(item =>
            gameplayCatalog.ComposedText(item.Path).Contains("DamageOverTime=", StringComparison.Ordinal)),
            StringComparer.Ordinal);
    int dotSwitchHandlers = dotScopeCounts.Values.Sum();
    int explicitHighest = statuses.Count(item => gameplayCatalog.ComposedText(item.Path).Contains("UseHighestDamageOverTime=", StringComparison.Ordinal));
    var specializedStatuses = gameplayCatalog.ByCategory["Status Effects — Specialized Enemy Overrides"];
    var ordinaryStatuses = gameplayCatalog.ByCategory["Status Effects — Ordinary Ground Enemies"];
    if (dotSwitchHandlers != 22 || explicitHighest != 1
        || !specializedStatuses.Any(item => item.Path.EndsWith("/TriangleFireDamageProc", StringComparison.Ordinal))
        || !specializedStatuses.Any(item => item.Path.EndsWith("/NokkoVIPFireDamageProc", StringComparison.Ordinal))
        || !ordinaryStatuses.Any(item => item.Name.StartsWith("Void —", StringComparison.Ordinal))
        || !ordinaryStatuses.Any(item => item.Name.StartsWith("Tau —", StringComparison.Ordinal)))
        throw new Exception("FAIL current DOT-handler scope distribution no longer matches the reflected metadata");
    var liveVisualOverviews = statuses.Select(item => GameplayMetadata.BuildStatusOverview(
        item.Name, item.Path, GameplayMetadata.BuildStatusFields(gameplayCatalog.ComposedText(item.Path)))).ToList();
    if (liveVisualOverviews.Any(overview => overview.Metrics.Count != 4 || overview.Steps.Count == 0)
        || !new[] { "Cold", "Heat", "Toxin", "Electricity", "Blast", "Radiation", "Gas", "Magnetic", "Viral", "Corrosive", "Void", "Tau" }
            .All(family => liveVisualOverviews.Any(overview => overview.Status == family)))
        throw new Exception("FAIL current status catalog could not build complete visual models for every base elemental family");
    var liveStatusFields = statuses.Select(item => GameplayMetadata.BuildStatusFields(
        gameplayCatalog.ComposedText(item.Path))).ToList();
    if (liveStatusFields.Any(fields => fields.Count(field => field.Key == "StackStyle") != 1
            || fields.Count(field => field.Key == "ConsolidateDamageOverTime") != 1
            || fields.Count(field => field.Key == "UseHighestDamageOverTime") != 1
            || !fields.Single(field => field.Key == "StackStyle").Options.SequenceEqual(GameplayMetadata.StackStyles)
            || !fields.Single(field => field.Key == "ConsolidateDamageOverTime").Options.SequenceEqual(GameplayMetadata.DotConsolidationModes)
            || !fields.Single(field => field.Key == "UseHighestDamageOverTime").Options.SequenceEqual(GameplayMetadata.HighestDotBasisModes)))
        throw new Exception("FAIL one or more current status handlers lack universal DOT/storage controls");
    var modGroups = gameplayCatalog.ByCategory.Where(kv => kv.Key.StartsWith("Mods —", StringComparison.Ordinal)).ToList();
    if (modGroups.Count != 8 || modGroups.Sum(kv => kv.Value.Count) != 2278)
        throw new Exception("FAIL fine mod categories lost catalog coverage");
    const string baseHeat = "/Lotus/Types/Enemies/BaseInjuryHandlers/BaseFireDamageProc";
    string liveHeatText = gameplayCatalog.ComposedText(baseHeat);
    var heatFields = GameplayMetadata.BuildStatusFields(liveHeatText);
    if (!heatFields.Any(f => f.Key == "UseHighestDamageOverTime")
        || !heatFields.Any(f => f.Key == "StackStyle"))
        throw new Exception("FAIL normal-enemy Heat handler lacks its guided DOT/storage controls. Composed metadata:\n" + liveHeatText);
    string dotScopeSummary = string.Join(", ", dotScopeCounts.Select(kv =>
        $"{kv.Key.Replace("Status Effects — ", "", StringComparison.Ordinal)} {kv.Value}"));
    Console.WriteLine($"PASS current cache status-effects={statuses.Count} in {statusGroups} subcategories; visual-models={liveVisualOverviews.Count}; DOT-capable={dotSwitchHandlers} ({dotScopeSummary}); explicit switch={explicitHighest}; normal-enemy Heat editable-fields={heatFields.Count}");
    const string playerCorrosive = "/Lotus/Types/Player/InjuryHandlers/TennoCorrosiveDamageProc";
    var corrosiveFields = GameplayMetadata.BuildStatusFields(gameplayCatalog.ComposedText(playerCorrosive));
    if (corrosiveFields.Count == 0 || !corrosiveFields.Any(field => field.Path == "StackedUpgrades.0.DamageType"))
        throw new Exception("FAIL Player/Tenno Corrosive handler no longer exposes its guided nested-upgrade fields");
    Console.WriteLine($"PASS Player/Tenno Corrosive guided-fields={corrosiveFields.Count}; nested damage filter remains independently editable");
    Console.WriteLine($"PASS current cache mods={modGroups.Sum(kv => kv.Value.Count)} in {modGroups.Count} subcategories");
    var qaOptions = gameplayCatalog.EnumOptions("FusionLimit", "QA_HIGH");
    if (!new[] { "QA_NONE", "QA_LOW", "QA_MEDIUM", "QA_HIGH", "QA_VERY_HIGH" }.All(v => qaOptions.Contains(v, StringComparer.Ordinal)))
        throw new Exception("FAIL decoded enum catalog did not expose the complete QA_* FusionLimit family");
    var upgradeTypeOptions = gameplayCatalog.EnumOptions("UpgradeType", "");
    if (!upgradeTypeOptions.Contains("AVATAR_ABILITY_RANGE", StringComparer.Ordinal)
        || !upgradeTypeOptions.Contains("AVATAR_DAMAGE_TAKEN", StringComparer.Ordinal))
        throw new Exception("FAIL decoded enum catalog omitted verified mod UpgradeType values");
    const string stretchType = "/Lotus/Upgrades/Mods/Warframe/AvatarAbilityRangeMod";
    const string flameType = "/Lotus/Upgrades/Mods/Warframe/AvatarDamageResistanceFire";
    string flameText = gameplayCatalog.ComposedText(flameType);
    if (!flameText.Contains("UpgradeType=AVATAR_DAMAGE_TAKEN", StringComparison.Ordinal)
        || !flameText.Contains("OperationType=MULTIPLY", StringComparison.Ordinal)
        || !flameText.Contains("DamageType=DT_FIRE", StringComparison.Ordinal))
        throw new Exception("FAIL current Flame Repellent no longer matches the verified resistance preset");
    var stretchUpgrades = DumpParser.ParseText(gameplayCatalog.ComposedText(stretchType)).TopLevel.First(f => f.Key == "Upgrades").RawValue;
    string combinedUpgrade = ModEffects.Append(stretchUpgrades, ModEffects.VanillaFireResistance);
    if (ModEffects.EntryCount(combinedUpgrade) != ModEffects.EntryCount(stretchUpgrades) + 1)
        throw new Exception("FAIL current Stretch Upgrades could not accept one preserved native effect entry");
    Console.WriteLine($"PASS current enum families QA={qaOptions.Count}, UpgradeType={upgradeTypeOptions.Count}; Stretch + verified Fire-resistance composition is structurally valid");

    var weapon = gameplayCatalog.ByCategory["Primary Weapons"]
        .Select(item =>
        {
            var fields = GameplayMetadata.BuildWeaponFields(gameplayCatalog.ComposedText(item.Path));
            int profiles = fields.Select(f => f.Group).Distinct(StringComparer.Ordinal).Count();
            return (Item: item, Fields: fields, Profiles: profiles);
        })
        .FirstOrDefault(x => x.Fields.Count >= 8 && x.Profiles >= 2);
    if (weapon.Item == null) throw new Exception("FAIL current cache exposes no independently editable multi-profile primary weapon AttackData");
    var liveComposition = WeaponDamageComposition.Parse(gameplayCatalog.ComposedText(weapon.Item.Path));
    if (liveComposition.Profiles.Count < 2 || liveComposition.FireControls.Count == 0)
        throw new Exception("FAIL current multi-profile weapon did not expose both damage profiles and firing-state controls");
    var liveStatusComposition = WeaponDamageComposition.Parse(gameplayCatalog.ComposedText(weapon.Item.Path));
    var liveStatusProfile = liveStatusComposition.Profiles.FirstOrDefault(profile => profile.HasStatusChance);
    if (liveStatusProfile == null)
        throw new Exception("FAIL current multi-profile weapon did not expose profile-owned status chance");
    decimal originalStatusPercent = decimal.Parse(liveStatusProfile.StatusChancePercent, System.Globalization.CultureInfo.InvariantCulture);
    liveStatusProfile.StatusChancePercent = WeaponDamageProfile.Format(originalStatusPercent + 5m);
    string expectedNativeStatus = WeaponDamageProfile.Format((originalStatusPercent + 5m) / 100m);
    var liveStatusBlocks = liveStatusComposition.BuildChangedTopLevelBlocks();
    if (!liveStatusBlocks.Values.Any(block => block.Contains("ProcChance=" + expectedNativeStatus, StringComparison.Ordinal)))
        throw new Exception("FAIL current weapon status-only edit did not emit the exact selected profile ProcChance");
    var liveProfile = liveComposition.Profiles[0];
    liveProfile.SetDamage("DT_MAGNETIC", liveProfile.Components.First(c => c.Key == "DT_MAGNETIC").Damage + 1);
    liveProfile.SetDamage("DT_VIRAL", liveProfile.Components.First(c => c.Key == "DT_VIRAL").Damage + 1);
    var liveFireControl = liveComposition.FireControls[0];
    decimal originalRpm = decimal.Parse(liveFireControl.FireRateRpm, System.Globalization.CultureInfo.InvariantCulture);
    liveFireControl.FireRateRpm = WeaponDamageProfile.Format(originalRpm + 60);
    var liveBlocks = liveComposition.BuildChangedTopLevelBlocks();
    if (liveBlocks.Count == 0 || !liveBlocks.Values.Any(block => block.Contains("DT_MAGNETIC=", StringComparison.Ordinal)
            && block.Contains("DT_VIRAL=", StringComparison.Ordinal) && block.Contains("UseNewFormat=1", StringComparison.Ordinal)
            && block.Contains("fireRate=" + liveFireControl.FireRateRpm, StringComparison.Ordinal)))
        throw new Exception("FAIL live weapon composer did not emit preserved elements plus the exact firing-state rate");
    Console.WriteLine($"PASS current multi-profile weapon '{weapon.Item.Name}' fields={weapon.Fields.Count}; damage-profiles={liveComposition.Profiles.Count}; firing-states={liveComposition.FireControls.Count}; status chance + Magnetic + Viral + fire-rate patchable");
    Console.WriteLine("SUMMARY live status and weapon metadata verification passed");
    return;
}
if (args.Length > 0 && args[0] == "--verify-octavia")
{
    if (args.Length < 2) throw new ArgumentException("Usage: mpe-dev --verify-octavia <Warframe folder or Cache.Windows>");
    string? octaviaCache = Cache.NormalizeToCacheWindows(args[1]);
    if (octaviaCache == null) throw new DirectoryNotFoundException("No Cache.Windows found under: " + args[1]);
    Console.WriteLine($"Decoding current cache read-only: {octaviaCache}");
    var octaviaPackage = PackagesBinDecoder.DecodeBytes(Cache.ExtractPackagesBin(octaviaCache));
    var octaviaCatalog = MetadataCatalog.Build(octaviaPackage, new Dictionary<string, string>());
    const string octaviaPrime = "/Lotus/Powersuits/Bard/OctaviaPrime";
    var currentPlan = ModSlotLayouts.Build(octaviaCatalog.ComposedText(octaviaPrime),
        new ModSlotLayoutRequest(octaviaPrime, "Warframes", 1, 2));
    Console.WriteLine($"PASS current Octavia baseline slots={currentPlan.OriginalSlots.Count}; generated slots={currentPlan.ModifiedSlots.Count}; ordinary={currentPlan.ResultOrdinarySlots}; aura={currentPlan.ResultAuraSlots}");
    Console.WriteLine(currentPlan.Patch);
    return;
}
if (args.Length > 0 && args[0] == "--server-match")
{
    if (args.Length < 3) throw new ArgumentException("Usage: mpe-dev --server-match <SpaceNinjaServer root> <Lotus unique name>");
    var matches = ServerMetadataPatches.FindMatches(args[1], args[2]);
    foreach (var match in matches)
        Console.WriteLine($"MATCH {match.DisplayPath} fields={match.Entry.Count}");
    Console.WriteLine($"SUMMARY matches={matches.Count}");
    return;
}
string? cacheDir = args.Length > 0 ? Cache.NormalizeToCacheWindows(args[0]) : Cache.FindCacheWindows(AppContext.BaseDirectory);
if (cacheDir == null) { Console.WriteLine("No Cache.Windows found. Usage: dev <path to your Warframe folder or its Cache.Windows>"); return; }
Console.WriteLine($"Decoding {cacheDir} ...");
var pkg = PackagesBinDecoder.DecodeBytes(Cache.ExtractPackagesBin(cacheDir));
var names = LanguagesBin.Decode(Cache.ExtractLanguagesBin(cacheDir, "en"));
var cat = MetadataCatalog.Build(pkg, names);
Console.WriteLine($"Types: {pkg.Types.Count:N0} (aligned={pkg.Aligned})   Names: {names.Count:N0}   Catalog: {cat.ItemCount:N0} in {cat.ByCategory.Count} categories\n");
foreach (var c in cat.ByCategory.OrderByDescending(kv => kv.Value.Count)) Console.WriteLine($"   {c.Value.Count,6}  {c.Key}");
