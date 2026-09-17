using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
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
    List<KeyValuePair<string, List<CatalogItem>>> _orderedCats = new();
    string _patchPreamble = "";
    PendingUpgradeOverride? _pendingUpgradeOverride;
    PendingServerMetadataOverride? _pendingServerMetadataOverride;
    WeaponDamageComposition? _pendingWeaponDamage;

    // Categories shown when "Gameplay only" is ticked (everything else = cosmetics/blueprints/misc).
    static readonly HashSet<string> Gameplay = new(StringComparer.Ordinal)
    {
        "Warframes", "Necramechs", "Operator Suits", "Archwing",
        "Primary Weapons", "Secondary Weapons", "Melee Weapons", "Archwing Weapons",
        "Companion Weapons", "Amps", "Parazon",
        "Mods", "Focus", "Rivens", "Arcanes", "Incarnon", "Amp Parts", "Kitgun Parts", "Zaw Parts",
        "Companions", "Railjack Crew", "Railjack Weapons", "Railjack",
        "Projectiles", "Gear & Consumables", "Boosters", "Vehicles", "Status Effects",
    };
    readonly List<FieldRow> _allRows = new();
    readonly ObservableCollection<FieldRow> _rows = new();
    FieldRow? _rivenStrengthRow, _rivenDotsRow;   // current Rivens selection's two rows

    public MainWindow()
    {
        InitializeComponent();
        var groupedRows = new ListCollectionView(_rows);
        groupedRows.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FieldRow.Group)));
        FieldGrid.ItemsSource = groupedRows;
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
        // The folder the user explicitly selected is authoritative. Startup's broad sibling search can
        // also see archived Warframe copies, so replace that guess with this install's own OpenWF path.
        var selectedGameRoot = Directory.GetParent(cacheDir)?.FullName;
        var selectedPatches = selectedGameRoot == null ? null : Path.Combine(selectedGameRoot, "OpenWF", "Metadata Patches");
        if (selectedPatches != null && Directory.Exists(selectedPatches)) _patchesFolder = selectedPatches;
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
        var shown = _orderedCats.Where(kv => !gpOnly || IsGameplayCategory(kv.Key)).ToList();
        string previous = _category;
        var itemStyle = (Style)FindResource("DarkTreeItem");
        CategoryTree.Items.Clear();
        foreach (var group in shown.GroupBy(kv => CategoryPresentation(kv.Key).Group)
                     .OrderBy(g => CategoryPresentation(g.First().Key).Order)
                     .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var first = CategoryPresentation(group.First().Key);
            var parent = new TreeViewItem
            {
                Header = $"{first.GroupIcon}  {group.Key}",
                ToolTip = CategoryGroupDescription(group.Key),
                IsExpanded = group.Any(entry => entry.Key == previous),
                Style = itemStyle,
                Foreground = System.Windows.Media.Brushes.White
            };
            parent.PreviewMouseLeftButtonDown += CategoryGroup_PreviewMouseLeftButtonDown;
            foreach (var entry in group.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                var presentation = CategoryPresentation(entry.Key);
                var child = new TreeViewItem
                {
                    Header = $"{presentation.ItemIcon}  {entry.Key}  ({entry.Value.Count})",
                    Tag = entry.Key,
                    ToolTip = CategoryDescription(entry.Key),
                    Style = itemStyle,
                    Foreground = System.Windows.Media.Brushes.White
                };
                parent.Items.Add(child);
            }
            CategoryTree.Items.Add(parent);
        }

        string? selected = shown.Any(kv => kv.Key == previous) ? previous : shown.FirstOrDefault().Key;
        if (!string.IsNullOrEmpty(selected))
        {
            SelectCategory(selected);
            ExpandCategoryGroup(selected);
        }
    }

    void CategoryGroup_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeViewItem group || e.OriginalSource is not DependencyObject source) return;
        // ContainerFromElement(CategoryTree, source) resolves to the root tree's direct child,
        // which is the parent group even when the click originated inside a nested category.
        // Find the nearest TreeViewItem instead so child clicks reach selection unchanged.
        var clickedContainer = FindAncestor<TreeViewItem>(source);
        if (!ReferenceEquals(clickedContainer, group)) return; // the nearest nested category owns this click
        if (FindAncestor<ToggleButton>(source) != null) return; // the native arrow already toggles once
        group.IsExpanded = !group.IsExpanded;
        e.Handled = true;
    }

    static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node != null)
        {
            if (node is T match) return match;
            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        }
        return null;
    }

    void ExpandCategoryGroup(string category)
    {
        foreach (var parent in CategoryTree.Items.OfType<TreeViewItem>())
            if (parent.Items.OfType<TreeViewItem>().Any(child => Equals(child.Tag, category)))
            {
                parent.IsExpanded = true;
                return;
            }
    }

    void GameplayCheck_Changed(object sender, RoutedEventArgs e) => PopulateCategories();

    static bool IsGameplayCategory(string category)
        => Gameplay.Contains(category)
        || category.StartsWith("Mods —", StringComparison.Ordinal)
        || category.StartsWith("Status Effects —", StringComparison.Ordinal);

    void CategoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem { Tag: string category })
        {
            SelectCategory(category);
            CategoryPopup.IsOpen = false;
            CategoryToggle.IsChecked = false;
        }
    }

    void SelectCategory(string category)
    {
        if (_catalog == null || !_catalog.ByCategory.ContainsKey(category)) return;
        _category = category;
        _all = _catalog.ByCategory[_category];
        var presentation = CategoryPresentation(_category);
        CategoryToggle.Content = $"{presentation.ItemIcon}  {_category}  ({_all.Count})";
        ListLabel.Text = _category;
        CategoryHelp.Text = CategoryDescription(_category);
        _allRows.Clear(); _rows.Clear(); Preview.Clear(); _itemPath = ""; _patchPreamble = ""; _pendingUpgradeOverride = null; _pendingServerMetadataOverride = null; _pendingWeaponDamage = null; PathText.Text = "Pick an item.";
        GroupCombo.ItemsSource = new[] { new SectionChoice("All sections", "▦  All sections") };
        GroupCombo.DisplayMemberPath = nameof(SectionChoice.Display);
        GroupCombo.SelectedIndex = 0;
        GameplayMetadataBtn.IsEnabled = false;
        GameplayMetadataBtn.Content = "Open Visual Editor…";
        ModEffectBtn.IsEnabled = false;
        ApplyItemFilter();
    }

    static (string Group, string GroupIcon, string ItemIcon, int Order) CategoryPresentation(string category)
    {
        if (category.StartsWith("Mods", StringComparison.Ordinal))
            return ("Upgrades & progression", "↑", "✦", 2);
        if (category.StartsWith("Status Effects", StringComparison.Ordinal))
            return ("Status & elements", "☄", "◈", 3);
        return category switch
        {
            "Primary Weapons" or "Secondary Weapons" or "Melee Weapons" or "Archwing Weapons"
                or "Companion Weapons" or "Amps" or "Parazon" or "Projectiles"
                => ("Weapons & attacks", "⚔", "◆", 0),
            "Warframes" or "Necramechs" or "Operator Suits" or "Archwing" or "Companions"
                or "Vehicles" or "Railjack Crew"
                => ("Characters & vehicles", "◉", "●", 1),
            "Arcanes" or "Rivens" or "Incarnon" or "Focus" or "Boosters"
                => ("Upgrades & progression", "↑", "✦", 2),
            "Railjack" or "Railjack Weapons" or "Railjack Skins"
                => ("Railjack & space", "✹", "◇", 4),
            "Mission Keys" or "Quest Keys" or "Quests"
                => ("Missions & keys", "⚑", "▸", 5),
            "Blueprints" or "Bundles" or "Gear & Consumables" or "Ayatan & Fusion"
                or "Amp Parts" or "Kitgun Parts" or "Zaw Parts" or "Special Items"
                => ("Items & crafting", "▣", "□", 6),
            "Skins & Cosmetics" or "Cosmetics" or "Decorations" or "Landing Craft"
                or "Companion Imprints"
                => ("Cosmetics & decorations", "✧", "✧", 7),
            _ => ("Other metadata", "☷", "•", 8),
        };
    }

    static string CategoryGroupDescription(string group) => group switch
    {
        "Weapons & attacks" => "Weapons, attack projectiles, fire modes, damage, critical, and status metadata.",
        "Characters & vehicles" => "Playable suits, companions, crew, vehicles, and their base gameplay metadata.",
        "Upgrades & progression" => "Mods, arcanes, Rivens, Incarnon upgrades, Focus, and progression items.",
        "Status & elements" => "Native status-effect handlers, including separate normal-enemy, player, faction, and Railjack behavior.",
        "Railjack & space" => "Railjack-specific objects and space-combat metadata that use their own systems.",
        "Missions & keys" => "Mission, quest, and key definitions.",
        "Items & crafting" => "Blueprints, modular parts, consumables, and crafting-related metadata.",
        "Cosmetics & decorations" => "Appearance-only assets, skins, decorations, and landing-craft cosmetics.",
        _ => "Additional decoded metadata that does not fit a narrower gameplay family."
    };

    static string CategoryDescription(string category)
    {
        if (category.StartsWith("Mods —", StringComparison.Ordinal))
            return $"Editable {category[7..].ToLowerInvariant()} mods: identity, compatibility, upgrade effects, values, localization links, and scripts.";
        if (category.StartsWith("Status Effects —", StringComparison.Ordinal))
            return category switch
            {
                "Status Effects — Ordinary Ground Enemies" => "Shared/default status handlers for ordinary ground enemies. ‘Enemy Base’ is the broad inherited default; immune enemies still reject the proc before this handler can matter.",
                "Status Effects — Player & Tenno" => "Separate handlers for statuses received by player/Tenno avatars. Editing these does not change the same status on ordinary enemies.",
                "Status Effects — Specialized Enemy Overrides" => "Narrow child handlers used by named faction units, bosses, VIPs, or special encounters. A ‘Grineer’ entry here is an override such as Teshin's handler, not automatically every Grineer enemy.",
                "Status Effects — Railjack / Space Combat" => "Separate Railjack/space-combat handlers. These do not control the normal ground-enemy elemental system.",
                _ => "Native status handlers divided by target scope. Select a handler to see its exact inheritance target."
            };
        return CategoryGroupDescription(CategoryPresentation(category).Group);
    }

    void ApplyItemFilter()
    {
        var q = (SearchBox.Text ?? "").Trim();
        ItemList.Items.Clear();
        foreach (var it in _all)
            if (q.Length == 0 || it.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                ItemList.Items.Add(it);
        CountText.Text = $"{ItemList.Items.Count} / {_all.Count}";
    }

    void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyItemFilter();
    void FieldFilter_TextChanged(object sender, TextChangedEventArgs e) => ApplyFieldFilter();

    void ApplyFieldFilter()
    {
        var q = (FieldFilter.Text ?? "").Trim();
        var group = (GroupCombo.SelectedItem as SectionChoice)?.Value ?? "All sections";
        _rows.Clear();
        foreach (var r in _allRows.OrderBy(r => GroupOrder(r.Group)).ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase))
            if ((group == "All sections" || r.Group == group)
                && (q.Length == 0 || r.Key.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || r.Label.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || r.Path.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || r.Description.Contains(q, StringComparison.OrdinalIgnoreCase)))
                _rows.Add(r);
    }

    void GroupCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFieldFilter();

    void AdvancedFieldsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (OperationColumn == null || ExactPathColumn == null) return;
        var visibility = AdvancedFieldsCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        OperationColumn.Visibility = visibility;
        ExactPathColumn.Visibility = visibility;
    }

    static int GroupOrder(string group) => group switch
    {
        "Weapon damage" or "Damage & Status" or "Behavior & stacking" => 0,
        "Damage over time" => 1,
        "Radial damage" => 2,
        "Stack effects" => 3,
        "Upgrades" => 4,
        "Equipment" => 5,
        "Scripting" => 6,
        "General" => 7,
        _ when group.StartsWith("Fire mode", StringComparison.Ordinal) => 0,
        _ => 8,
    };

    void DecorateAndRefreshGroups(string? preferred = null)
    {
        foreach (var row in _allRows)
        {
            string metadataKey = MetadataKey(row);
            row.Group = row.Op switch
            {
                "effect" => "Upgrades",
                "weapon" => "Weapon damage",
                _ => GameplayMetadata.GroupFor(_category, metadataKey, row.Path),
            };
            row.Description = row.Op switch
            {
                "effect" => "Preserves the complete original Upgrades collection and appends the native effects created with Add Mod Effect.",
                "weapon" => "Preserves the complete native weapon block while changing explicit physical and elemental damage components in its selected attack profiles.",
                _ => GameplayMetadata.DescriptionFor(metadataKey, row.Path),
            };
            ConfigureRowEditor(row);
        }
        var groups = _allRows.Select(r => r.Group).Distinct(StringComparer.Ordinal)
            .OrderBy(GroupOrder).ThenBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();
        groups.Insert(0, "All sections");
        var choices = groups.Select(g => new SectionChoice(g, SectionDisplay(g))).ToList();
        GroupCombo.ItemsSource = choices;
        GroupCombo.DisplayMemberPath = nameof(SectionChoice.Display);
        string wanted = preferred != null && groups.Contains(preferred) ? preferred : "All sections";
        GroupCombo.SelectedItem = choices.FirstOrDefault(c => c.Value == wanted);
        if (GroupCombo.SelectedIndex < 0) GroupCombo.SelectedIndex = 0;
    }

    static readonly HashSet<string> BooleanFields = new(StringComparer.Ordinal)
    {
        "ApplyUpgradesByDefault", "AutoType", "CanBlockInjury", "CanRerollPVP",
        "DestroyAvatar", "DisplayAsMultiplier", "DisplayAsPercent",
        "InjuryExitsWallSlide", "IsAutonomous", "IsSpace", "OverrideLocalization", "ReverseValueSymbol",
        "SmallerIsBetter"
    };

    void ConfigureRowEditor(FieldRow row)
    {
        string compact = PatchGenerator.Compact(row.Value);
        string key = MetadataKey(row);
        row.Options = Array.Empty<string>();
        if (BooleanFields.Contains(key) && compact is "0" or "1")
        {
            row.EditorKind = "Boolean";
            return;
        }

        IReadOnlyList<string> options = key switch
        {
            "Rarity" => ["COMMON", "UNCOMMON", "RARE", "LEGENDARY"],
            "FusionLimit" or "BaseDrain" => ["QA_NONE", "QA_LOW", "QA_MEDIUM", "QA_HIGH", "QA_VERY_HIGH"],
            "OperationType" => GameplayMetadata.UpgradeOperations,
            "StackStyle" => GameplayMetadata.StackStyles,
            "ConsolidateDamageOverTime" => GameplayMetadata.DotConsolidationModes,
            "UseHighestDamageOverTime" => GameplayMetadata.HighestDotBasisModes,
            "DamageType" => GameplayMetadata.DamageTypes,
            "Type" when row.Path.Contains("AttackData", StringComparison.Ordinal)
                         || row.Path.Contains("DamageOverTime", StringComparison.Ordinal)
                         || row.Path.Contains("RadialDamage", StringComparison.Ordinal)
                => GameplayMetadata.DamageTypes,
            "ForcedProcs" or "ForcedDeathProcs" => GameplayMetadata.ProcTypes,
            _ => _catalog?.EnumOptions(key, compact) ?? Array.Empty<string>()
        };
        if (options.Count >= 2)
        {
            row.Options = options.Contains(compact, StringComparer.Ordinal)
                ? options
                : options.Prepend(compact).Where(v => v.Length > 0).Distinct(StringComparer.Ordinal).ToList();
            row.EditorKind = "Enum";
            return;
        }

        row.EditorKind = decimal.TryParse(compact, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            ? "Number" : "Text";
    }

    static string MetadataKey(FieldRow row)
    {
        if (row.Op != "q|") return row.Key;
        int dot = row.Path.LastIndexOf('.');
        return dot >= 0 && dot + 1 < row.Path.Length ? row.Path[(dot + 1)..] : row.Key;
    }

    static string SectionDisplay(string group)
    {
        string icon = group switch
        {
            "All sections" => "▦",
            "Behavior & stacking" => "⟳",
            "Weapon damage" => "⚔",
            "Damage over time" => "◷",
            "Radial damage" => "◎",
            "Stack effects" => "☷",
            "Upgrades" => "↑",
            "Equipment" => "◆",
            "Scripting" => "⌘",
            "General" => "•",
            _ when group.StartsWith("Fire mode", StringComparison.Ordinal) => "⚔",
            _ => "◇",
        };
        return $"{icon}  {group}";
    }

    void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _allRows.Clear(); _rows.Clear(); Preview.Clear(); _itemPath = ""; _patchPreamble = ""; _pendingUpgradeOverride = null; _pendingServerMetadataOverride = null; _pendingWeaponDamage = null;
        _rivenStrengthRow = null; _rivenDotsRow = null; RivenReadout.Text = "";
        GameplayMetadataBtn.IsEnabled = false;
        GameplayMetadataBtn.Content = "Open Visual Editor…";
        ModEffectBtn.IsEnabled = false;
        if (_catalog == null || ItemList.SelectedItem is not CatalogItem item) return;
        string name = item.Name;

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
                DecorateAndRefreshGroups();
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
            bool isStatusHandler = _category.StartsWith("Status Effects", StringComparison.Ordinal);
            bool isWeapon = GameplayMetadata.IsWeaponCategory(_category);
            if (isStatusHandler)
                CategoryHelp.Text = CategoryDescription(_category) + "\n\n" + GameplayMetadata.StatusScopeDescription(d.Path);
            foreach (var f in d.TopLevel)
            {
                // Slot/compatibility sets are first-class equipment fields. A top-level prepend
                // override accepts the complete compact block and is safer than an unanchored regex.
                if (f.Kind == FieldKind.ComplexBlock)
                {
                    if (f.Key is "ArtifactSlots" or "AdditionalBaseModTypes" or "CompatibilityTags" or "IncompatibilityTags")
                    {
                        var values = EquipmentExperiments.ReadSet(text, f.Key);
                        _allRows.Add(new FieldRow
                        {
                            Key = f.Key,
                            Display = f.Key switch
                            {
                                "ArtifactSlots" => $"Equipment slot layout ({values.Count} entries)",
                                "AdditionalBaseModTypes" => "Additional accepted mod base types",
                                _ => f.Key,
                            },
                            Path = "(top-level set)", Op = "Field",
                            Original = EquipmentExperiments.FormatSet(values),
                            Value = EquipmentExperiments.FormatSet(values),
                        });
                    }
                    continue;
                }
                var v = PatchGenerator.Compact(f.RawValue);
                string? disp = _category.StartsWith("Mods", StringComparison.Ordinal) && f.Key == "Rarity" ? "Visible mod rarity"
                             : _category.StartsWith("Mods", StringComparison.Ordinal) && f.Key == "FusionLimit" ? "Coarse cache fusion tier (not exact rank)"
                             : (isStatusHandler || isWeapon) ? GameplayMetadata.DisplayFor(f.Key, f.Key)
                             : isWarframe  && WarframeStatLabels.TryGetValue(f.Key, out var wl) ? wl
                             : isIncarnon  && EvolutionLabels.TryGetValue(f.Key, out var el)   ? el
                             : null;
                _allRows.Add(new FieldRow { Key = f.Key, Display = disp,
                    Path = disp != null ? f.Key : "(top-level)", Op = "Field", Original = v, Value = v });
            }
            if (isStatusHandler || isWeapon)
            {
                var guidedDefinitions = (isStatusHandler
                        ? GameplayMetadata.BuildStatusFields(text)
                        : GameplayMetadata.BuildWeaponFields(text));
                foreach (var definition in guidedDefinitions.Where(f => f.TopLevel)
                    .Where(f => !_allRows.Any(r => r.Op == "Field" && r.Key == f.Key)))
                {
                    _allRows.Add(new FieldRow
                    {
                        Key = definition.Key,
                        Display = definition.Display,
                        Path = "(optional top-level field)",
                        Op = "Field",
                        Original = definition.Original,
                        Value = definition.Original,
                    });
                }
                var guidedPaths = guidedDefinitions
                    .Where(f => !f.TopLevel)
                    .GroupBy(f => f.Path, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                foreach (var lf in Extract.QueryableText(text).Where(f => !f.TopLevel && guidedPaths.ContainsKey(f.Path)))
                {
                    var definition = guidedPaths[lf.Path];
                    _allRows.Add(new FieldRow
                    {
                        Key = lf.Key,
                        Display = definition.Display,
                        Path = lf.Path,
                        Op = "q|",
                        Original = lf.Value,
                        Value = lf.Value,
                    });
                }
            }
            else
            {
                foreach (var lf in Extract.FlattenAllText(text))
                    if (!lf.TopLevel)
                        _allRows.Add(new FieldRow { Key = lf.Key, Path = lf.Anchor, Op = "s|", Original = lf.Value, Value = lf.Value });
            }
            var up = d.TopLevel.FirstOrDefault(f => f.Key == "Upgrades");
            ModEffectBtn.IsEnabled = up != null && _category.StartsWith("Mods", StringComparison.Ordinal);
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
            bool supportsWarframeEditor = isWarframe && string.Equals(_category, "Warframes", StringComparison.Ordinal);
            GameplayMetadataBtn.IsEnabled = isStatusHandler || isWeapon || supportsWarframeEditor;
            GameplayMetadataBtn.Content = isStatusHandler ? "Open Visual Status Editor…"
                : isWeapon ? "Open Visual Weapon Editor…"
                : "Open Visual Warframe Editor…";
            DecorateAndRefreshGroups(isStatusHandler ? "Behavior & stacking" : null);
            ApplyFieldFilter();
        }
        catch (Exception ex) { Status.Text = "Parse error: " + ex.Message; }
    }

    void ItemList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (GameplayMetadataBtn.IsEnabled)
            GameplayMetadataBtn_Click(GameplayMetadataBtn, new RoutedEventArgs(Button.ClickEvent));
    }

    void GameplayMetadataBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_catalog == null || string.IsNullOrEmpty(_itemPath) || ItemList.SelectedItem is not CatalogItem item)
        {
            Status.Text = "Decode the cache and select a status effect, weapon, or Warframe first.";
            return;
        }

        bool isStatus = _category.StartsWith("Status Effects", StringComparison.Ordinal);
        bool isWeapon = GameplayMetadata.IsWeaponCategory(_category);
        bool isWarframe = string.Equals(_category, "Warframes", StringComparison.Ordinal);
        if (!isStatus && !isWeapon && !isWarframe)
        {
            Status.Text = "The visual gameplay editor is available for Status Effects, weapons, and Warframes.";
            return;
        }

        try
        {
            string text = _catalog.ComposedText(_itemPath);
            if (isWarframe)
            {
                var composition = WarframeStatsComposition.Parse(text);
                if (composition.BaseStats.Count == 0)
                {
                    Status.Text = "This Warframe exposes no supported native base-stat fields.";
                    return;
                }

                foreach (var stat in composition.BaseStats)
                {
                    var pending = _allRows.FirstOrDefault(row => row.Op == "Field" && row.Key == stat.Key);
                    if (pending?.IsChanged == true) stat.Value = pending.Value;
                }
                foreach (var stat in composition.RankStats)
                {
                    decimal total = 0;
                    foreach (var milestone in stat.Milestones)
                    {
                        var pending = _allRows.FirstOrDefault(row => row.Op == "q|" && row.Path == milestone.Path);
                        string value = pending?.IsChanged == true ? pending.Value : milestone.Original.ToString(CultureInfo.InvariantCulture);
                        if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed))
                            throw new InvalidDataException($"{stat.Label}: pending milestone {milestone.Path} is not numeric.");
                        total += parsed;
                    }
                    stat.Value = total.ToString("0.#########", CultureInfo.InvariantCulture);
                }

                var warframeDialog = new WarframeStatsWindow(item.Name, composition) { Owner = this };
                if (warframeDialog.ShowDialog() != true) return;

                // Reset this visual editor's owned rows first so choosing stock values also
                // clears an earlier unsaved visual edit.
                foreach (var stat in composition.BaseStats)
                {
                    var row = _allRows.FirstOrDefault(candidate => candidate.Op == "Field" && candidate.Key == stat.Key);
                    if (row is not null) row.Value = row.Original;
                }
                foreach (var milestone in composition.RankStats.SelectMany(stat => stat.Milestones))
                {
                    var row = _allRows.FirstOrDefault(candidate => candidate.Op == "q|" && candidate.Path == milestone.Path);
                    if (row is not null) row.Value = row.Original;
                }

                foreach (var changed in warframeDialog.ChangedFields)
                {
                    string op = changed.TopLevel ? "Field" : "q|";
                    var row = _allRows.FirstOrDefault(candidate => candidate.Op == op
                        && (changed.TopLevel ? candidate.Key == changed.Key : candidate.Path == changed.Path));
                    if (row is null)
                    {
                        row = new FieldRow
                        {
                            Key = changed.Key,
                            Display = changed.Display,
                            Path = changed.TopLevel ? "(top-level)" : changed.Path,
                            Op = op,
                            Original = changed.Original,
                            Value = changed.Original,
                        };
                        _allRows.Add(row);
                    }
                    row.Value = changed.Value;
                }

                DecorateAndRefreshGroups("Warframe stats");
                ApplyFieldFilter();
                PreviewBtn_Click(sender, e);
                Status.Text = $"Applied {warframeDialog.ChangedFields.Count} exact Warframe stat field change(s). Review the rank preview, then save the patch.";
                return;
            }

            if (isWeapon)
            {
                var working = (_pendingWeaponDamage ?? WeaponDamageComposition.Parse(text)).Clone();
                if (working.Profiles.Count == 0)
                {
                    Status.Text = "This weapon exposes no supported AttackData, AlternateAttackData, radial-damage, or damage-over-time profile.";
                    return;
                }

                var weaponDialog = new WeaponDamageWindow(item.Name, working) { Owner = this };
                if (weaponDialog.ShowDialog() != true) return;
                _pendingWeaponDamage = working;

                foreach (string rootKey in weaponDialog.ChangedBlocks.Keys)
                {
                    _allRows.RemoveAll(row => row.Op == "weapon" && row.Key == rootKey);
                    _allRows.RemoveAll(row => row.Op == "q|"
                        && (row.Path == rootKey || row.Path.StartsWith(rootKey + ".", StringComparison.Ordinal)));
                    _allRows.Add(new FieldRow
                    {
                        Key = rootKey,
                        Display = "Composed weapon damage profiles",
                        Path = $"(complete preserved {rootKey} block)",
                        Op = "weapon",
                        Original = "No composed damage changes",
                        Value = "Explicit damage components updated",
                        PatchValue = weaponDialog.ChangedBlocks[rootKey],
                        IsEditable = false,
                    });
                }

                DecorateAndRefreshGroups("Weapon damage");
                ApplyFieldFilter();
                PreviewBtn_Click(sender, e);
                Status.Text = $"Applied explicit damage components to {weaponDialog.ChangedBlocks.Count} preserved metadata block(s). Review the damage totals, then save the patch.";
                return;
            }

            var kind = isStatus ? GuidedMetadataKind.StatusEffect : GuidedMetadataKind.WeaponDamage;
            IReadOnlyList<GuidedField> fields = isStatus
                ? GameplayMetadata.BuildStatusFields(text)
                : GameplayMetadata.BuildWeaponFields(text);
            if (isStatus)
            {
                fields = fields.Select(field => field.Key == "UpgradeType"
                        ? field with { Options = _catalog.EnumOptions("UpgradeType", field.Original) }
                        : field)
                    .ToList();

                // Reopening the visual editor should show any pending unsaved value. This also
                // lets Original / inherited reset any optional DOT/storage override.
                fields = fields.Select(field =>
                {
                    string op = field.TopLevel ? "Field" : "q|";
                    var pending = _allRows.FirstOrDefault(row => row.Op == op
                        && (field.TopLevel ? row.Key == field.Key : row.Path == field.Path));
                    return pending?.IsChanged == true ? field with { Original = pending.Value } : field;
                }).ToList();
            }
            if (fields.Count == 0)
            {
                Status.Text = isStatus
                    ? "This handler exposes no supported status-behavior fields in the decoded metadata."
                    : "This weapon exposes no supported AttackData damage/status fields in the decoded metadata.";
                return;
            }

            var dialog = new GuidedMetadataWindow(item.Name, _itemPath, kind, fields, isStatus ? text : null) { Owner = this };
            if (dialog.ShowDialog() != true) return;

            foreach (var changed in dialog.ChangedFields)
            {
                var definition = changed.Definition;
                string op = definition.TopLevel ? "Field" : "q|";
                var row = _allRows.FirstOrDefault(r => r.Op == op
                    && (definition.TopLevel ? r.Key == definition.Key : r.Path == definition.Path));
                if (GameplayMetadata.IsOptionalInheritedStatusField(definition.Key)
                    && changed.Value == GameplayMetadata.OriginalOrInherited)
                {
                    if (row != null) row.Value = row.Original;
                    continue;
                }
                if (row == null)
                {
                    row = new FieldRow
                    {
                        Key = definition.Key,
                        Display = definition.Display,
                        Path = definition.TopLevel ? "(top-level)" : definition.Path,
                        Op = op,
                        Original = definition.Original,
                        Value = definition.Original,
                    };
                    _allRows.Add(row);
                }
                row.Value = changed.Value;
            }

            foreach (var changedDot in dialog.ChangedDotFields)
            {
                string key = changedDot.Key;
                bool block = key is "DamageOverTime" or "DOTPercentOfBaseDamage";
                _allRows.RemoveAll(row => row.Op == "q|"
                    && (row.Path == key || row.Path.StartsWith(key + ".", StringComparison.Ordinal)));
                _allRows.RemoveAll(row => (row.Op == "statusblock" || row.Op == "Field") && row.Key == key);
                _allRows.Add(new FieldRow
                {
                    Key = key,
                    Display = block ? "Complete native status DOT block" : GameplayMetadata.DisplayFor(key, key),
                    Path = block ? $"(complete preserved {key} block)" : "(top-level)",
                    Op = block ? "statusblock" : "Field",
                    Original = "(not applied)",
                    Value = "Apply native DOT setting",
                    PatchValue = changedDot.Value,
                    IsEditable = false,
                });
            }

            DecorateAndRefreshGroups(isStatus ? "Behavior & stacking" : null);
            ApplyFieldFilter();
            PreviewBtn_Click(sender, e);
            Status.Text = $"Applied {dialog.ChangedFields.Count} guided field edit(s) and {dialog.ChangedDotFields.Count} DOT field(s). Exact metadata paths are preserved in the patch preview.";
        }
        catch (Exception ex) { Status.Text = "Guided metadata error: " + ex.Message; }
    }

    void ModEffectBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_catalog == null || string.IsNullOrEmpty(_itemPath) || ItemList.SelectedItem is not CatalogItem item
            || !_category.StartsWith("Mods", StringComparison.Ordinal))
        {
            Status.Text = "Decode the cache and select a mod first.";
            return;
        }
        if (_allRows.Any(r => r.IsChanged && r.Op == "q|" && r.Path.StartsWith("Upgrades.", StringComparison.Ordinal)))
        {
            Status.Text = "Add the new effect before manually changing existing Upgrades rows. Reset those nested edits first so the complete list cannot contain conflicting versions.";
            return;
        }

        try
        {
            var parsed = DumpParser.ParseText(_catalog.ComposedText(_itemPath));
            var upgrades = parsed.TopLevel.FirstOrDefault(f => f.Key == "Upgrades");
            if (upgrades == null) { Status.Text = "This selected mod has no native Upgrades list to extend."; return; }

            var upgradeTypes = _catalog.EnumOptions("UpgradeType", "");
            var dialog = new ModEffectWindow(item.Name, upgradeTypes) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.SelectedEffect == null) return;

            var effectRow = _allRows.FirstOrDefault(r => r.Op == "effect" && r.Key == "Upgrades");
            string currentRaw = effectRow is { IsChanged: true, PatchValue: not null }
                ? effectRow.PatchValue
                : upgrades.RawValue;
            int before = ModEffects.EntryCount(currentRaw);
            string modified = ModEffects.Append(currentRaw, dialog.SelectedEffect);
            if (effectRow == null)
            {
                effectRow = new FieldRow
                {
                    Key = "Upgrades", Display = "Added native mod effects", Path = "(complete native Upgrades collection)",
                    Op = "effect", Original = "No added effects", Value = "No added effects", IsEditable = false,
                    Group = "Upgrades",
                    Description = "Complete replacement of the native Upgrades collection, preserving every original entry and appending only effects created here."
                };
                _allRows.Add(effectRow);
            }
            effectRow.PatchValue = modified;
            effectRow.Value = $"{ModEffects.EntryCount(modified) - ModEffects.EntryCount(upgrades.RawValue)} effect(s) added · {before} → {ModEffects.EntryCount(modified)} entries";
            DecorateAndRefreshGroups("Upgrades");
            ApplyFieldFilter();
            PreviewBtn_Click(sender, e);
            Status.Text = ModEffects.IsFireResistance(dialog.SelectedEffect)
                ? "Added the current vanilla Flame Repellent gameplay effect shape. "
                  + (dialog.SelectedEffect.DescriptionLocTag.Length > 0
                      ? "Its existing translated description key is included; verify the combined card in game."
                      : "No explicit card-description line was included.")
                : "Added one native Upgrades entry. Existing entries were preserved; review the full patch preview and verify custom localization in game.";
        }
        catch (Exception ex) { Status.Text = "Mod effect was not added: " + ex.Message; }
    }

    void FieldGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FieldGrid.SelectedItem is not FieldRow row)
        {
            DetailTitle.Text = "Select a field";
            DetailDescription.Text = "Choose a row to see what it controls and the exact patch target.";
            DetailPath.Text = "";
            return;
        }
        DetailTitle.Text = row.Label;
        DetailDescription.Text = row.Description;
        DetailPath.Text = $"{row.Op}  {row.Path}";
    }

    void ModLayoutBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_catalog == null || string.IsNullOrEmpty(_itemPath) || ItemList.SelectedItem is not CatalogItem item)
        {
            Status.Text = "Decode the cache and select a mod-using item first.";
            return;
        }
        try
        {
            string composed = _catalog.ComposedText(_itemPath);
            var original = EquipmentExperiments.ReadSet(composed, "ArtifactSlots");
            if (original.Count == 0)
            {
                Status.Text = "The selected item has no ArtifactSlots field; it does not expose a metadata mod layout.";
                return;
            }

            var dialog = new ModSlotLayoutWindow(item.Name, _itemPath, _category, original.Count) { Owner = this };
            if (dialog.ShowDialog() != true) return;

            var request = new ModSlotLayoutRequest(_itemPath, _category,
                dialog.AdditionalOrdinarySlots, dialog.AuraSlots, dialog.NewSlotPolarity);
            var plan = ModSlotLayouts.Build(composed, request);

            _allRows.Clear(); _rows.Clear();
            PathText.Text = $"NATIVE MOD LAYOUT → {item.Name} → {_itemPath}";
            _allRows.Add(new FieldRow
            {
                Key = "ArtifactSlots", Display = $"Equipment slot layout ({plan.OriginalSlots.Count} → {plan.ModifiedSlots.Count})",
                Path = $"({plan.Kind}; append-only; stock indices preserved)", Op = "Field",
                Original = EquipmentExperiments.FormatSet(plan.OriginalSlots),
                Value = EquipmentExperiments.FormatSet(plan.ModifiedSlots),
            });
            if (!plan.ModifiedAdditionalBaseModTypes.SequenceEqual(plan.OriginalAdditionalBaseModTypes))
            {
                _allRows.Add(new FieldRow
                {
                    Key = "AdditionalBaseModTypes", Display = "Native Aura mod base types (Jade profile)",
                    Path = "(required for native second-Aura compatibility)", Op = "Field",
                    Original = EquipmentExperiments.FormatSet(plan.OriginalAdditionalBaseModTypes),
                    Value = EquipmentExperiments.FormatSet(plan.ModifiedAdditionalBaseModTypes),
                });
            }
            foreach (var row in _allRows) _rows.Add(row);
            PatchName.Text = Regex.Replace(item.Name, "[^A-Za-z0-9_-]+", "") + "_ModLayout.txt";
            int firstType = plan.Patch.IndexOf(_itemPath, StringComparison.Ordinal);
            _patchPreamble = firstType > 0 ? plan.Patch[..firstType] : "";
            Preview.Text = BuildPatch();
            Status.Text = plan.Kind == ModEquipmentKind.Generic
                ? $"Append-only layout ready: logical ArtifactSlots {plan.OriginalSlots.Count}→{plan.ModifiedSlots.Count}. Special-slot classification for {_category} is not claimed; two-Arcane behavior unchanged. Review and Save Patch; nothing was deployed."
                : $"Layout ready: ordinary {plan.OriginalOrdinarySlots}→{plan.ResultOrdinarySlots}, " +
                  $"Aura/Stance {plan.ResultAuraSlots}, Utility/Exilus {plan.ResultUtilitySlots}; two-Arcane behavior unchanged. Review and Save Patch; nothing was deployed.";
        }
        catch (Exception ex)
        {
            Status.Text = "Layout generation refused: " + ex.Message;
        }
    }

    void ModIdentityBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_catalog == null || string.IsNullOrEmpty(_itemPath) || ItemList.SelectedItem is not CatalogItem item)
        {
            Status.Text = "Decode the cache and select a mod first.";
            return;
        }
        if (!_category.StartsWith("Mods", StringComparison.Ordinal) || !_itemPath.StartsWith("/Lotus/Upgrades/", StringComparison.Ordinal))
        {
            Status.Text = "Rarity and exact-rank coordination currently supports the Mods category only.";
            return;
        }

        var rarityRow = _allRows.FirstOrDefault(row => row.Key == "Rarity");
        var cacheRarity = rarityRow?.Original ?? "COMMON";
        var fusionRow = _allRows.FirstOrDefault(row => row.Key == "FusionLimit");
        var cacheFusion = fusionRow?.Original ?? "(absent)";
        var detectedServer = UpgradeIdentity.FindOpenWfServer(AppContext.BaseDirectory);
        var dialog = new ModIdentityWindow(item.Name, _itemPath, cacheRarity, cacheFusion, detectedServer) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        if (rarityRow == null)
        {
            rarityRow = new FieldRow
            {
                Key = "Rarity", Display = "Visible mod rarity", Path = "(add top-level)", Op = "Field",
                Original = "(absent)", Value = "(absent)",
            };
            _allRows.Add(rarityRow);
        }
        rarityRow.Value = dialog.SelectedClientRarity;

        if (dialog.SelectedClientFusionLimit != UpgradeIdentity.ClientFusionUnchanged)
        {
            if (fusionRow == null)
            {
                fusionRow = new FieldRow
                {
                    Key = "FusionLimit", Display = "Client FusionLimit tier",
                    Path = "(add · experimental)", Op = "Field", Original = "(absent)", Value = "(absent)",
                };
                _allRows.Add(fusionRow);
            }
            fusionRow.Value = dialog.SelectedClientFusionLimit;
        }

        _pendingUpgradeOverride = dialog.CreateServerOverride
            ? new PendingUpgradeOverride(dialog.ServerRoot, item.Name, _itemPath,
                dialog.SelectedServerRarity, dialog.ServerFusionLimit)
            : null;
        _pendingServerMetadataOverride = null;
        PatchName.Text = Regex.Replace(item.Name, "[^A-Za-z0-9_-]+", "") + "_ClientMetadata.txt";
        ApplyFieldFilter();
        PreviewBtn_Click(sender, e);
        Status.Text = dialog.CreateServerOverride
            ? $"Client metadata plus removable OpenWF package prepared. Server effective value: {dialog.SelectedServerRarity}, rank {dialog.ServerFusionLimit}; inventory remains untouched."
            : $"Client-only metadata prepared: rarity {dialog.SelectedClientRarity}, FusionLimit {dialog.SelectedClientFusionLimit}. OpenWF remains vanilla and inventory remains untouched.";
    }

    void ServerMetadataBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_catalog == null || string.IsNullOrEmpty(_itemPath) || ItemList.SelectedItem is not CatalogItem item)
        {
            Status.Text = "Decode the cache and select an item first.";
            return;
        }
        var detectedServer = UpgradeIdentity.FindOpenWfServer(AppContext.BaseDirectory);
        var dialog = new ServerMetadataWindow(item.Name, _itemPath, detectedServer) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _pendingUpgradeOverride = null;
        _pendingServerMetadataOverride = new PendingServerMetadataOverride(
            dialog.ServerRoot, item.Name, dialog.Dataset, dialog.EntryPath, dialog.Changes);
        PreviewBtn_Click(sender, e);
        Status.Text = $"Generic server package prepared for {dialog.Dataset} → {string.Join(" → ", dialog.EntryPath)} with {dialog.Changes.Count} changed field(s). Inventory and installed dependency files remain untouched.";
    }

    // Format one changed row's patch line. In batch mode the s| op matches ANY current value
    // (per-item values differ), so it applies to every targeted item.
    string RowLine(FieldRow r, bool batch)
    {
        var v = PatchGenerator.Compact(r.PatchValue ?? r.Value);
        return r.Op switch
        {
            "riven" => $"    s|(?s)({r.Path}.*?Attenuation)=[0-9.]+|$1={v}",   // individual weapon only
            "q|"  => $"    q|{r.Path}|{v}",
            "effect" => $"    Upgrades={v}",
            "weapon" => $"    {r.Key}={v}",
            "statusblock" => $"    {r.Key}={v}",
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
        if (_patchPreamble.Length > 0) sb.Append(_patchPreamble);
        if (batch)
        {
            var paths = ItemList.Items.Cast<CatalogItem>().Select(item => item.Path).ToHashSet(StringComparer.Ordinal);
            int count = 0;
            foreach (var it in _all)
            {
                if (!paths.Contains(it.Path)) continue;
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
    static readonly string[] ValidRarity = { "COMMON", "UNCOMMON", "RARE", "LEGENDARY" };

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
            if (r.Key == "Rarity" && v != "(absent)" && !ValidRarity.Contains(v))
                return $"⚠ Rarity \"{v}\" isn't valid — use one of: {string.Join(", ", ValidRarity)}.";
        }
        return null;
    }

    // Equipment metadata is unusually unforgiving: a malformed set can erase the stock layout,
    // while a misspelled object path silently produces a patch that can never match. Validate the
    // complete replacement values before the editor lets a generated or hand-edited patch escape.
    string? ValidateEquipmentRows()
    {
        foreach (var r in _allRows.Where(r => r.IsChanged))
        {
            var value = PatchGenerator.Compact(r.Value);
            if (r.Key == "ArtifactSlots")
            {
                var warning = EquipmentExperiments.ValidateArtifactSlots(value);
                if (warning != null) return "⚠ " + warning + " Refusing to save a malformed layout.";
            }
            else if (r.Key is "AdditionalBaseModTypes" or "CompatibilityTags" or "IncompatibilityTags")
            {
                var warning = r.Key == "AdditionalBaseModTypes"
                    ? EquipmentExperiments.ValidateAbsolutePathSet(r.Key, value)
                    : EquipmentExperiments.ValidateNonEmptySet(r.Key, value);
                if (warning != null) return "⚠ " + warning;
            }
            else if (r.Key == "ItemCompatibility")
            {
                var warning = EquipmentExperiments.ValidateAbsolutePath(r.Key, value);
                if (warning != null) return "⚠ " + warning;
            }
        }
        return null;
    }

    string? ValidateGameplayRows()
    {
        if (_catalog == null || string.IsNullOrEmpty(_itemPath)) return null;
        bool isStatus = _category.StartsWith("Status Effects", StringComparison.Ordinal);
        bool isWeapon = GameplayMetadata.IsWeaponCategory(_category);
        if (!isStatus && !isWeapon) return null;
        if (BatchCheck.IsChecked == true && _allRows.Any(r => r.IsChanged && r.Op is "q|" or "weapon"))
            return "⚠ Batch mode is disabled for nested or composed status/weapon fields because different items can have different fire-mode layouts. Save these edits per item.";

        var definitions = (isStatus
                ? GameplayMetadata.BuildStatusFields(_catalog.ComposedText(_itemPath))
                : GameplayMetadata.BuildWeaponFields(_catalog.ComposedText(_itemPath)))
            .GroupBy(f => f.TopLevel ? "F:" + f.Key : "Q:" + f.Path, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var row in _allRows.Where(r => r.IsChanged))
        {
            string lookup = row.Op == "q|" ? "Q:" + row.Path : "F:" + row.Key;
            if (!definitions.TryGetValue(lookup, out var definition)) continue;
            var warning = GameplayMetadata.Validate(definition, row.Value);
            if (warning != null) return "⚠ " + warning;
        }
        return null;
    }

    string? ValidateChangedRows()
    {
        if (_allRows.Any(r => r.IsChanged && r.Op == "effect")
            && _allRows.Any(r => r.IsChanged && r.Op == "q|" && r.Path.StartsWith("Upgrades.", StringComparison.Ordinal)))
            return "⚠ A complete added-effect list and manual edits to its old nested rows are both changed. Reset one route to avoid conflicting Upgrades patches.";
        foreach (var block in _allRows.Where(r => r.IsChanged && r.Op == "weapon"))
            if (_allRows.Any(r => r.IsChanged && r.Op == "q|"
                && (r.Path == block.Key || r.Path.StartsWith(block.Key + ".", StringComparison.Ordinal))))
                return $"⚠ The complete {block.Key} weapon block and one of its old nested fields are both changed. Reset one route to avoid a conflicting patch.";
        foreach (var block in _allRows.Where(r => r.IsChanged && r.Op == "statusblock"))
            if (_allRows.Any(r => r.IsChanged && r.Op == "q|"
                && (r.Path == block.Key || r.Path.StartsWith(block.Key + ".", StringComparison.Ordinal))))
                return $"⚠ The complete {block.Key} status block and one of its old nested fields are both changed. Reset one route to avoid a conflicting patch.";
        return ValidateEnumRows() ?? ValidateEquipmentRows() ?? ValidateGameplayRows();
    }

    void PreviewBtn_Click(object sender, RoutedEventArgs e)
    {
        var p = BuildPatch();
        var serverPreview = _pendingUpgradeOverride == null ? "" :
            $"\n# Optional OpenWF server-definition package\n# id={UpgradeIdentity.BuildPatchId(_pendingUpgradeOverride.UniqueName)}\n# {_pendingUpgradeOverride.UniqueName}\n# rarity={_pendingUpgradeOverride.Rarity}\n# fusionLimit={_pendingUpgradeOverride.FusionLimit}\n# inventoryMigration=false\n# target={Path.Combine(_pendingUpgradeOverride.ServerRoot, UpgradeIdentity.EnabledPatchRelativePath.Replace('/', Path.DirectorySeparatorChar))}\n";
        var genericPreview = _pendingServerMetadataOverride == null ? "" :
            $"\n# Generic OpenWF server metadata package\n# id={ServerMetadataPatches.BuildPatchId(_pendingServerMetadataOverride.Dataset, _pendingServerMetadataOverride.EntryPath)}\n# dataset={_pendingServerMetadataOverride.Dataset}\n# path={string.Join(" → ", _pendingServerMetadataOverride.EntryPath)}\n# fields={string.Join(", ", _pendingServerMetadataOverride.Changes.Keys.Order(StringComparer.Ordinal))}\n# inventoryMigration=false\n# target={Path.Combine(_pendingServerMetadataOverride.ServerRoot, UpgradeIdentity.EnabledPatchRelativePath.Replace('/', Path.DirectorySeparatorChar))}\n";
        Preview.Text = (p.Length == 0 ? "(no client metadata fields changed)" : p) + serverPreview + genericPreview;
        var w = ValidateChangedRows();
        if (w != null) Status.Text = w;
    }

    void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        var p = BuildPatch();
        if (p.Length == 0 && _pendingUpgradeOverride == null && _pendingServerMetadataOverride == null) { Status.Text = "Nothing changed — edit a value first."; return; }
        var w = ValidateChangedRows();
        if (w != null) { Preview.Text = p; Status.Text = w + " — fix it before saving."; return; }
        PreviewBtn_Click(sender, e);

        if (p.Length == 0 && (_pendingUpgradeOverride != null || _pendingServerMetadataOverride != null))
        {
            try
            {
                var packageDirectory = WritePendingServerPackage(hasClientMetadata: false);
                Status.Text = $"Installed server-only metadata package: {packageDirectory}. Inventory was not modified. Restart OpenWF.";
            }
            catch (Exception ex) { Status.Text = "Save refused: " + ex.Message; }
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "Metadata patch (*.txt)|*.txt",
            FileName = string.IsNullOrWhiteSpace(PatchName.Text) ? "patch.txt" : PatchName.Text,
            InitialDirectory = _patchesFolder ?? AppContext.BaseDirectory
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                File.WriteAllText(dlg.FileName, p);
                if (_pendingUpgradeOverride != null)
                {
                    var serverPackage = UpgradeIdentity.WriteServerPackage(_pendingUpgradeOverride.ServerRoot,
                        _pendingUpgradeOverride.DisplayName, _pendingUpgradeOverride.UniqueName,
                        _pendingUpgradeOverride.Rarity, _pendingUpgradeOverride.FusionLimit, hasClientMetadata: true);
                    Status.Text = $"Saved client patch {dlg.FileName} and enabled server package {serverPackage.PackageDirectory}. Inventory was not modified. Restart OpenWF and the client.";
                }
                else if (_pendingServerMetadataOverride != null)
                {
                    var serverPackage = ServerMetadataPatches.WriteServerPackage(
                        _pendingServerMetadataOverride.ServerRoot, _pendingServerMetadataOverride.DisplayName,
                        _pendingServerMetadataOverride.Dataset, _pendingServerMetadataOverride.EntryPath,
                        _pendingServerMetadataOverride.Changes, hasClientMetadata: true);
                    Status.Text = $"Saved client patch {dlg.FileName} and enabled generic server package {serverPackage.PackageDirectory}. Inventory was not modified. Restart OpenWF and the client.";
                }
                else Status.Text = "Saved: " + dlg.FileName;
            }
            catch (Exception ex) { Status.Text = "Save failed: " + ex.Message; }
        }
    }

    string WritePendingServerPackage(bool hasClientMetadata)
    {
        if (_pendingUpgradeOverride != null)
        {
            return UpgradeIdentity.WriteServerPackage(_pendingUpgradeOverride.ServerRoot,
                _pendingUpgradeOverride.DisplayName, _pendingUpgradeOverride.UniqueName,
                _pendingUpgradeOverride.Rarity, _pendingUpgradeOverride.FusionLimit, hasClientMetadata).PackageDirectory;
        }
        if (_pendingServerMetadataOverride != null)
        {
            return ServerMetadataPatches.WriteServerPackage(
                _pendingServerMetadataOverride.ServerRoot, _pendingServerMetadataOverride.DisplayName,
                _pendingServerMetadataOverride.Dataset, _pendingServerMetadataOverride.EntryPath,
                _pendingServerMetadataOverride.Changes, hasClientMetadata).PackageDirectory;
        }
        throw new InvalidOperationException("No pending server package exists.");
    }

    // Combine several patch .txt files into one: ops are grouped under their type-path (so two files
    // that edit the same type merge into one block), duplicate op lines are dropped, comments stripped.
    void MergeBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Pick 2+ metadata patch files to merge",
            Filter = "Metadata patch (*.txt)|*.txt",
            Multiselect = true,
            InitialDirectory = _patchesFolder ?? AppContext.BaseDirectory
        };
        if (dlg.ShowDialog() != true) return;
        if (dlg.FileNames.Length < 2) { Status.Text = "Pick at least 2 patch files to merge."; return; }

        var order = new List<string>();
        var byType = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        int shared = 0;
        foreach (var file in dlg.FileNames)
        {
            string? cur = null;
            var seenHere = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rawLine in File.ReadAllLines(file))
            {
                var trimmed = rawLine.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;
                bool header = !char.IsWhiteSpace(rawLine[0]) && trimmed.StartsWith("/");
                if (header)
                {
                    cur = trimmed;
                    if (!byType.TryGetValue(cur, out _)) { byType[cur] = new(); order.Add(cur); }
                    else if (seenHere.Add(cur)) shared++;    // this type also came from an earlier file
                }
                else if (cur != null && byType.TryGetValue(cur, out var ops) && !ops.Contains(trimmed))
                    ops.Add(trimmed);
            }
        }
        if (order.Count == 0) { Status.Text = "No patch content found in the selected files."; return; }

        var sb = new StringBuilder();
        foreach (var type in order)
        {
            sb.Append(type).Append('\n');
            foreach (var op in byType[type]) sb.Append("    ").Append(op).Append('\n');
        }
        Preview.Text = sb.ToString();

        var save = new SaveFileDialog
        {
            Filter = "Metadata patch (*.txt)|*.txt",
            FileName = "Merged.txt",
            InitialDirectory = _patchesFolder ?? AppContext.BaseDirectory
        };
        if (save.ShowDialog() == true)
        {
            File.WriteAllText(save.FileName, sb.ToString());
            Status.Text = $"Merged {dlg.FileNames.Length} files → {order.Count} type block(s)"
                        + (shared > 0 ? $" ({shared} shared type[s] combined)" : "") + ". Saved: " + save.FileName;
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
        foreach (var r in _allRows) { r.Value = r.Original; r.PatchValue = null; }
        _patchPreamble = "";
        _pendingUpgradeOverride = null;
        _pendingServerMetadataOverride = null;
        _pendingWeaponDamage = null;
        Preview.Clear();
        Status.Text = "Reset.";
    }
}

sealed record PendingUpgradeOverride(string ServerRoot, string DisplayName, string UniqueName, string Rarity, int FusionLimit);
sealed record PendingServerMetadataOverride(
    string ServerRoot, string DisplayName, string Dataset, IReadOnlyList<object> EntryPath,
    IReadOnlyDictionary<string, JsonNode?> Changes);

public sealed record SectionChoice(string Value, string Display);

public class FieldRow : INotifyPropertyChanged
{
    public string Key { get; init; } = "";           // the REAL field name / path — used to build the patch line
    public string? Display { get; init; }             // optional friendly label; does NOT affect the emitted patch
    public string Label => Display ?? Key;            // what the grid shows
    public string Path { get; init; } = "";
    public string Op { get; init; } = "Field";
    public string Target { get; init; } = "";   // which type this patch line applies to ("" = the selected item)
    public string Original { get; init; } = "";
    public string Group { get; set; } = "General";
    public string Description { get; set; } = "Exact metadata field.";
    public string EditorKind { get; set; } = "Text";
    public IReadOnlyList<string> Options { get; set; } = Array.Empty<string>();
    public bool IsEditable { get; init; } = true;
    public string? PatchValue { get; set; }

    string _value = "";
    public string Value
    {
        get => _value;
        set { if (_value != value) { _value = value; Raise(nameof(Value)); Raise(nameof(IsChanged)); } }
    }
    public bool IsChanged => Value != Original
        && !(GameplayMetadata.IsOptionalInheritedStatusField(Key)
             && Value == GameplayMetadata.OriginalOrInherited);

    public event PropertyChangedEventHandler? PropertyChanged;
    void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
