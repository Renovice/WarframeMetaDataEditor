using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using MetadataPatchEditor.Core;
using Microsoft.Win32;

namespace MetadataPatchEditor;

public partial class MainWindow : Window
{
    MetadataCatalog? _catalog;
    string? _patchesFolder;
    string _category = "";
    string _itemPath = "";
    List<CatalogItem> _all = new();
    List<string> _catNames = new();
    List<KeyValuePair<string, List<CatalogItem>>> _orderedCats = new();

    // Categories shown when "Gameplay only" is ticked (everything else = cosmetics/blueprints/misc).
    static readonly HashSet<string> Gameplay = new(StringComparer.Ordinal)
    {
        "Warframes", "Necramechs", "Operator Suits", "Archwing",
        "Primary Weapons", "Secondary Weapons", "Melee Weapons", "Archwing Weapons",
        "Companion Weapons", "Amps", "Parazon",
        "Mods", "Focus", "Rivens", "Arcanes", "Incarnon", "Amp Parts",
        "Companions", "Railjack Crew", "Railjack Weapons", "Railjack",
        "Projectiles", "Gear & Consumables", "Boosters", "Vehicles",
    };
    readonly List<FieldRow> _allRows = new();
    readonly ObservableCollection<FieldRow> _rows = new();
    FieldRow? _rivenStrengthRow, _rivenDotsRow;   // current Rivens selection's two rows

    public MainWindow()
    {
        InitializeComponent();
        FieldGrid.ItemsSource = _rows;
        _patchesFolder = Catalog.FindPatchesFolder(AppContext.BaseDirectory);
        Status.Text = "Click “Decode from Cache…” to load every game type offline (~2s).";
    }

    async void DecodeBtn_Click(object sender, RoutedEventArgs e)
    {
        // Always let the user pick from Explorer; just default the dialog to the auto-detected install.
        string? auto = Cache.FindCacheWindows(AppContext.BaseDirectory);
        var dlg = new OpenFolderDialog
        {
            Title = "Select your Warframe folder (or its Cache.Windows)",
            InitialDirectory = auto != null ? (Directory.GetParent(auto)?.FullName ?? auto) : Cache.BestGuessStart()
        };
        if (dlg.ShowDialog() != true) return;
        string? cacheDir = Cache.NormalizeToCacheWindows(dlg.FolderName);
        if (cacheDir == null)
        {
            Status.Text = "No Cache.Windows found there (missing H.Misc.toc). Pick your Warframe install folder, or its Cache.Windows.";
            return;
        }
        DecodeBtn.IsEnabled = false;
        Status.Text = "Decoding " + cacheDir + " …";
        try
        {
            var cat = await Task.Run(() =>
            {
                var r = PackagesBinDecoder.DecodeBytes(Cache.ExtractPackagesBin(cacheDir));
                Dictionary<string, string>? names = null;
                try { names = LanguagesBin.Decode(Cache.ExtractLanguagesBin(cacheDir, "en")); }
                catch { /* names optional; fall back to internal names */ }
                return MetadataCatalog.Build(r, names);
            });
            _catalog = cat;
            _orderedCats = cat.ByCategory.OrderByDescending(kv => kv.Value.Count).ToList();
            Status.Text = $"Loaded {cat.ItemCount:N0} items in {cat.ByCategory.Count} categories. " +
                          (_patchesFolder != null ? "Patches → " + _patchesFolder
                                                  : "(choose patch location on save)");
            PopulateCategories();
        }
        catch (Exception ex) { Status.Text = "Decode failed: " + ex.Message; }
        finally { DecodeBtn.IsEnabled = true; }
    }

    async void DumpBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_catalog == null) { Status.Text = "Decode from cache first, then dump."; return; }
        var dlg = new OpenFolderDialog { Title = "Choose a folder to dump all decoded metadata into" };
        if (dlg.ShowDialog() != true) return;
        var folder = dlg.FolderName;
        DumpBtn.IsEnabled = false;
        Status.Text = "Dumping all metadata to " + folder + " …";
        try
        {
            var (types, items, names) = await Task.Run(() => _catalog!.DumpTo(folder));
            Status.Text = $"Dumped {types:N0} types + {items:N0} items + {names:N0} names → {folder}";
        }
        catch (Exception ex) { Status.Text = "Dump failed: " + ex.Message; }
        finally { DumpBtn.IsEnabled = true; }
    }

    void PopulateCategories()
    {
        if (_catalog == null) return;
        bool gpOnly = GameplayCheck.IsChecked == true;
        var shown = _orderedCats.Where(kv => !gpOnly || Gameplay.Contains(kv.Key)).ToList();
        _catNames = shown.Select(kv => kv.Key).ToList();
        CategoryCombo.ItemsSource = shown.Select(kv => $"{kv.Key}  ({kv.Value.Count})").ToList();
        if (CategoryCombo.Items.Count > 0) CategoryCombo.SelectedIndex = 0;
    }

    void GameplayCheck_Changed(object sender, RoutedEventArgs e) => PopulateCategories();

    void Category_Changed(object sender, SelectionChangedEventArgs e)
    {
        int i = CategoryCombo.SelectedIndex;
        if (_catalog == null || i < 0 || i >= _catNames.Count) return;
        _category = _catNames[i];
        _all = _catalog.ByCategory[_category];
        ListLabel.Text = _category;
        _allRows.Clear(); _rows.Clear(); Preview.Clear(); _itemPath = ""; PathText.Text = "Pick an item.";
        ApplyItemFilter();
    }

    void ApplyItemFilter()
    {
        var q = (SearchBox.Text ?? "").Trim();
        ItemList.Items.Clear();
        foreach (var it in _all)
            if (q.Length == 0 || it.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                ItemList.Items.Add(it.Name);
        CountText.Text = $"{ItemList.Items.Count} / {_all.Count}";
    }

    void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyItemFilter();
    void FieldFilter_TextChanged(object sender, TextChangedEventArgs e) => ApplyFieldFilter();

    void ApplyFieldFilter()
    {
        var q = (FieldFilter.Text ?? "").Trim();
        _rows.Clear();
        foreach (var r in _allRows)
            if (q.Length == 0 || r.Key.Contains(q, StringComparison.OrdinalIgnoreCase))
                _rows.Add(r);
    }

    void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _allRows.Clear(); _rows.Clear(); Preview.Clear(); _itemPath = "";
        _rivenStrengthRow = null; _rivenDotsRow = null; RivenReadout.Text = "";
        if (_catalog == null || ItemList.SelectedItem is not string name) return;
        var item = _all.FirstOrDefault(i => i.Name == name);
        if (item == null) return;

        if (_category == "Rivens")
        {
            if (_catalog.Rivens.TryGetValue(item.Path, out var rv))
            {
                _itemPath = item.Path;   // rows carry explicit Target; this is just a reference
                PathText.Text = $"{name}   riven   →   strength in {rv.ModPath.Split('/').Last()}, dots on the weapon";

                // Real uncapped strength — anchored to THIS weapon, patches the riven mod.
                _rivenStrengthRow = new FieldRow { Key = "Riven Strength (Attenuation)", Path = rv.WeaponTail, Op = "riven",
                                                   Target = rv.ModPath, Original = rv.Attenuation, Value = rv.Attenuation };
                _allRows.Add(_rivenStrengthRow);

                // Cosmetic dots (0.5–1.55) — patches the weapon itself.
                var omega = DumpParser.ParseText(_catalog.ComposedText(item.Path)).TopLevel.FirstOrDefault(f => f.Key == "OmegaAttenuation");
                var oval = omega != null ? PatchGenerator.Compact(omega.RawValue) : "1.55";
                _rivenDotsRow = new FieldRow { Key = "Riven Dots (OmegaAttenuation)", Path = "(weapon)", Op = "Field",
                                               Target = item.Path, Original = oval, Value = oval };
                _allRows.Add(_rivenDotsRow);

                _rivenStrengthRow.PropertyChanged += RivenRowChanged;
                _rivenDotsRow.PropertyChanged += RivenRowChanged;
                UpdateRivenReadout();

                PatchName.Text = name.Replace(" ", "") + "Riven.txt";
                Status.Text = $"Strength = real uncapped multiplier (anchored to {name}); Dots = cosmetic 0.5–1.55. Both edit ONLY this weapon.";
                ApplyFieldFilter();
            }
            else Status.Text = "No riven entry found for this weapon.";
            return;
        }

        try
        {
            string text = _catalog.ComposedText(item.Path);
            var d = DumpParser.ParseText(text);
            _itemPath = d.Path;
            PathText.Text = $"{name}    →    {d.Path}";

            // Warframe base stats live as top-level scalars; give them friendly labels (gated to powersuits so
            // no other category gets mislabeled). Emit still uses the real field name — Display is display-only.
            bool isWarframe = d.Path.Contains("/Powersuits/")
                || d.TopLevel.Any(f => f.Key == "ProductCategory" && f.RawValue.Trim() == "Suits");
            bool isIncarnon = d.Path.Contains("/Evolutions/", StringComparison.Ordinal)
                || d.Path.Contains("Incarnon", StringComparison.OrdinalIgnoreCase);
            foreach (var f in d.TopLevel)
            {
                if (f.Kind == FieldKind.ComplexBlock) continue;
                var v = PatchGenerator.Compact(f.RawValue);
                string? disp = isWarframe  && WarframeStatLabels.TryGetValue(f.Key, out var wl) ? wl
                             : isIncarnon  && EvolutionLabels.TryGetValue(f.Key, out var el)   ? el
                             : null;
                _allRows.Add(new FieldRow { Key = f.Key, Display = disp,
                    Path = disp != null ? f.Key : "(top-level)", Op = "Field", Original = v, Value = v });
            }
            foreach (var lf in Extract.FlattenAllText(text))
                if (!lf.TopLevel)
                    _allRows.Add(new FieldRow { Key = lf.Key, Path = lf.Anchor, Op = "s|", Original = lf.Value, Value = lf.Value });
            var up = d.TopLevel.FirstOrDefault(f => f.Key == "Upgrades");
            if (up != null)
                foreach (var u in Extract.Upgrades(up.RawValue, "Upgrades"))
                {
                    // u.Path = "Upgrades.{idx}.{field}"; give the two levers the user cares about clear names.
                    // (Key is display-only for q| rows — the patch line uses Path — so relabelling is safe.)
                    var parts = u.Path.Split('.');
                    int idx = parts.Length >= 2 && int.TryParse(parts[1], out var pi) ? pi : 0;
                    string label = u.Key switch
                    {
                        "Value"         => $"Gain per rank · stat {idx}",
                        "OperationType" => $"Operation / formula · stat {idx}",
                        "UpgradeType"   => $"Affects (stat) · stat {idx}",
                        _               => u.Path,
                    };
                    _allRows.Add(new FieldRow { Key = label, Path = u.Path, Op = "q|", Original = u.Value, Value = u.Value });
                }
            // Warframe per-level scaling: LevelUpgrades adds Value × rank to Health/Shield/Energy/Armor.
            var lup = d.TopLevel.FirstOrDefault(f => f.Key == "LevelUpgrades");
            if (lup != null)
                foreach (var u in Extract.StatUpgrades(lup.RawValue, "LevelUpgrades"))
                    if (u.Value != "0")   // skip the inert NONE entry
                        _allRows.Add(new FieldRow { Key = FriendlyStat(u.Key) + " / level", Path = u.Path, Op = "q|", Original = u.Value, Value = u.Value });
            foreach (var a in Extract.NumericArraysText(text).GroupBy(x => x.Key + "=" + x.Value).Select(g => g.First()))
                _allRows.Add(new FieldRow { Key = a.Key + " [per-rank]", Path = a.Anchor + ".*?" + a.Key, Op = "arr", Original = a.Value, Value = a.Value });
            // Fusion tier (max-rank flag). Mods that carry it already appear as a top-level 'FusionLimit' Field row
            // above; mods that DON'T (e.g. Stretch, which defaults to rank 5) get an ADD row here so it can be
            // injected. EXPERIMENTAL: the client cache's FusionLimit does not by itself decide max rank — rank-3 and
            // rank-5 mods are cache-identical (Reach r5 and Quick Return r3 both QA_MEDIUM) — so this may not move
            // the usable max rank. QA_VERY_HIGH⟺rank10 and absent⟺rank5 are the only reliable correlations.
            if (up != null && !d.TopLevel.Any(f => f.Key == "FusionLimit"))
                _allRows.Add(new FieldRow { Key = "FusionLimit", Path = "(add · experimental)", Op = "Field", Original = "(absent)", Value = "(absent)" });

            PatchName.Text = (BatchCheck.IsChecked == true ? _category : name) + ".txt";
            Status.Text = BatchCheck.IsChecked == true
                ? $"{_allRows.Count} fields. BATCH ON: edits apply to all {ItemList.Items.Count} filtered {_category}."
                : $"{_allRows.Count} fields. Filter to find stats; edit values, then Save Patch.";
            ApplyFieldFilter();
        }
        catch (Exception ex) { Status.Text = "Parse error: " + ex.Message; }
    }

    // Format one changed row's patch line. In batch mode the s| op matches ANY current value
    // (per-item values differ), so it applies to every targeted item.
    string RowLine(FieldRow r, bool batch)
    {
        var v = PatchGenerator.Compact(r.Value);
        return r.Op switch
        {
            "riven" => $"    s|(?s)({r.Path}.*?Attenuation)=[0-9.]+|$1={v}",   // individual weapon only
            "q|"  => $"    q|{r.Path}|{v}",
            "arr" => $"    s|(?s)({r.Path})=\\{{[^}}]*\\}}|$1={{{v}}}",
            "s|"  => batch
                ? $"    s|(?s)({r.Path}.*?{r.Key})=[^\\r\\n]*|$1={v}"
                : $"    s|(?s)({r.Path}.*?{r.Key})={Regex.Escape(r.Original)}|$1={v}",
            _     => $"    {r.Key}={v}",
        };
    }

    string BuildPatch()
    {
        var changed = _allRows.Where(r => r.IsChanged).ToList();
        if (changed.Count == 0) return "";

        bool batch = BatchCheck.IsChecked == true && _category != "Rivens";   // rivens are always individual-weapon
        var sb = new StringBuilder();
        if (batch)
        {
            var names = ItemList.Items.Cast<string>().ToHashSet();
            int count = 0;
            foreach (var it in _all)
            {
                if (!names.Contains(it.Name)) continue;
                sb.Append(it.Path).Append('\n');
                foreach (var r in changed) sb.Append(RowLine(r, true)).Append('\n');
                count++;
            }
            Status.Text = $"Batch patch targets {count} items in ‘{_category}’.";
        }
        else
        {
            // group by target type so multi-type edits (e.g. riven strength on the mod + dots on the weapon) each get a block
            foreach (var grp in changed.GroupBy(r => string.IsNullOrEmpty(r.Target) ? _itemPath : r.Target))
            {
                sb.Append(grp.Key).Append('\n');
                foreach (var r in grp) sb.Append(RowLine(r, false)).Append('\n');
            }
        }
        return sb.ToString();
    }

    static readonly string[] ValidOps    = { "STACKING_MULTIPLY", "ADD", "MULTIPLY", "SET", "ADD_BASE" };
    static readonly string[] ValidFusion = { "QA_NONE", "QA_LOW", "QA_MEDIUM", "QA_HIGH", "QA_VERY_HIGH" };

    // Guard the two enum fields: a typo'd OperationType / FusionLimit would be written verbatim and
    // silently no-op in-game. Returns a warning string, or null if all changed enum rows are valid.
    string? ValidateEnumRows()
    {
        foreach (var r in _allRows.Where(r => r.IsChanged))
        {
            var v = PatchGenerator.Compact(r.Value);
            if (r.Key.StartsWith("Operation / formula") && !ValidOps.Contains(v))
                return $"⚠ Operation \"{v}\" isn't valid — use one of: {string.Join(", ", ValidOps)}.";
            if (r.Key == "FusionLimit" && v != "(absent)" && !ValidFusion.Contains(v))
                return $"⚠ FusionLimit \"{v}\" isn't valid — use one of: {string.Join(", ", ValidFusion)}.";
        }
        return null;
    }

    void PreviewBtn_Click(object sender, RoutedEventArgs e)
    {
        var p = BuildPatch();
        Preview.Text = p.Length == 0 ? "(no fields changed yet)" : p;
        var w = ValidateEnumRows();
        if (w != null) Status.Text = w;
    }

    void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        var p = BuildPatch();
        if (p.Length == 0) { Status.Text = "Nothing changed — edit a value first."; return; }
        var w = ValidateEnumRows();
        if (w != null) { Preview.Text = p; Status.Text = w + " — fix it before saving."; return; }
        Preview.Text = p;
        var dlg = new SaveFileDialog
        {
            Filter = "Metadata patch (*.txt)|*.txt",
            FileName = string.IsNullOrWhiteSpace(PatchName.Text) ? "patch.txt" : PatchName.Text,
            InitialDirectory = _patchesFolder ?? AppContext.BaseDirectory
        };
        if (dlg.ShowDialog() == true)
        {
            File.WriteAllText(dlg.FileName, p);
            Status.Text = "Saved: " + dlg.FileName;
        }
    }

    // Raw powersuit base-stat field name -> friendly display label (covers naming variants across frames).
    static readonly Dictionary<string, string> WarframeStatLabels = new(StringComparer.Ordinal)
    {
        ["MaxEnergy"]               = "Base Energy (max)",
        ["MaxPower"]                = "Base Energy (max)",
        ["Power"]                   = "Base Energy (max)",
        ["InitialEnergy"]           = "Starting Energy",
        ["MaxHealthOverride"]       = "Base Health",
        ["MaxHealth"]               = "Base Health",
        ["Health"]                  = "Base Health",
        ["MaxShieldOverride"]       = "Base Shield",
        ["MaxShield"]               = "Base Shield",
        ["Shield"]                  = "Base Shield",
        ["ArmourRatingOverride"]    = "Base Armor",
        ["ArmourRating"]            = "Base Armor",
        ["Armour"]                  = "Base Armor",
        ["MovementSpeedMultiplier"] = "Sprint Speed (×)",
        ["SprintSpeed"]             = "Sprint Speed",
        ["RunSpeed"]                = "Sprint Speed",
    };

    // Incarnon / evolution metadata levers -> friendly labels (gated to Incarnon + /Evolutions/ types).
    // These are the numbers you CAN change in metadata: unlock cost, buff/mode duration, perk stacking &
    // trigger chance, and the condition requirements. (The head-vs-body charge test is Lua, not here.)
    static readonly Dictionary<string, string> EvolutionLabels = new(StringComparer.Ordinal)
    {
        ["RequiredCount"]         = "Unlock requirement (count)",
        ["UpgradeDuration"]       = "Perk buff duration (sec)",   // temp buff a conditional perk grants; NOT the form (form is ammo-gated, no timer)
        ["MaxConditionalStacks"]  = "Perk buff max stacks",
        ["UpgradeChance"]         = "Perk trigger chance (0–1)",
        ["StackMode"]             = "Stack behavior",
        ["IsInfinite"]            = "Infinite duration",
        ["DamageType"]            = "Required damage type",
        ["VictimActiveProcTypes"] = "Required status on target",
        ["Injury"]                = "Required injury type",
        ["AttackerIsAirborne"]    = "Requires airborne",
        ["SoloPlayer"]            = "Solo mission only",
        ["GameTag"]               = "Trigger tag",
        ["XPReward"]              = "XP reward",
        ["RequiredWeapon"]        = "Required weapon",
        ["RequiredLevel"]         = "Required weapon level",
        ["AmmoClipSize"]          = "Incarnon magazine",
        ["AmmoCapacity"]          = "Incarnon reserve ammo",
    };

    static string FriendlyStat(string t) => t switch
    {
        "AVATAR_HEALTH_MAX" => "Health",
        "AVATAR_SHIELD_MAX" => "Shield",
        "AVATAR_POWER_MAX" => "Energy",
        "AVATAR_ARMOUR_MAX" or "AVATAR_ARMOR_MAX" => "Armor",
        "AVATAR_STAMINA_MAX" => "Stamina",
        _ => t.Replace("AVATAR_", "").Replace("_MAX", ""),
    };

    void MultBtn_Click(object sender, RoutedEventArgs e)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (!double.TryParse(MultBox.Text, System.Globalization.NumberStyles.Any, inv, out var n) || n <= 0)
        { Status.Text = "Enter a positive multiplier (e.g. 5)."; return; }

        if (_rivenStrengthRow != null)   // Rivens: scale strength (+ tied dots)
        {
            if (!double.TryParse(_rivenStrengthRow.Original, System.Globalization.NumberStyles.Any, inv, out var baseStr))
            { Status.Text = "Couldn't read the base strength value."; return; }
            _rivenStrengthRow.Value = (baseStr * n).ToString("0.#######", inv);
            if (_rivenDotsRow != null && double.TryParse(_rivenDotsRow.Original, System.Globalization.NumberStyles.Any, inv, out var baseDots))
                _rivenDotsRow.Value = Math.Clamp(baseDots * n, 0.5, 1.55).ToString("0.#######", inv);
            Status.Text = $"×{n}: strength {_rivenStrengthRow.Original} → {_rivenStrengthRow.Value}, dots → {_rivenDotsRow?.Value} (this weapon only).";
        }
        else   // Mods / warframes: multiply every per-rank / per-level Value (q| rows) — scales the whole curve
        {
            int scaled = 0;
            foreach (var r in _allRows.Where(r => r.Op == "q|"))
                if (double.TryParse(r.Original, System.Globalization.NumberStyles.Any, inv, out var b))
                { r.Value = (b * n).ToString("0.#######", inv); scaled++; }
            if (scaled == 0) { Status.Text = "No per-rank/per-level values to scale on this item."; return; }
            Status.Text = $"×{n}: scaled {scaled} per-rank/per-level value(s) — the whole rank curve scales linearly.";
        }
        PreviewBtn_Click(sender, e);
    }

    void RivenRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateRivenReadout();

    void UpdateRivenReadout()
    {
        if (_rivenStrengthRow == null) { RivenReadout.Text = ""; return; }
        var dots = _rivenDotsRow != null ? $"    dots {_rivenDotsRow.Value}" : "";
        RivenReadout.Text = $"strength {_rivenStrengthRow.Original} → {_rivenStrengthRow.Value}{dots}";
    }

    void ResetBtn_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _allRows) r.Value = r.Original;
        Preview.Clear();
        Status.Text = "Reset.";
    }
}

public class FieldRow : INotifyPropertyChanged
{
    public string Key { get; init; } = "";           // the REAL field name / path — used to build the patch line
    public string? Display { get; init; }             // optional friendly label; does NOT affect the emitted patch
    public string Label => Display ?? Key;            // what the grid shows
    public string Path { get; init; } = "";
    public string Op { get; init; } = "Field";
    public string Target { get; init; } = "";   // which type this patch line applies to ("" = the selected item)
    public string Original { get; init; } = "";

    string _value = "";
    public string Value
    {
        get => _value;
        set { if (_value != value) { _value = value; Raise(nameof(Value)); Raise(nameof(IsChanged)); } }
    }
    public bool IsChanged => Value != Original;

    public event PropertyChangedEventHandler? PropertyChanged;
    void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
