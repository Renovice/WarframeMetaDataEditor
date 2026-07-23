using MetadataPatchEditor.Core;
using System.Diagnostics;
string cacheDir = @"C:\Users\Bartek\OneDrive\Dokumenter\Warframe\Cache.Windows";
var bytes = Cache.ExtractPackagesBin(cacheDir);
var sw = Stopwatch.StartNew();
var r = PackagesBinDecoder.DecodeBytes(bytes);
sw.Stop();
System.Console.WriteLine($"Decoded in {sw.ElapsedMilliseconds} ms (structural locate)");
System.Console.WriteLine($"EntityCount={r.EntityCount}  TextCount={r.TextCount}  Compressed={r.Compressed}  Raw={r.Raw}  Aligned={r.Aligned}  Types={r.Types.Count}");
bool ok = r.EntityCount==461789 && r.TextCount==201547 && r.Compressed==198870 && r.Aligned;
System.Console.WriteLine($"MATCHES known v46 counts: {ok}");
if(r.Types.TryGetValue("/Lotus/Weapons/Grineer/Bows/GrnBow/GrnBowWeapon", out var t) && t.OwnText!=null){
  foreach(var key in new[]{"reloadTime=","fireRate=","AmmoCapacity=","GripType="}){
    int i=t.OwnText.IndexOf(key); System.Console.WriteLine("   "+(i>=0?t.OwnText.Substring(i, System.Math.Min(24, t.OwnText.Length-i)).Split('\n')[0]:key+"?"));
  }
}
