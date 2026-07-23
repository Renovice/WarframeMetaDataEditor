using MetadataPatchEditor.Core;
string cacheDir = @"C:\Users\Bartek\OneDrive\Dokumenter\Warframe\Cache.Windows";
var r = PackagesBinDecoder.DecodeBytes(Cache.ExtractPackagesBin(cacheDir));
var names = LanguagesBin.Decode(Cache.ExtractLanguagesBin(cacheDir,"en"));
var cat = MetadataCatalog.Build(r, names);
// all types whose leaf relates to Cantic Prism (CorpAmpSet1BarrelPartA) — dump their top-level keys
System.Console.WriteLine("=== all *CorpAmpSet1BarrelPartA* types + their top-level field keys ===");
foreach(var p in r.Types.Keys.Where(x=>x.Contains("CorpAmpSet1BarrelPartA",System.StringComparison.OrdinalIgnoreCase)).OrderBy(x=>x)){
  var d=DumpParser.ParseText(cat.ComposedText(p));
  var pc=d.TopLevel.FirstOrDefault(f=>f.Key=="ProductCategory")?.RawValue.Trim()??"(none)";
  System.Console.WriteLine($"\n  {p}  [PC={pc}]  parent={cat.Parent(p)}");
  System.Console.WriteLine("     keys: "+string.Join(", ", d.TopLevel.Where(f=>f.Kind!=FieldKind.ComplexBlock).Select(f=>f.Key)));
  System.Console.WriteLine("     blocks: "+string.Join(", ", d.TopLevel.Where(f=>f.Kind==FieldKind.ComplexBlock).Select(f=>f.Key)));
}
// what does a prism's parent chain / fire stats look like? check for DamagePerShot etc anywhere in composed text
var pp="/Lotus/Weapons/Sentients/OperatorAmplifiers/CorpusAmpSet1/CorpAmpSet1BarrelPartA";
if(r.Types.ContainsKey(pp)){
  var txt=cat.ComposedText(pp);
  foreach(var key in new[]{"DamagePerShot","FireRate","Trigger","CritChance","StatusChance","AmmoClipSize","ProjectileSpeed","Magazine"}){
    int i=txt.IndexOf(key); if(i>=0) System.Console.WriteLine($"   FOUND {txt.Substring(i,System.Math.Min(40,txt.Length-i)).Split('\n')[0]}");
  }
}
