using System.Text;

namespace MetadataPatchEditor.Core;

public enum FieldKind { Scalar, SimpleSet, ComplexBlock }

public record Field(string Key, string RawValue, FieldKind Kind);

public class Dump
{
    public string Path = "";
    public List<Field> TopLevel = new();
}

// A nested scalar reachable by dot-path, e.g. Upgrades.0.Value  (mods/arcanes).
public record UpgradeField(string Path, string Key, string Value);

// A per-weapon entry in a riven mod's ItemCompatibilities list.
public record RivenWeapon(string ItemType, string Tail, string Attenuation);

// Any scalar leaf in the tree, with an s|-ready anchor (its last named ancestors).
public record Leaf(string Anchor, string Key, string Value, bool TopLevel);

// Parses an OpenWF client-metadata dump (.txt). First line ">/path", then
// Key=Value / Key={...} lines. Indentation is unreliable, so nesting is tracked
// by BRACE BALANCE, not whitespace. (Validated vs real dumps.)
public static class DumpParser
{
    public static Dump Parse(string file) => Parse(File.ReadAllLines(file));
    public static Dump ParseText(string text) => Parse(text.Replace("\r", "").Split('\n'));

    public static Dump Parse(IEnumerable<string> lines)
    {
        var d = new Dump();
        int depth = 0;
        string? blockKey = null;
        var block = new StringBuilder();

        foreach (var raw in lines)
        {
            if (depth == 0)
            {
                var t = raw.Trim();
                if (t.Length == 0) continue;
                if (t.StartsWith(">")) { if (d.Path.Length == 0) d.Path = t[1..]; continue; }
                int eq = t.IndexOf('=');
                if (eq <= 0) continue;
                string key = t[..eq];
                string val = t[(eq + 1)..];
                int net = Count(val, '{') - Count(val, '}');
                if (net <= 0)
                {
                    d.TopLevel.Add(new Field(key, val, FieldKind.Scalar));
                }
                else
                {
                    blockKey = key;
                    block.Clear();
                    block.Append(val);
                    depth += net;
                }
            }
            else
            {
                block.Append('\n').Append(raw);
                depth += Count(raw, '{') - Count(raw, '}');
                if (depth <= 0)
                {
                    depth = 0;
                    string text = block.ToString();
                    d.TopLevel.Add(new Field(blockKey!, text,
                        IsSimpleSet(text) ? FieldKind.SimpleSet : FieldKind.ComplexBlock));
                    blockKey = null;
                }
            }
        }
        return d;
    }

    internal static int Count(string s, char c) { int n = 0; foreach (var ch in s) if (ch == c) n++; return n; }

    static bool IsSimpleSet(string raw)
    {
        int first = raw.IndexOf('{');
        int last = raw.LastIndexOf('}');
        if (first < 0 || last < 0 || last <= first) return false;
        return !raw.Substring(first + 1, last - first - 1).Contains('{');
    }
}

// Sub-parsers for the two nested structures we edit: an Upgrades array (mods/arcanes)
// and a riven mod's ItemCompatibilities list.
public static class Extract
{
    // Direct scalar children of each element of an Upgrades-style array block.
    // rootKey = "Upgrades" -> paths like "Upgrades.0.Value" (used with the q| op).
    public static List<UpgradeField> Upgrades(string rawBlock, string rootKey)
    {
        var res = new List<UpgradeField>();
        int depth = 0, index = -1;
        foreach (var lineRaw in rawBlock.Split('\n'))
        {
            var t = lineRaw.Trim();
            if (t.Length == 0) continue;
            var tc = t.TrimEnd(',');
            int open = DumpParser.Count(t, '{'), close = DumpParser.Count(t, '}');

            if (tc == "{") { if (depth == 1) index++; depth++; continue; }
            if (tc == "}") { depth--; continue; }

            int eq = t.IndexOf('=');
            if (eq > 0 && open == close)
            {
                if (depth == 2)
                    res.Add(new UpgradeField($"{rootKey}.{index}.{t[..eq]}", t[..eq], tc[(eq + 1)..]));
            }
            else
            {
                depth += open - close; // key={ opens deeper; skip its scalars
            }
        }
        return res;
    }

    // One (UpgradeType, Value) per element of a stat-upgrade array like LevelUpgrades (warframe
    // per-level scaling) — collapses each {UpgradeType, OperationType, Value, …} entry to a single
    // editable row. Path = "rootKey.N.Value" for the q| operator; Key = the UpgradeType.
    public static List<UpgradeField> StatUpgrades(string rawBlock, string rootKey)
    {
        var res = new List<UpgradeField>();
        int depth = 0, index = -1; string? utype = null, val = null;
        foreach (var lineRaw in rawBlock.Split('\n'))
        {
            var t = lineRaw.Trim();
            if (t.Length == 0) continue;
            var tc = t.TrimEnd(',');
            if (tc == "{") { if (depth == 1) { index++; utype = null; val = null; } depth++; continue; }
            if (tc == "}") { depth--; if (depth == 1 && val != null) res.Add(new UpgradeField($"{rootKey}.{index}.Value", utype ?? "Stat", val)); continue; }
            int eq = t.IndexOf('=');
            if (eq > 0 && depth == 2)
            {
                var k = t[..eq]; var v = tc[(eq + 1)..];
                if (k == "UpgradeType") utype = v;
                else if (k == "Value") val = v;
            }
        }
        return res;
    }

    // { {ItemType=.., Rarity=.., Attenuation=..}, ... }
    public static List<RivenWeapon> RivenWeapons(string rawBlock)
    {
        var res = new List<RivenWeapon>();
        string? itemType = null, atten = null;
        foreach (var lineRaw in rawBlock.Split('\n'))
        {
            var t = lineRaw.Trim().TrimEnd(',');
            if (t.StartsWith("ItemType=")) itemType = t["ItemType=".Length..];
            else if (t.StartsWith("Attenuation=")) atten = t["Attenuation=".Length..];
            if (itemType != null && atten != null)
            {
                res.Add(new RivenWeapon(itemType, Tail(itemType), atten));
                itemType = null; atten = null;
            }
        }
        return res;
    }

    public static string Tail(string path)
    {
        var p = path.Split('/');
        return p.Length >= 2 ? p[^2] + "/" + p[^1] : path;
    }

    // Every scalar leaf in the whole dump, each with an s|-ready anchor built from its
    // last 2 named ancestors. Used for nested weapon stats (damage/crit/fireRate).
    // Nesting tracked by brace balance; anon array elements push "" (skipped in anchors).
    public static List<Leaf> FlattenAll(string file) => FlattenLines(File.ReadAllLines(file));
    public static List<Leaf> FlattenAllText(string text) => FlattenLines(text.Replace("\r", "").Split('\n'));

    static List<Leaf> FlattenLines(IEnumerable<string> src)
    {
        var leaves = new List<Leaf>();
        var stack = new List<string>();
        bool skippedPath = false;
        foreach (var raw in src)
        {
            var t = raw.Trim();
            if (t.Length == 0) continue;
            if (!skippedPath && t.StartsWith(">")) { skippedPath = true; continue; }
            var tc = t.TrimEnd(',');
            int net = DumpParser.Count(t, '{') - DumpParser.Count(t, '}');

            if (tc == "{") { stack.Add(""); continue; }
            if (tc == "}") { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); continue; }

            int eq = t.IndexOf('=');
            if (eq <= 0) continue;
            string key = t[..eq], val = tc[(eq + 1)..];
            if (net <= 0)
                leaves.Add(new Leaf(LastNamed(stack, 2), key, val, stack.Count == 0));
            else
                stack.Add(key); // key={ opens a named block
        }
        return leaves;
    }

    static string LastNamed(List<string> stack, int n)
    {
        var picked = new List<string>();
        for (int i = stack.Count - 1; i >= 0 && picked.Count < n; i--)
            if (stack[i].Length > 0) picked.Insert(0, stack[i]);
        return string.Join(".*?", picked);
    }

    // Nested numeric arrays like  _healAmount={25,50,75,100,125,150}  (arcane per-rank
    // values, buried in LocKeyWordScript). Value = comma-joined numbers; patched via s|
    // replacing the whole {..}. Anchor = last 2 named ancestors.
    public static List<Leaf> NumericArrays(string file) => NumericArraysLines(File.ReadAllLines(file));
    public static List<Leaf> NumericArraysText(string text) => NumericArraysLines(text.Replace("\r", "").Split('\n'));

    static List<Leaf> NumericArraysLines(string[] lines)
    {
        var res = new List<Leaf>();
        var stack = new List<string>();
        bool skippedPath = false;
        for (int i = 0; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (t.Length == 0) continue;
            if (!skippedPath && t.StartsWith(">")) { skippedPath = true; continue; }
            var tc = t.TrimEnd(',');
            int net = DumpParser.Count(t, '{') - DumpParser.Count(t, '}');

            if (tc == "{") { stack.Add(""); continue; }
            if (tc == "}") { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); continue; }

            int eq = t.IndexOf('=');
            if (eq <= 0) continue;
            string key = t[..eq], val = tc[(eq + 1)..];
            if (net <= 0) continue;                        // scalar / balanced tuple
            if (val != "{") { stack.Add(key); continue; }   // opened with inline content

            // key={ : look ahead — is the block just bare numbers?
            var nums = new List<string>();
            int j = i + 1; bool numeric = true;
            for (; j < lines.Length; j++)
            {
                var tj = lines[j].Trim().TrimEnd(',');
                if (tj == "}") break;
                if (tj.Length == 0) continue;
                if (IsNumber(tj)) nums.Add(tj);
                else { numeric = false; break; }
            }
            if (numeric && nums.Count >= 2)
            {
                res.Add(new Leaf(LastNamed(stack, 2), key, string.Join(", ", nums), false));
                i = j;                 // skip to (and past) the closing }
            }
            else stack.Add(key);        // a real nested block — descend
        }
        return res;
    }

    static bool IsNumber(string s)
    {
        foreach (var c in s)
            if (!(char.IsDigit(c) || c is '.' or '-' or '+' or 'e' or 'E')) return false;
        return s.Length > 0;
    }
}

public static class PatchGenerator
{
    // Top-level scalar/set override (game keeps the FIRST duplicate). Proven.
    public static string Build(string itemPath, IEnumerable<(string key, string value)> edits)
    {
        var sb = new StringBuilder();
        sb.Append(itemPath).Append('\n');
        foreach (var (key, value) in edits)
            sb.Append("    ").Append(key).Append('=').Append(Compact(value)).Append('\n');
        return sb.ToString();
    }

    // Nested value in a queryable array (mods/arcanes Upgrades.N.field). Proven on boosters.
    public static string Upgrade(string itemPath, string dotPath, string value)
        => $"{itemPath}\n    q|{dotPath}|{Compact(value)}\n";

    // Per-weapon riven strength (cross-line regex; needs (?s)+lazy, not \s/[\s\S]). Proven.
    public static string RivenAttenuation(string modPath, string weaponPathTail, string value)
        => $"{modPath}\n    s|(?s)({weaponPathTail}.*?Attenuation)=[0-9.]+|$1={value}\n";

    public static string Compact(string v)
    {
        var s = v.Replace("\r", "").Replace("\n", " ");
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Replace("{ ", "{").Replace(" }", "}").Trim();
    }
}

public static class Catalog
{
    public static readonly string[] Categories = { "Weapons", "Mods", "Arcanes" };

    public static string? FindDumpsRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir != null)
        {
            var c1 = Path.Combine(dir.FullName, "data", "dumps");
            if (Directory.Exists(c1)) return c1;
            var c2 = Path.Combine(dir.FullName, "MetadataPatchEditor", "data", "dumps");
            if (Directory.Exists(c2)) return c2;
            dir = dir.Parent;
        }
        return null;
    }

    // Auto-locate the game's Metadata Patches folder. Walk up (covers the tool sitting
    // inside the Warframe tree) AND probe sibling folders under Documents (covers the
    // tool being moved to a sibling like "Warframe RE PROJECT RENOVICE").
    public static string? FindPatchesFolder(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir != null)
        {
            var here = Path.Combine(dir.FullName, "OpenWF", "Metadata Patches");
            if (Directory.Exists(here)) return here;
            // probe siblings of the current dir
            var parent = dir.Parent;
            if (parent != null)
            {
                foreach (var sib in SafeDirs(parent))
                {
                    var c = Path.Combine(sib, "OpenWF", "Metadata Patches");
                    if (Directory.Exists(c)) return c;
                }
            }
            dir = dir.Parent;
        }
        return null;
    }

    static IEnumerable<string> SafeDirs(DirectoryInfo d)
    {
        try { return Directory.GetDirectories(d.FullName); }
        catch { return Array.Empty<string>(); }
    }

    public static List<(string Name, string Path)> Items(string dumpsRoot, string category)
    {
        var folder = Path.Combine(dumpsRoot, category);
        var list = new List<(string Name, string Path)>();
        if (Directory.Exists(folder))
            foreach (var f in Directory.GetFiles(folder, "*.txt"))
                list.Add((Path.GetFileNameWithoutExtension(f), f));
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    // weapon uniqueName path -> friendly name, from the ">/path" first line of each
    // Weapons dump. Lets the Rivens tab show real names instead of path tails.
    public static Dictionary<string, string> WeaponNameMap(string dumpsRoot)
    {
        var map = new Dictionary<string, string>();
        var folder = Path.Combine(dumpsRoot, "Weapons");
        if (!Directory.Exists(folder)) return map;
        foreach (var f in Directory.GetFiles(folder, "*.txt"))
        {
            try
            {
                using var sr = new StreamReader(f);
                var first = sr.ReadLine();
                if (first != null && first.StartsWith(">"))
                    map[first[1..].Trim()] = Path.GetFileNameWithoutExtension(f);
            }
            catch { }
        }
        return map;
    }
}
