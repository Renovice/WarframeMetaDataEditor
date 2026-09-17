using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MetadataPatchEditor.Core;

namespace MetadataPatchEditor;

public partial class WarframeStatsWindow : Window
{
    readonly WarframeStatsComposition _composition;

    public IReadOnlyList<WarframeStatChange> ChangedFields { get; private set; } = [];

    public WarframeStatsWindow(string warframeName, WarframeStatsComposition composition)
    {
        InitializeComponent();
        _composition = composition;
        Heading.Text = warframeName + " · visual Warframe editor";
        BaseStatsList.ItemsSource = composition.BaseStats;
        RankStatsList.ItemsSource = composition.RankStats;
        RefreshPreview();
        Show(OverviewPanel);
    }

    void Overview_Click(object sender, RoutedEventArgs e) => Show(OverviewPanel);
    void Stats_Click(object sender, RoutedEventArgs e) => Show(StatsPanel);
    void Ranks_Click(object sender, RoutedEventArgs e) => Show(RanksPanel);
    void StatsCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => Show(StatsPanel);

    void Show(UIElement panel)
    {
        OverviewPanel.Visibility = panel == OverviewPanel ? Visibility.Visible : Visibility.Collapsed;
        StatsPanel.Visibility = panel == StatsPanel ? Visibility.Visible : Visibility.Collapsed;
        RanksPanel.Visibility = panel == RanksPanel ? Visibility.Visible : Visibility.Collapsed;
    }

    void AnyValue_TextChanged(object sender, TextChangedEventArgs e) => RefreshPreview();

    void RefreshPreview()
    {
        if (!IsInitialized) return;
        string Pair(decimal? basis, decimal rank) => basis is null ? "Not exposed" : $"{Number(basis.Value)} → {Number(basis.Value + rank)}";
        HealthPreview.Text = Pair(_composition.BaseValue("Base Health"), _composition.RankTotal("AVATAR_HEALTH_MAX"));
        ShieldPreview.Text = Pair(_composition.BaseValue("Base Shields"), _composition.RankTotal("AVATAR_SHIELD_MAX"));
        ArmorPreview.Text = Pair(_composition.BaseValue("Base Armor"),
            _composition.RankTotal("AVATAR_ARMOUR_MAX") + _composition.RankTotal("AVATAR_ARMOR_MAX"));
        EnergyPreview.Text = Pair(_composition.BaseValue("Base Energy"), _composition.RankTotal("AVATAR_POWER_MAX"));
        StartingEnergyPreview.Text = ValueOrInherited(_composition.BaseValue("Starting Energy"));
        SprintPreview.Text = ValueOrInherited(_composition.BaseValue("Sprint Speed"));
        HeaderPreview.Text = "native base → rank 30";
        StatusText.Text = "Changes are staged only after Apply to Patch.";
    }

    void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ChangedFields = _composition.BuildChanges();
            DialogResult = true;
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    static string ValueOrInherited(decimal? value) => value is null ? "Inherited" : Number(value.Value);
    static string Number(decimal value) => value.ToString("0.#########", CultureInfo.InvariantCulture);
}
