using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MetadataPatchEditor.Core;

namespace MetadataPatchEditor;

public partial class WeaponDamageWindow : Window
{
    readonly WeaponDamageComposition _composition;
    readonly ObservableCollection<DamageComponentRow> _rows = [];
    readonly ObservableCollection<ElementChip> _chips = [];
    WeaponDamageProfile? _profile;
    WeaponFireControl? _fireControl;
    bool _loadingFireControl;
    bool _loadingStatusChance;

    public IReadOnlyDictionary<string, string> ChangedBlocks { get; private set; }
        = new Dictionary<string, string>();

    public WeaponDamageWindow(string weaponName, WeaponDamageComposition composition)
    {
        InitializeComponent();
        Heading.Text = weaponName + " · visual weapon editor";
        _composition = composition;
        DamageGrid.ItemsSource = _rows;
        ElementChips.ItemsSource = _chips;
        ProfileBox.ItemsSource = composition.Profiles;
        ProfileBox.DisplayMemberPath = nameof(WeaponDamageProfile.Name);
        FireControlBox.ItemsSource = composition.FireControls;
        FireControlBox.DisplayMemberPath = nameof(WeaponFireControl.Name);
        FireNav.IsEnabled = composition.FireControls.Count > 0;
        StatusNav.IsEnabled = composition.Profiles.Any(profile => profile.HasStatusChance);
        if (composition.Profiles.Count > 0) ProfileBox.SelectedIndex = 0;
        else if (composition.FireControls.Count > 0) FireControlBox.SelectedIndex = 0;
        ShowPanel(OverviewPanel);
    }

    void ProfileBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _profile = ProfileBox.SelectedItem as WeaponDamageProfile;
        RefreshRows();
        LoadStatusChance();
        if (_profile != null)
        {
            var matching = _composition.FireControls.FirstOrDefault(control => control.BehaviorIndex == _profile.BehaviorIndex);
            if (matching != null) FireControlBox.SelectedItem = matching;
            else if (FireControlBox.SelectedIndex < 0 && _composition.FireControls.Count > 0) FireControlBox.SelectedIndex = 0;
        }
        RefreshOverview();
    }

    void FireControlBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _fireControl = FireControlBox.SelectedItem as WeaponFireControl;
        LoadFireControl();
        RefreshOverview();
    }

    void LoadFireControl()
    {
        _loadingFireControl = true;
        if (_fireControl == null)
        {
            FireRateBox.Text = ReloadBox.Text = ChargeBox.Text = BurstBox.Text = "";
            ShotsPerSecondText.Text = "—";
            ReloadLabel.Visibility = ReloadBox.Visibility = Visibility.Collapsed;
            ChargeLabel.Visibility = ChargeBox.Visibility = Visibility.Collapsed;
            BurstLabel.Visibility = BurstBox.Visibility = Visibility.Collapsed;
            FirePathText.Text = "No firing-state metadata was exposed for this profile.";
        }
        else
        {
            FireRateBox.Text = _fireControl.FireRateRpm;
            ReloadBox.Text = _fireControl.ReloadSeconds;
            ChargeBox.Text = _fireControl.ChargeSeconds;
            BurstBox.Text = _fireControl.BurstDelaySeconds;
            ReloadLabel.Visibility = ReloadBox.Visibility = _fireControl.HasReloadTime ? Visibility.Visible : Visibility.Collapsed;
            ChargeLabel.Visibility = ChargeBox.Visibility = _fireControl.HasChargeTime ? Visibility.Visible : Visibility.Collapsed;
            BurstLabel.Visibility = BurstBox.Visibility = _fireControl.HasBurstDelay ? Visibility.Visible : Visibility.Collapsed;
            FirePathText.Text = _fireControl.Path;
            RefreshShotsPerSecond();
        }
        _loadingFireControl = false;
    }

    void FireControl_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingFireControl || _fireControl == null) return;
        _fireControl.FireRateRpm = FireRateBox.Text.Trim();
        if (_fireControl.HasReloadTime) _fireControl.ReloadSeconds = ReloadBox.Text.Trim();
        if (_fireControl.HasChargeTime) _fireControl.ChargeSeconds = ChargeBox.Text.Trim();
        if (_fireControl.HasBurstDelay) _fireControl.BurstDelaySeconds = BurstBox.Text.Trim();
        RefreshShotsPerSecond();
        RefreshOverview();
    }

    void RefreshShotsPerSecond()
    {
        if (_fireControl == null || _fireControl.ShotsPerSecond <= 0)
            ShotsPerSecondText.Text = "—";
        else
            ShotsPerSecondText.Text = _fireControl.ShotsPerSecond.ToString("0.###", CultureInfo.InvariantCulture);
    }

    void RefreshRows()
    {
        _rows.Clear();
        _chips.Clear();
        if (_profile == null) return;
        foreach (var component in _profile.Components.Where(component => component.Damage > 0))
        {
            var row = new DamageComponentRow(_profile, component);
            row.PropertyChanged += Row_PropertyChanged;
            _rows.Add(row);
            _chips.Add(new ElementChip(component.Key, component.Name + "  " + WeaponDamageProfile.Format(component.Damage),
                DamageColor(component.Key)));
        }
        RefreshAddChoices();
        RefreshTotal();
        FormatNote.Text = _profile.UsesExplicitDamage
            ? "This profile already uses explicit per-element damage values. The full containing block is preserved."
            : "This legacy fraction profile is converted to the game's explicit-damage representation only after a real change. The full containing block is preserved.";
        DamagePathText.Text = _profile.Path;
    }

    void RefreshAddChoices()
    {
        if (_profile == null) return;
        var choices = _profile.Components.Where(component => component.Damage == 0)
            .Select(component => component.Key).ToList();
        MetadataOptionCombo.Configure(AddTypeBox, "Type", choices,
            "Damage component added to this exact attack profile.");
        if (choices.Count > 0) AddTypeBox.SelectedValue = choices[0];
    }

    void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DamageComponentRow.Value)) return;
        RefreshTotal();
        RefreshChips();
    }

    void RefreshChips()
    {
        _chips.Clear();
        if (_profile == null) return;
        foreach (var component in _profile.Components.Where(component => component.Damage > 0))
            _chips.Add(new ElementChip(component.Key, component.Name + "  " + WeaponDamageProfile.Format(component.Damage),
                DamageColor(component.Key)));
        OverviewElementCount.Text = _chips.Count.ToString(CultureInfo.InvariantCulture);
    }

    void RefreshTotal()
    {
        string total = _profile == null ? "0" : WeaponDamageProfile.Format(_profile.TotalDamage);
        TotalText.Text = total;
        OverviewDamage.Text = total;
        OverviewElementCount.Text = _profile?.Components.Count(component => component.Damage > 0)
            .ToString(CultureInfo.InvariantCulture) ?? "0";
    }

    void RefreshOverview()
    {
        RefreshTotal();
        OverviewStatusChance.Text = _profile?.HasStatusChance == true
            && decimal.TryParse(_profile.StatusChancePercent, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal statusPercent)
                ? statusPercent.ToString("0.###", CultureInfo.InvariantCulture) + "%"
                : "—";
        if (_fireControl == null)
        {
            OverviewFireRate.Text = "—";
            OverviewReload.Text = "—";
        }
        else
        {
            OverviewFireRate.Text = _fireControl.ShotsPerSecond > 0
                ? _fireControl.ShotsPerSecond.ToString("0.###", CultureInfo.InvariantCulture)
                : "—";
            OverviewReload.Text = _fireControl.ReloadSeconds.Length == 0 ? "—" : _fireControl.ReloadSeconds;
        }
        if (_profile == null) OverviewExplanation.Text = "Select a damage profile to inspect it.";
        else
        {
            string fireSentence = _fireControl == null
                ? "No matching firing-state scalar was found for this profile."
                : $"Its matching firing state is {_fireControl.Name}. Fire rate is stored as {_fireControl.FireRateRpm} RPM and displayed above as shots per second.";
            string statusSentence = _profile.HasStatusChance
                ? $"Status chance is {_profile.StatusChancePercent}%."
                : "This profile exposes no normal status-chance scalar.";
            OverviewExplanation.Text = $"{_profile.Name} owns the damage shown here. {fireSentence}  •  {statusSentence}";
        }
    }

    void LoadStatusChance()
    {
        _loadingStatusChance = true;
        if (_profile?.HasStatusChance == true)
        {
            StatusChanceBox.IsEnabled = true;
            StatusChanceBox.Text = _profile.StatusChancePercent;
            StatusChanceNativeText.Text = _profile.NativeStatusChance;
            StatusProfileText.Text = $"Editing {_profile.Name} only.";
            StatusPathText.Text = _profile.Path + ".ProcChance";
            StatusNav.IsEnabled = true;
        }
        else
        {
            StatusChanceBox.Text = "";
            StatusChanceBox.IsEnabled = false;
            StatusChanceNativeText.Text = "Not present on this attack profile";
            StatusProfileText.Text = "The selected profile does not expose ProcChance, so the editor will not invent one.";
            StatusPathText.Text = "No ProcChance scalar on the selected profile.";
            StatusNav.IsEnabled = false;
        }
        _loadingStatusChance = false;
    }

    void StatusChance_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingStatusChance || _profile?.HasStatusChance != true) return;
        _profile.StatusChancePercent = StatusChanceBox.Text.Trim();
        StatusChanceNativeText.Text = _profile.NativeStatusChance;
        RefreshOverview();
    }

    void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_profile == null || AddTypeBox.SelectedValue is not string damageType) return;
        if (!decimal.TryParse(AddAmountBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal damage) || damage <= 0)
        {
            StatusText.Text = "Enter a positive absolute damage amount using a decimal point.";
            return;
        }
        _profile.SetDamage(damageType, damage);
        StatusText.Text = "";
        RefreshRows();
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DamageComponentRow row) return;
        row.Profile.SetDamage(row.Key, 0);
        RefreshRows();
    }

    void ElementChip_Click(object sender, RoutedEventArgs e)
    {
        ShowPanel(DamagePanel);
        if ((sender as FrameworkElement)?.Tag is not string key) return;
        var row = _rows.FirstOrDefault(candidate => candidate.Key == key);
        if (row != null)
        {
            DamageGrid.SelectedItem = row;
            DamageGrid.ScrollIntoView(row);
        }
    }

    void DamageCard_Click(object sender, MouseButtonEventArgs e) => ShowPanel(DamagePanel);
    void StatusCard_Click(object sender, MouseButtonEventArgs e) { if (StatusNav.IsEnabled) ShowPanel(StatusPanel); }
    void FireCard_Click(object sender, MouseButtonEventArgs e) { if (FireNav.IsEnabled) ShowPanel(FirePanel); }
    void OverviewNav_Click(object sender, RoutedEventArgs e) => ShowPanel(OverviewPanel);
    void DamageNav_Click(object sender, RoutedEventArgs e) => ShowPanel(DamagePanel);
    void StatusNav_Click(object sender, RoutedEventArgs e) => ShowPanel(StatusPanel);
    void FireNav_Click(object sender, RoutedEventArgs e) => ShowPanel(FirePanel);
    void AdvancedNav_Click(object sender, RoutedEventArgs e) => ShowPanel(AdvancedPanel);

    void ShowPanel(FrameworkElement panel)
    {
        foreach (var candidate in new FrameworkElement[] { OverviewPanel, DamagePanel, StatusPanel, FirePanel, AdvancedPanel })
            candidate.Visibility = ReferenceEquals(candidate, panel) ? Visibility.Visible : Visibility.Collapsed;
    }

    void Apply_Click(object sender, RoutedEventArgs e)
    {
        DamageGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        DamageGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (_rows.Any(row => !row.IsValid))
        {
            StatusText.Text = "Every damage amount must be a non-negative number using a decimal point.";
            ShowPanel(DamagePanel);
            return;
        }
        if (_composition.Profiles.Any(profile => profile.IsChanged && profile.TotalDamage <= 0))
        {
            StatusText.Text = "A changed attack profile must retain at least one positive damage component.";
            ShowPanel(DamagePanel);
            return;
        }
        foreach (var profile in _composition.Profiles.Where(profile => profile.StatusChanceIsChanged))
        {
            string? warning = profile.ValidateStatusChance();
            if (warning == null) continue;
            StatusText.Text = warning;
            ProfileBox.SelectedItem = profile;
            ShowPanel(StatusPanel);
            return;
        }
        foreach (var control in _composition.FireControls.Where(control => control.IsChanged))
        {
            string? warning = control.Validate();
            if (warning == null) continue;
            StatusText.Text = warning;
            FireControlBox.SelectedItem = control;
            ShowPanel(FirePanel);
            return;
        }
        ChangedBlocks = _composition.BuildChangedTopLevelBlocks();
        if (ChangedBlocks.Count == 0)
        {
            StatusText.Text = "No weapon values changed.";
            return;
        }
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    static string DamageColor(string key) => key switch
    {
        "DT_IMPACT" => "#75BFE8", "DT_PUNCTURE" => "#E6C278", "DT_SLASH" => "#E58A8A",
        "DT_FIRE" => "#F08A45", "DT_FREEZE" => "#6CCAF2", "DT_ELECTRICITY" => "#E8D95C",
        "DT_POISON" => "#75D27A", "DT_EXPLOSION" => "#E5A95B", "DT_RADIATION" => "#D6E56A",
        "DT_GAS" => "#91D27B", "DT_MAGNETIC" => "#B984E8", "DT_VIRAL" => "#D677B5",
        "DT_CORROSIVE" => "#A7CF67", "DT_RADIANT" => "#D9C8FF", "DT_SENTIENT" => "#D77A88",
        _ => "#AFC1CC"
    };
}

public sealed record ElementChip(string Key, string Label, string Color);

public sealed class DamageComponentRow : INotifyPropertyChanged
{
    public WeaponDamageProfile Profile { get; }
    public string Key { get; }
    public string Name { get; }
    public string Color => WeaponDamageWindowColor(Key);
    public bool IsValid { get; private set; } = true;
    string _value;

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            IsValid = decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal damage) && damage >= 0;
            if (IsValid) Profile.SetDamage(Key, damage);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsValid)));
        }
    }

    public DamageComponentRow(WeaponDamageProfile profile, WeaponDamageComponent component)
    {
        Profile = profile;
        Key = component.Key;
        Name = component.Name;
        _value = WeaponDamageProfile.Format(component.Damage);
    }

    static string WeaponDamageWindowColor(string key) => key switch
    {
        "DT_IMPACT" => "#75BFE8", "DT_PUNCTURE" => "#E6C278", "DT_SLASH" => "#E58A8A",
        "DT_FIRE" => "#F08A45", "DT_FREEZE" => "#6CCAF2", "DT_ELECTRICITY" => "#E8D95C",
        "DT_POISON" => "#75D27A", "DT_EXPLOSION" => "#E5A95B", "DT_RADIATION" => "#D6E56A",
        "DT_GAS" => "#91D27B", "DT_MAGNETIC" => "#B984E8", "DT_VIRAL" => "#D677B5",
        "DT_CORROSIVE" => "#A7CF67", "DT_RADIANT" => "#D9C8FF", "DT_SENTIENT" => "#D77A88",
        _ => "#AFC1CC"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
}
