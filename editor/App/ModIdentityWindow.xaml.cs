using System.IO;
using System.Windows;
using MetadataPatchEditor.Core;
using Microsoft.Win32;

namespace MetadataPatchEditor;

public partial class ModIdentityWindow : Window
{
    readonly string _uniqueName;

    public string SelectedClientRarity => ClientRarityCombo.SelectedValue?.ToString() ?? "COMMON";
    public string SelectedClientFusionLimit => ClientFusionCombo.SelectedValue?.ToString() ?? UpgradeIdentity.ClientFusionUnchanged;
    public bool CreateServerOverride => ServerOverrideCheck.IsChecked == true;
    public string SelectedServerRarity => ServerRarityCombo.SelectedValue?.ToString() ?? "COMMON";
    public int ServerFusionLimit { get; private set; }
    public string ServerRoot => ServerPathBox.Text.Trim();

    public ModIdentityWindow(
        string displayName, string uniqueName, string cacheRarity, string cacheFusionLimit, string? detectedServer)
    {
        InitializeComponent();
        _uniqueName = uniqueName;

        MetadataOptionCombo.Configure(ClientRarityCombo, "Rarity", UpgradeIdentity.ValidRarities,
            "Visible client rarity tier for this mod.");
        ClientRarityCombo.SelectedValue = UpgradeIdentity.ValidRarities.Contains(cacheRarity) ? cacheRarity : "COMMON";
        MetadataOptionCombo.Configure(ClientFusionCombo, "FusionLimit",
            new[] { UpgradeIdentity.ClientFusionUnchanged }.Concat(UpgradeIdentity.ValidFusionLimits),
            "Coarse client FusionLimit enum; this is not the server's exact numeric rank.");
        ClientFusionCombo.SelectedValue = UpgradeIdentity.ValidFusionLimits.Contains(cacheFusionLimit)
            ? cacheFusionLimit
            : UpgradeIdentity.ClientFusionUnchanged;
        MetadataOptionCombo.Configure(ServerRarityCombo, "Rarity", UpgradeIdentity.ValidRarities,
            "Rarity written to the optional OpenWF server definition.");
        ServerRarityCombo.SelectedValue = ClientRarityCombo.SelectedValue;
        ServerRankBox.Text = "5";
        ServerPathBox.Text = detectedServer ?? "";

        TargetText.Text = $"{displayName}\n{uniqueName}\nClient cache: rarity {cacheRarity}, FusionLimit {cacheFusionLimit}";
        LoadServerState(showError: false);
        UpdateServerMode();
    }

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the SpaceNinjaServer repository folder",
            InitialDirectory = Directory.Exists(ServerPathBox.Text)
                ? ServerPathBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog() != true) return;
        ServerPathBox.Text = dialog.FolderName;
        LoadServerState(showError: true);
    }

    void LoadServerState(bool showError)
    {
        if (!UpgradeIdentity.IsServerRoot(ServerRoot))
        {
            InstalledStateText.Text = "No valid OpenWF server selected.";
            DisableServerPatchButton.IsEnabled = false;
            if (showError) ErrorText.Text = "Select the SpaceNinjaServer root containing src, package.json, and node_modules/warframe-public-export-plus.";
            return;
        }

        try
        {
            var vanilla = UpgradeIdentity.ReadVanillaValue(ServerRoot, _uniqueName);
            var effective = UpgradeIdentity.ReadEffectiveValue(ServerRoot, _uniqueName);
            var installed = UpgradeIdentity.HasEnabledServerPackage(ServerRoot, _uniqueName);
            ServerRarityCombo.SelectedValue = effective.Rarity;
            ServerRankBox.Text = effective.FusionLimit.ToString();
            ServerOverrideCheck.IsChecked = installed;
            DisableServerPatchButton.IsEnabled = installed;
            InstalledStateText.Text = installed ? "Matching package is ENABLED." : "No matching enabled package.";
            ServerModeText.Text = installed
                ? $"OpenWF vanilla: {vanilla.Rarity}, rank {vanilla.FusionLimit}. Effective: {effective.Rarity}, rank {effective.FusionLimit}."
                : $"Client-only mode. OpenWF remains vanilla: {vanilla.Rarity}, rank {vanilla.FusionLimit}.";
            ErrorText.Text = "";
        }
        catch (Exception ex)
        {
            InstalledStateText.Text = "Server package inspection failed.";
            DisableServerPatchButton.IsEnabled = false;
            if (showError) ErrorText.Text = ex.Message;
        }
    }

    void ServerOverride_Changed(object sender, RoutedEventArgs e) => UpdateServerMode();

    void UpdateServerMode()
    {
        if (ServerDefinitionPanel == null || ServerModeText == null) return;
        ServerDefinitionPanel.IsEnabled = CreateServerOverride;
        if (!CreateServerOverride)
        {
            try
            {
                ServerModeText.Text = UpgradeIdentity.HasEnabledServerPackage(ServerRoot, _uniqueName)
                    ? "A matching package is still enabled. Unchecking only prevents an update; use Disable installed server package to restore vanilla."
                    : "Client-only: no OpenWF definition package will be created or changed.";
            }
            catch (Exception ex)
            {
                ServerModeText.Text = "Server package state is invalid: " + ex.Message;
            }
        }
        else
            ServerModeText.Text = "Server override enabled: Save Patch installs a removable package under Metadata Patches\\Enabled. Restart OpenWF afterward.";
    }

    void DisableServerPatch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var destination = UpgradeIdentity.DisableServerPackage(ServerRoot, _uniqueName);
            ServerOverrideCheck.IsChecked = false;
            DisableServerPatchButton.IsEnabled = false;
            InstalledStateText.Text = "Package moved to Disabled.";
            ErrorText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(143, 206, 127));
            ErrorText.Text = $"Disabled safely: {destination}. Restart OpenWF to restore the vanilla definition.";
        }
        catch (Exception ex)
        {
            ErrorText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 139, 139));
            ErrorText.Text = "Disable refused: " + ex.Message;
        }
    }

    void Accept_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 139, 139));
        var clientFusionWarning = UpgradeIdentity.ValidateClientFusionLimit(SelectedClientFusionLimit);
        if (clientFusionWarning != null)
        {
            ErrorText.Text = clientFusionWarning;
            return;
        }
        if (CreateServerOverride)
        {
            if (!int.TryParse(ServerRankBox.Text.Trim(), out var rank))
            {
                ErrorText.Text = "Server fusion limit must be an integer from 0 through 10.";
                return;
            }
            var warning = UpgradeIdentity.Validate(_uniqueName, SelectedServerRarity, rank);
            if (warning != null) { ErrorText.Text = warning; return; }
            if (!UpgradeIdentity.IsServerRoot(ServerRoot))
            {
                ErrorText.Text = "Select the real SpaceNinjaServer root before enabling a server component.";
                return;
            }
            ServerFusionLimit = rank;
        }
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
