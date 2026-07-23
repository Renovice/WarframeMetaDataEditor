// Dev harness for the Core decoder — decodes the cache and prints a catalog summary.
// Usage:  dev [<path to your Warframe folder, or its Cache.Windows>]
using MetadataPatchEditor.Core;
string? cacheDir = args.Length > 0 ? Cache.NormalizeToCacheWindows(args[0]) : Cache.FindCacheWindows(AppContext.BaseDirectory);
if (cacheDir == null) { Console.WriteLine("No Cache.Windows found. Usage: dev <path to your Warframe folder or its Cache.Windows>"); return; }
Console.WriteLine($"Decoding {cacheDir} ...");
var pkg = PackagesBinDecoder.DecodeBytes(Cache.ExtractPackagesBin(cacheDir));
var names = LanguagesBin.Decode(Cache.ExtractLanguagesBin(cacheDir, "en"));
var cat = MetadataCatalog.Build(pkg, names);
Console.WriteLine($"Types: {pkg.Types.Count:N0} (aligned={pkg.Aligned})   Names: {names.Count:N0}   Catalog: {cat.ItemCount:N0} in {cat.ByCategory.Count} categories\n");
foreach (var c in cat.ByCategory.OrderByDescending(kv => kv.Value.Count)) Console.WriteLine($"   {c.Value.Count,6}  {c.Key}");
