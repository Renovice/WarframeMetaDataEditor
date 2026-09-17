using System.Windows;
using System.Windows.Controls;
using MetadataPatchEditor.Core;

namespace MetadataPatchEditor;

public partial class ModEffectWindow : Window
{
    public ModEffectSpec? SelectedEffect { get; private set; }

    public ModEffectWindow(string modName, IReadOnlyList<string> upgradeTypes)
    {
        InitializeComponent();
        Heading.Text = "Add effect · " + modName;
        PresetBox.ItemsSource = new[] { "Fire resistance · vanilla Flame Repellent", "Custom native upgrade" };
        MetadataOptionCombo.Configure(UpgradeTypeBox, "UpgradeType", upgradeTypes,
            "The decoded gameplay stat or behavior changed by this upgrade entry.");
        MetadataOptionCombo.Configure(OperationBox, "OperationType", GameplayMetadata.UpgradeOperations,
            "How the value combines with the underlying stat.");
        MetadataOptionCombo.Configure(DamageTypeBox, "DamageType", GameplayMetadata.DamageTypes,
            "Damage family/filter used by this upgrade entry.");
        PresetBox.SelectedIndex = 0;
    }

    void PresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetBox.SelectedIndex != 0)
        {
            LocTagBox.Text = "";
            IncludeDescriptionBox.IsChecked = false;
            return;
        }
        var effect = ModEffects.VanillaFireResistance;
        UpgradeTypeBox.SelectedValue = effect.UpgradeType;
        OperationBox.SelectedValue = effect.OperationType;
        ValueBox.Text = effect.Value;
        DamageTypeBox.SelectedValue = effect.DamageType;
        LocTagBox.Text = effect.DescriptionLocTag;
        IncludeDescriptionBox.IsChecked = true;
        PercentBox.IsChecked = effect.DisplayAsPercent;
        SmallerBox.IsChecked = effect.SmallerIsBetter;
    }

    void IncludeDescriptionBox_Changed(object sender, RoutedEventArgs e)
    {
        if (LocTagBox == null || EvidenceText == null) return;
        bool include = IncludeDescriptionBox.IsChecked == true;
        LocTagBox.IsEnabled = include;
        EvidenceText.Text = include
            ? "A visible line needs an existing /Lotus/Language/... key. The Fire Resistance preset reuses the game's translated Flame Repellent line; verify the combined card in game."
            : "The gameplay effect will still be added, but no explicit description line will be requested for this entry.";
    }

    void Add_Click(object sender, RoutedEventArgs e)
    {
        if (IncludeDescriptionBox.IsChecked == true && string.IsNullOrWhiteSpace(LocTagBox.Text))
        {
            StatusText.Text = "Choose an existing /Lotus/Language/... description key, or turn off the visible card-description option.";
            return;
        }
        var effect = new ModEffectSpec(
            UpgradeTypeBox.SelectedValue as string ?? "",
            OperationBox.SelectedValue as string ?? "",
            ValueBox.Text.Trim(),
            DamageTypeBox.SelectedValue as string ?? "",
            IncludeDescriptionBox.IsChecked == true ? LocTagBox.Text.Trim() : "",
            PercentBox.IsChecked == true,
            SmallerBox.IsChecked == true);
        string? warning = ModEffects.Validate(effect);
        if (warning != null) { StatusText.Text = warning; return; }
        SelectedEffect = effect;
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
