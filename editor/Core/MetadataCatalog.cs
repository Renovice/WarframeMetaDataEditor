using System.Text;
using System.Text.Json;

namespace MetadataPatchEditor.Core;

/// <summary>One browsable item: its type path, display name, category, and (lazy) composed metadata text.</summary>
public sealed class CatalogItem
{
    public required string Path;
    public required string Name;
    public required string Category;
}

/// <summary>
/// In-memory catalog built from a full Packages.bin decode. Groups every item by the game's
/// ProductCategory (own or inherited), and composes full metadata (own + inherited) on demand.
/// </summary>
public sealed class MetadataCatalog
{
    readonly Dictionary<string, DecodedType> _types;
    readonly Dictionary<string, string?> _pcCache = new(StringComparer.Ordinal);
    readonly Dictionary<string, string?> _tagCache = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> _composed = new(StringComparer.Ordinal);
    Dictionary<string, string>? _names;   // LocalizeTag -> localized display name

    /// <summary>Where a weapon's REAL riven strength lives: the riven mod + the per-weapon Attenuation.</summary>
    public record RivenEntry(string ModPath, string WeaponTail, string Attenuation);
    /// <summary>weaponPath -> its riven Attenuation entry (the uncapped strength multiplier).</summary>
    public Dictionary<string, RivenEntry> Rivens { get; } = new(StringComparer.Ordinal);

    /// <summary>Category name -> items, sorted by name.</summary>
    public SortedDictionary<string, List<CatalogItem>> ByCategory { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<string> Categories => ByCategory.Keys;
    public int ItemCount { get; private set; }

    MetadataCatalog(Dictionary<string, DecodedType> types) { _types = types; }

    public static MetadataCatalog Build(PackagesBinDecoder.DecodeResult r, Dictionary<string, string>? names = null)
    {
        var cat = new MetadataCatalog(r.Types) { _names = names };

        // Riven index: per-INDIVIDUAL-weapon Attenuation (the real uncapped strength) from the 7
        // weapon-riven mods (/Randomized/*RandomModRare). Each weapon is its own compat entry.
        foreach (var (path, t) in r.Types)
        {
            if (t.OwnText == null) continue;
            if (!(path.Contains("/Randomized/", StringComparison.Ordinal) && path.EndsWith("RandomModRare", StringComparison.Ordinal))) continue;
            var block = TopBlock(t.OwnText, "ItemCompatibilities");
            if (block == null) continue;
            foreach (var w in Extract.RivenWeapons(block))
                cat.Rivens.TryAdd(w.ItemType, new RivenEntry(path, w.Tail, w.Attenuation));
        }

        // Collect candidates: skip /Lotus/StoreItems/ market wrappers (duplicates of the real item).
        var candidates = new List<CatalogItem>();
        foreach (var (path, t) in r.Types)
        {
            if (path.StartsWith("/Lotus/StoreItems/", StringComparison.Ordinal)) continue;
            var raw = cat.EffProductCategory(path);
            string friendly;
            if (raw != null)
            {
                friendly = FriendlyCategory(raw);
                if (path.Contains("CrewMember", StringComparison.Ordinal)) friendly = "Railjack Crew";  // Ash*CrewMemberSuit etc.
            }
            // Core mods (Serration, Stretch…) have ProductCategory ONLY on their StoreItems wrapper — the real
            // /Lotus/Upgrades/Mods/ type has none. Recognise them by path + a real name (skip rivens = /Randomized/).
            else if (path.StartsWith("/Lotus/Upgrades/Mods/", StringComparison.Ordinal)
                     && !path.Contains("/Randomized/", StringComparison.Ordinal)
                     && cat.EffLocalizeTag(path) != null)
                friendly = "Mods";
            else if (cat.EffIsProjectile(path)) friendly = "Projectiles";   // weapon projectiles (speed/AoE/lifespan/bounce…)
            else continue;
            var nm = cat.Display(path);
            if (nm.Length > 0 && nm.All(c => c == '?')) continue;   // unreleased/dev placeholders ("???")
            candidates.Add(new CatalogItem { Path = path, Name = nm, Category = friendly });
        }

        // Some real items carry ProductCategory ONLY on their /Lotus/StoreItems/ wrapper (many weapons, mods,
        // pets, ships, operator suits, emotes…). Use the wrapper as the authoritative category, but point to
        // the REAL editable type (strip /StoreItems/). Skip ones the passes above already handled.
        foreach (var (path, t) in r.Types)
        {
            if (!path.StartsWith("/Lotus/StoreItems/", StringComparison.Ordinal)) continue;
            var raw = cat.EffProductCategory(path);
            if (raw == null) continue;
            var real = "/Lotus/" + path["/Lotus/StoreItems/".Length..];
            if (!r.Types.ContainsKey(real)) continue;
            if (cat.EffProductCategory(real) != null) continue;                      // real type already categorised
            if (real.StartsWith("/Lotus/Upgrades/Mods/", StringComparison.Ordinal)
                && !real.Contains("/Randomized/", StringComparison.Ordinal)
                && cat.EffLocalizeTag(real) != null) continue;                       // handled by the mods pass
            var mnm = cat.Display(real);
            if (mnm.Length > 0 && mnm.All(c => c == '?')) continue;
            candidates.Add(new CatalogItem { Path = real, Name = mnm, Category = FriendlyCategory(raw) });
        }

        // Rivens category: one entry per INDIVIDUAL weapon that has a riven Attenuation.
        // (Editing one patches ONLY that weapon's entry via an anchored regex — never the whole type.)
        foreach (var (weaponPath, _) in cat.Rivens)
        {
            var nm = cat.Display(weaponPath);
            if (nm.Length > 0 && nm.All(c => c == '?')) continue;
            candidates.Add(new CatalogItem { Path = weaponPath, Name = nm, Category = "Rivens" });
        }

        // Incarnon category: gathers Incarnon Genesis content that is otherwise scattered or unsurfaced —
        // the evolution PERKS + unlock CHALLENGES (/Upgrades/Evolutions/, which aren't under /Mods/ so they
        // appear in no other category), plus the dedicated Incarnon weapon FORMS (types with "Incarnon" in the
        // path that carry a weapon ProductCategory, e.g. EntFistIncarnon = "Ruvox"). Editing is identical to a
        // normal weapon/mod (same Path) — this is just a curated view. Names use the leaf for perks (unique +
        // descriptive) so same-named perks on different weapons aren't collapsed by the (category,name) dedupe.
        foreach (var (path, t) in r.Types)
        {
            bool isEvo = path.StartsWith("/Lotus/Upgrades/Evolutions/", StringComparison.Ordinal);
            bool isForm = path.Contains("Incarnon", StringComparison.OrdinalIgnoreCase)
                          && IncarnonWeaponCats.Contains(cat.EffProductCategory(path) ?? "");
            if (!isEvo && !isForm) continue;
            string nm = isForm ? cat.Display(path) : PrettyLeaf(Leaf(path));
            if (nm.Length == 0 || nm.All(c => c == '?')) continue;
            candidates.Add(new CatalogItem { Path = path, Name = nm, Category = "Incarnon" });
        }

        // Dedupe each (category, name) to the single most-canonical variant
        // (drops AI/enemy/NPC/base clones that share a display name).
        var best = new Dictionary<(string, string), CatalogItem>();
        foreach (var it in candidates)
        {
            var key = (it.Category, it.Name);
            if (!best.TryGetValue(key, out var cur) || VariantPenalty(it.Path) < VariantPenalty(cur.Path))
                best[key] = it;
        }

        foreach (var it in best.Values)
        {
            if (!cat.ByCategory.TryGetValue(it.Category, out var list)) cat.ByCategory[it.Category] = list = new();
            list.Add(it);
            cat.ItemCount++;
        }
        foreach (var list in cat.ByCategory.Values)
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return cat;
    }

    // Lower = more canonical. Penalise enemy/NPC/base clones; shorter path breaks ties.
    static int VariantPenalty(string path)
    {
        var leaf = path[(path.LastIndexOf('/') + 1)..];
        int v = 0;
        if (leaf.StartsWith("AI", StringComparison.Ordinal)) v += 5;
        if (leaf.Contains("Npc", StringComparison.OrdinalIgnoreCase)) v += 4;
        if (leaf.EndsWith("BaseSuit", StringComparison.Ordinal)) v += 3;
        if (leaf.Contains("Enemy", StringComparison.OrdinalIgnoreCase)) v += 3;
        return v * 10000 + path.Length;
    }

    // Raw ProductCategory -> human-friendly, grouped category name.
    static readonly Dictionary<string, string> CatMap = new(StringComparer.Ordinal)
    {
        ["Suits"] = "Warframes", ["MechSuits"] = "Necramechs", ["OperatorSuits"] = "Operator Suits", ["SpaceSuits"] = "Archwing",
        ["LongGuns"] = "Primary Weapons", ["DrifterGuns"] = "Primary Weapons",
        ["Pistols"] = "Secondary Weapons", ["Melee"] = "Melee Weapons", ["DrifterMelee"] = "Melee Weapons",
        ["SpaceGuns"] = "Archwing Weapons", ["SpaceMelee"] = "Archwing Weapons",
        ["SentinelWeapons"] = "Companion Weapons", ["DataKnives"] = "Parazon", ["OperatorAmps"] = "Amps",
        ["Sentinels"] = "Companions", ["KubrowPets"] = "Companions", ["MoaPets"] = "Companions", ["Drones"] = "Companions",
        ["KubrowPetPrints"] = "Companion Imprints", ["CrewMembers"] = "Railjack Crew",
        ["CrewShipWeapons"] = "Railjack Weapons", ["CrewShipWeaponSkins"] = "Railjack Skins",
        ["CrewShipAmmo"] = "Railjack", ["CrewShips"] = "Railjack", ["CrewShipHarnesses"] = "Railjack",
        ["Upgrades"] = "Mods", ["Cards"] = "Mods", ["FocusUpgrades"] = "Focus",
        ["Recipes"] = "Blueprints", ["Consumables"] = "Gear & Consumables", ["Gadgets"] = "Gear & Consumables", ["Boosters"] = "Boosters",
        ["WeaponSkins"] = "Skins & Cosmetics", ["ShipDecorations"] = "Decorations", ["FlavourItems"] = "Cosmetics",
        ["MiscItems"] = "Misc Items", ["SpecialItems"] = "Special Items", ["SolarRails"] = "Misc Items",
        ["SlotItems"] = "Misc Items", ["EmailItems"] = "Misc Items", ["LeagueTickets"] = "Misc Items",
        ["Packages"] = "Bundles", ["FusionBundles"] = "Bundles", ["CreditBundles"] = "Bundles", ["SupplyDrop"] = "Bundles",
        ["FusionTreasures"] = "Ayatan & Fusion", ["Antiques"] = "Ayatan & Fusion",
        ["LevelKeys"] = "Mission Keys", ["QuestKeys"] = "Quest Keys", ["Quests"] = "Quests",
        ["Ships"] = "Landing Craft", ["Scoops"] = "Vehicles", ["Hoverboards"] = "Vehicles", ["Motorcycles"] = "Vehicles", ["Horses"] = "Vehicles",
    };

    static string FriendlyCategory(string raw) => CatMap.GetValueOrDefault(raw, raw);

    // Raw ProductCategory values that count as a weapon (used to pick out real Incarnon weapon FORMS
    // from the many Incarnon-named FX/animation assets, which have no weapon ProductCategory).
    static readonly HashSet<string> IncarnonWeaponCats = new(StringComparer.Ordinal)
    {
        "Melee", "Pistols", "LongGuns", "SpaceGuns", "SpaceMelee",
        "DrifterGuns", "DrifterMelee", "SentinelWeapons", "OperatorAmps", "DataKnives",
    };

    // "EvoDaggerAttackSpeed" -> "Dagger Attack Speed": strip the Evo prefix + split camelCase.
    static string PrettyLeaf(string leaf)
    {
        var s = leaf.StartsWith("Evo", StringComparison.Ordinal) ? leaf[3..] : leaf;
        var sb = new System.Text.StringBuilder(s.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && char.IsUpper(s[i]) && (!char.IsUpper(s[i - 1]) || (i + 1 < s.Length && char.IsLower(s[i + 1]))))
                sb.Append(' ');
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    static string Leaf(string path) { int i = path.LastIndexOf('/'); return i < 0 ? path : path[(i + 1)..]; }

    /// <summary>Localized display name via LocalizeTag, else the internal leaf name.</summary>
    string Display(string path)
    {
        if (_names != null)
        {
            var tag = EffLocalizeTag(path);
            if (tag != null && _names.TryGetValue(tag, out var nm) && nm.Length > 0) return nm;
        }
        return Leaf(path);
    }

    // Base types that mark a real weapon projectile (excludes FX like ParticleSys/Spawner/Sequencer).
    static readonly HashSet<string> ProjectileBases = new(StringComparer.Ordinal)
    {
        "/EE/Types/Engine/Projectile", "/EE/Types/Engine/GuidedProjectile",
        "/Lotus/Types/Weapons/GunProjectile", "/Lotus/Types/Weapon/WaveProjectile",
        "/Lotus/Weapons/Tenno/Grenades/GrenadeProjectile", "/Lotus/Types/Game/ClusterBombProjectile",
    };
    readonly Dictionary<string, bool> _projCache = new(StringComparer.Ordinal);

    /// <summary>True if the type's inheritance chain descends from a projectile base, memoized.</summary>
    bool EffIsProjectile(string path)
    {
        if (_projCache.TryGetValue(path, out var c)) return c;
        _projCache[path] = false;
        bool res = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (string? p = path; p != null && _types.ContainsKey(p) && seen.Add(p); p = _types[p].Parent)
            if (ProjectileBases.Contains(p)) { res = true; break; }
        _projCache[path] = res;
        return res;
    }

    /// <summary>Effective LocalizeTag (own, else inherited), memoized.</summary>
    string? EffLocalizeTag(string path)
    {
        if (_tagCache.TryGetValue(path, out var c)) return c;
        _tagCache[path] = null;
        string? res = null;
        if (_types.TryGetValue(path, out var t))
        {
            res = TopScalar(t.OwnText, "LocalizeTag");
            if (res == null && t.Parent.Length > 0 && _types.ContainsKey(t.Parent))
                res = EffLocalizeTag(t.Parent);
        }
        _tagCache[path] = res;
        return res;
    }

    /// <summary>Effective ProductCategory (own, else inherited), memoized.</summary>
    string? EffProductCategory(string path)
    {
        if (_pcCache.TryGetValue(path, out var c)) return c;
        _pcCache[path] = null;                                // cycle guard
        string? res = null;
        if (_types.TryGetValue(path, out var t))
        {
            res = TopScalar(t.OwnText, "ProductCategory");
            if (res == null && t.Parent.Length > 0 && _types.ContainsKey(t.Parent))
                res = EffProductCategory(t.Parent);
        }
        _pcCache[path] = res;
        return res;
    }

    /// <summary>Compose full metadata text (own + inherited, child overrides), in dump format (">path" + fields).</summary>
    public string ComposedText(string path)
    {
        if (_composed.TryGetValue(path, out var c)) return c;
        var chain = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        for (string? p = path; p != null && _types.ContainsKey(p) && seen.Add(p); p = _types[p].Parent) chain.Add(p);

        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        var order = new List<string>();
        for (int k = chain.Count - 1; k >= 0; k--)            // root -> self, self overrides
            foreach (var (key, full) in TopFields(_types[chain[k]].OwnText))
            {
                if (!merged.ContainsKey(key)) order.Add(key);
                merged[key] = full;
            }

        var sb = new StringBuilder().Append('>').Append(path).Append('\n');
        foreach (var key in order) sb.Append(merged[key]).Append('\n');
        var text = sb.ToString();
        _composed[path] = text;
        return text;
    }

    public string Parent(string path) => _types.TryGetValue(path, out var t) ? t.Parent : "";

    // ---------------- full offline dump ----------------

    record DumpRaw(string path, string parent, string ownText);
    record DumpItem(string path, string name, string category, string metadata);

    /// <summary>Export the entire decoded dataset to a folder. Returns (types, items, names) written.</summary>
    public (int types, int items, int names) DumpTo(string folder)
    {
        Directory.CreateDirectory(folder);
        var opts = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        var utf8 = new UTF8Encoding(false);

        int types = 0;
        using (var w = new StreamWriter(Path.Combine(folder, "Packages.jsonl"), false, utf8))
            foreach (var (path, t) in _types)
                if (t.OwnText != null) { w.WriteLine(JsonSerializer.Serialize(new DumpRaw(path, t.Parent, t.OwnText), opts)); types++; }

        int items = 0;
        using (var w = new StreamWriter(Path.Combine(folder, "Effective.jsonl"), false, utf8))
            foreach (var list in ByCategory.Values)
                foreach (var it in list) { w.WriteLine(JsonSerializer.Serialize(new DumpItem(it.Path, it.Name, it.Category, ComposedText(it.Path)), opts)); items++; }

        int names = 0;
        if (_names != null)
        {
            File.WriteAllText(Path.Combine(folder, "Names.en.json"), JsonSerializer.Serialize(_names, opts), utf8);
            names = _names.Count;
        }

        File.WriteAllText(Path.Combine(folder, "README.txt"), utf8.GetString(utf8.GetBytes(
            "Offline Warframe metadata dump (decoded from Cache.Windows -- no game running).\n\n" +
            $"Packages.jsonl   every type's raw OWN property text  {{path, parent, ownText}}   ({types} types)\n" +
            $"Effective.jsonl  composed metadata (own + inherited) per editable item  {{path, name, category, metadata}}   ({items} items)\n" +
            $"Names.en.json    LocalizeTag -> localized name   ({names} names)\n")));

        return (types, items, names);
    }

    // ---- lightweight top-level field helpers (brace-balance tracked) ----

    static int Count(string s, char c) { int n = 0; foreach (var ch in s) if (ch == c) n++; return n; }

    /// <summary>First top-level scalar "key=value" -> value, or null.</summary>
    static string? TopScalar(string? text, string key)
    {
        if (text == null) return null;
        int depth = 0;
        foreach (var raw in text.Split('\n'))
        {
            var s = raw.Trim();
            if (depth == 0 && s.StartsWith(key + "=", StringComparison.Ordinal))
            {
                var val = s[(key.Length + 1)..];
                if (Count(val, '{') - Count(val, '}') <= 0) return val;
            }
            depth += Count(s, '{') - Count(s, '}');
            if (depth < 0) depth = 0;
        }
        return null;
    }

    /// <summary>The full text of one top-level block field (e.g. "ItemCompatibilities={...}"), or null.</summary>
    static string? TopBlock(string? text, string key)
    {
        foreach (var (k, full) in TopFields(text)) if (k == key) return full;
        return null;
    }

    /// <summary>All top-level fields: key -> full field text (scalar or whole {block}).</summary>
    static IEnumerable<(string key, string full)> TopFields(string? text)
    {
        if (text == null) yield break;
        var lines = text.Split('\n');
        int i = 0;
        while (i < lines.Length)
        {
            var s = lines[i].TrimEnd('\r');
            var st = s.Trim();
            int eq = st.IndexOf('=');
            if (st.Length == 0 || st[0] == '>' || eq <= 0) { i++; continue; }
            string key = st[..eq], val = st[(eq + 1)..];
            int net = Count(val, '{') - Count(val, '}');
            if (net <= 0) { yield return (key, key + "=" + val); i++; }
            else
            {
                var buf = new StringBuilder(s); int depth = net; i++;
                while (i < lines.Length && depth > 0)
                {
                    var rr = lines[i].TrimEnd('\r'); buf.Append('\n').Append(rr);
                    depth += Count(rr, '{') - Count(rr, '}'); i++;
                }
                yield return (key, buf.ToString());
            }
        }
    }
}
