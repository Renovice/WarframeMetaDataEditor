using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MetadataPatchEditor.Core;

namespace MetadataPatchEditor;

public partial class GuidedMetadataWindow : Window
{
    readonly ObservableCollection<GuidedFieldRow> _visible = new();
    readonly List<GuidedFieldRow> _all;
    readonly string _displayName;
    readonly StatusDotConfiguration? _dotConfiguration;
    readonly GuidedMetadataKind _kind;
    string _groupFilter = "";

    public Brush StatusAccent { get; private set; } = new SolidColorBrush(Color.FromRgb(87, 185, 232));

    public IReadOnlyList<GuidedFieldRow> ChangedFields => _all.Where(r => r.IsChanged).ToList();
    public IReadOnlyDictionary<string, string> ChangedDotFields
        => _dotConfiguration?.BuildChangedTopLevelFields()
           ?? new Dictionary<string, string>(StringComparer.Ordinal);

    public GuidedMetadataWindow(string displayName, string path, GuidedMetadataKind kind,
        IReadOnlyList<GuidedField> fields, string? composedText = null)
    {
        InitializeComponent();
        _kind = kind;
        _displayName = displayName;
        Heading.Text = kind == GuidedMetadataKind.StatusEffect
            ? $"Status behavior · {displayName}"
            : $"Weapon damage & status · {displayName}";
        PathLabel.Text = path;
        Explanation.Text = kind == GuidedMetadataKind.StatusEffect
            ? "Edit the behavior below. The friendly explanation is shown beside every value; turn on Advanced only when you need the exact emitted metadata path."
            : "Each fire mode and alternate/radial profile has a separate indexed path. Damage shares use decimal fractions; status chance 0.25 means 25%; forced procs use PT_* identifiers.";
        if (kind == GuidedMetadataKind.StatusEffect)
        {
            var overview = GameplayMetadata.BuildStatusOverview(displayName, path, fields);
            StatusAccent = (Brush)new BrushConverter().ConvertFromString(overview.Accent)!;
            HeaderIcon.IconKey = overview.Status;
            HeaderIcon.Accent = StatusAccent;
            SidebarIcon.IconKey = overview.Status;
            SidebarIcon.Accent = StatusAccent;
            ElementName.Text = overview.Status;
            StatusFamilyLabel.Text = overview.Status;
            StatusFamilyLabel.Foreground = StatusAccent;
            OverviewSummary.Text = overview.Summary;
            OverviewRules.Text = overview.Rules;
            OverviewScope.Text = overview.Scope;
            MetricList.ItemsSource = overview.Metrics;
            EffectList.ItemsSource = overview.Steps;
            DotCapabilityLabel.Text = overview.DotCapability;
            DotBuilderButton.Content = overview.HasDot ? "Edit DOT…" : "Add DOT…";
            fields = fields.Select(field => CustomizeStatusField(overview.Status, field)).ToList();
            if (composedText != null)
                _dotConfiguration = StatusDotConfiguration.Parse(composedText, displayName);
            else
            {
                DotBuilderButton.IsEnabled = false;
                DotCapabilityLabel.Text += " Reload the selected cache record to use the DOT builder.";
            }
        }
        else
        {
            HeaderIcon.IconKey = "effect";
            SidebarIcon.IconKey = "effect";
            ElementName.Text = "Metadata";
            StatusFamilyLabel.Text = "ADVANCED";
            OverviewNav.IsEnabled = false;
            BehaviorNav.IsEnabled = false;
            DotNav.IsEnabled = false;
            EffectsNav.IsEnabled = false;
        }
        _all = fields.Select(f => new GuidedFieldRow(f)).ToList();
        Grid.ItemsSource = _visible;
        GroupFieldList.ItemsSource = _visible;
        Grid.SelectionChanged += Grid_SelectionChanged;
        DataContext = this;
        Refresh();
        if (kind == GuidedMetadataKind.StatusEffect) ShowOverview();
        else ShowAdvanced();
    }

    static GuidedField CustomizeStatusField(string status, GuidedField field)
    {
        if (status != "Cold") return field;
        return field.Key switch
        {
            "MaxStacks" => field with
            {
                Display = "Added stacks before full freeze",
                Description = "Cold only: repeated stacks accepted after the first proc. Full freeze occurs at the cap, so 9 here means 10 total procs (1 initial + 9 added)."
            },
            "MaxStacksWithOverguard" => field with
            {
                Display = "Added stacks while Overguard remains",
                Description = "Separate Cold repeat-stack cap while the target still has Overguard. The initial proc is not included in this number."
            },
            "RepeatFreezeModifier" => field with
            {
                Display = "Freeze buildup per added stack",
                Description = "Additional Cold slow/freeze modifier contributed by every repeated stack. The stored decimal 0.05 means 5%."
            },
            _ when field.Path.StartsWith("StackedUpgrades.", StringComparison.Ordinal) && field.Key == "BaseValue" => field with
            {
                Display = "Critical vulnerability — first proc"
            },
            _ when field.Path.StartsWith("StackedUpgrades.", StringComparison.Ordinal) && field.Key == "RepeatValue" => field with
            {
                Display = "Critical vulnerability — each added stack"
            },
            _ when field.Path.StartsWith("FrozenDebuffs.", StringComparison.Ordinal) && field.Key == "BaseValue" => field with
            {
                Display = "Critical vulnerability — fully frozen"
            },
            _ => field
        };
    }

    void DotBuilder_Click(object sender, RoutedEventArgs e)
    {
        if (_dotConfiguration == null) return;
        var dialog = new StatusDotWindow(_displayName, _dotConfiguration) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        DotBuilderButton.Content = "Edit DOT…";
        DotCapabilityLabel.Text = _dotConfiguration.Existed
            ? "DOT settings are ready. Apply this window to add the changed native fields to the patch preview."
            : "A new DOT payload is ready. Apply this window, review the patch, then validate its damage in game.";
    }

    void Refresh()
    {
        string query = FilterBox.Text?.Trim() ?? "";
        _visible.Clear();
        foreach (var row in _all)
            if ((_groupFilter.Length == 0 || row.Group.Equals(_groupFilter, StringComparison.OrdinalIgnoreCase))
                && (query.Length == 0 || row.Display.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.Group.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.Path.Contains(query, StringComparison.OrdinalIgnoreCase)))
                _visible.Add(row);
        CountLabel.Text = _groupFilter.Length == 0
            ? $"{_visible.Count} / {_all.Count} settings"
            : $"{_visible.Count} {_groupFilter} settings";
    }

    void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    void Section_Click(object sender, RoutedEventArgs e)
    {
        ShowGroup((sender as FrameworkElement)?.Tag as string ?? "");
    }

    void EffectStep_Click(object sender, RoutedEventArgs e)
    {
        string group = (sender as FrameworkElement)?.Tag as string ?? "";
        ShowGroup(group.Length == 0 ? "Behavior & stacking" : group);
        if (_visible.Count > 0)
        {
            Grid.SelectedIndex = 0;
        }
    }

    void OverviewNav_Click(object sender, RoutedEventArgs e) => ShowOverview();
    void BehaviorNav_Click(object sender, RoutedEventArgs e) => ShowGroup("Behavior & stacking");
    void DotNav_Click(object sender, RoutedEventArgs e) => ShowGroup("Damage over time");
    void EffectsNav_Click(object sender, RoutedEventArgs e) => ShowGroup("Stack effects");
    void AdvancedNav_Click(object sender, RoutedEventArgs e) => ShowAdvanced();

    void ShowOverview()
    {
        _groupFilter = "";
        OverviewPanel.Visibility = Visibility.Visible;
        GroupPanel.Visibility = Visibility.Collapsed;
        AdvancedPanel.Visibility = Visibility.Collapsed;
    }

    void ShowGroup(string group)
    {
        _groupFilter = group;
        FilterBox.Text = "";
        Refresh();
        GroupHeading.Text = group;
        GroupHeading.Foreground = StatusAccent;
        GroupExplanation.Text = group switch
        {
            "Behavior & stacking" => "Lifetime, stack limits, reaction rules, and the native values that decide how repeated procs build up.",
            "Damage over time" => "The explicit repeating-damage payload, damage family, source-hit scaling, merging, and highest-basis behavior.",
            "Stack effects" => "Modifiers applied by the first stack, every repeated stack, and any special capped or frozen state.",
            _ => "Editable native controls for this part of the status effect."
        };
        EmptyGroupLabel.Visibility = _visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        OverviewPanel.Visibility = Visibility.Collapsed;
        GroupPanel.Visibility = Visibility.Visible;
        AdvancedPanel.Visibility = Visibility.Collapsed;
    }

    void ShowAdvanced()
    {
        _groupFilter = "";
        Refresh();
        OverviewPanel.Visibility = Visibility.Collapsed;
        GroupPanel.Visibility = Visibility.Collapsed;
        AdvancedPanel.Visibility = Visibility.Visible;
    }

    void AdvancedCheck_Changed(object sender, RoutedEventArgs e)
        => ExactPathColumn.Visibility = AdvancedCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Grid.SelectedItem is not GuidedFieldRow row)
        {
            DetailLabel.Text = "Select a setting to see what it controls.";
            return;
        }
        string options = row.Options.Count == 0 ? "" : $" Valid values: {string.Join(", ", row.Options)}.";
        DetailLabel.Text = row.Description + options + $" Exact field: {row.Path}.";
    }

    void Apply_Click(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit(DataGridEditingUnit.Cell, true);
        Grid.CommitEdit(DataGridEditingUnit.Row, true);
        foreach (var row in _all.Where(r => r.IsChanged))
        {
            var warning = GameplayMetadata.Validate(row.Definition, row.Value);
            if (warning != null)
            {
                StatusLabel.Text = warning;
                Grid.SelectedItem = row;
                Grid.ScrollIntoView(row);
                return;
            }
        }
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

public sealed class GuidedFieldRow : INotifyPropertyChanged
{
    public GuidedField Definition { get; }
    public string Key => Definition.Key;
    public string Path => Definition.Path;
    public string Display => Definition.Display;
    public string Group => Definition.Group;
    public string Description => Definition.Description;
    public string Original => Definition.Original;
    public bool TopLevel => Definition.TopLevel;
    public bool IsSet => Definition.IsSet;
    public IReadOnlyList<string> Options => Definition.Options;
    public string EditorKind { get; }
    public string IconKey { get; }

    string _value;
    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChanged)));
        }
    }
    public bool IsChanged => Value != Original;

    public GuidedFieldRow(GuidedField definition)
    {
        Definition = definition;
        _value = definition.Original;
        EditorKind = definition.Options.SequenceEqual(["0", "1"], StringComparer.Ordinal) ? "Boolean"
            : definition.Options.Count >= 2 ? "Enum"
            : decimal.TryParse(definition.Original, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? "Number"
            : "Text";
        IconKey = definition.Key switch
        {
            "Duration" or "FrozenDuration" => "duration",
            "MaxStacks" or "MaxStacksWithOverguard" or "ReworkMaxStacks" => "stacks",
            "StackStyle" => "instances",
            "BaseFreezeModifier" or "RepeatFreezeModifier" or "OverfreezeDurationPercentPerStack" => "freeze",
            _ when definition.Path.StartsWith("DamageOverTime.", StringComparison.Ordinal) => "dot",
            _ when definition.Path.Contains("Upgrades.", StringComparison.Ordinal) => "vulnerability",
            _ when definition.Key.Contains("Radial", StringComparison.Ordinal) || definition.Key == "DamageRadius" => "blast",
            _ => "effect"
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
