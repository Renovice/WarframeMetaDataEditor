using System.Windows;
using System.Windows.Controls;
using MetadataPatchEditor.Core;

namespace MetadataPatchEditor;

public partial class ModSlotLayoutWindow : Window
{
    readonly ModEquipmentKind _kind;
    public int AdditionalOrdinarySlots { get; private set; }
    public int AuraSlots { get; private set; }
    public string NewSlotPolarity { get; private set; } = "AP_ANY";

    public ModSlotLayoutWindow(string name, string path, string category, int artifactCount)
    {
        InitializeComponent();
        _kind = ModSlotLayouts.Classify(category, path);
        TargetText.Text = $"{name}  •  {category}\n{path}\nDecoded ArtifactSlots: {artifactCount}";
        MetadataOptionCombo.Configure(PolarityCombo, "ArtifactPolarity",
            ["AP_ANY", "AP_UNIVERSAL", "AP_ATTACK", "AP_DEFENSE", "AP_TACTIC"],
            "Polarity assigned to each newly appended ordinary slot.");
        PolarityCombo.SelectedValue = "AP_ANY";

        if (_kind == ModEquipmentKind.Warframe)
        {
            int aura = ModSlotLayouts.WarframeAuraCountFromArtifactCount(artifactCount);
            AuraCombo.SelectedIndex = Math.Clamp(aura, 0, 2);
            AdditionalBox.Text = "1";
            AuraCombo.SelectedIndex = 2;
            RuleText.Text = "Warframe U43 rule: slots 1–8 are ordinary, 9 is Aura, 10 is Exilus, and 11 is the second Aura. The native grid is 3x4, so the largest clean metadata-only layout is 9 ordinary + 2 Aura + 1 Exilus. Larger arrays are clipped until the runtime grid hook exists.";
        }
        else if (_kind == ModEquipmentKind.Necramech)
        {
            AuraCombo.SelectedIndex = 0;
            AuraCombo.IsEnabled = false;
            AdditionalBox.Text = "0";
            RuleText.Text = "Necramech profile: every decoded ArtifactSlots entry is an ordinary mod position. Stock Necramechs already fill the clean native 3x4 grid with 12 slots. More rows require the separate runtime grid hook.";
        }
        else
        {
            AuraCombo.SelectedIndex = 0;
            AuraCombo.IsEnabled = false;
            RuleText.Text = "Generic append-only profile: preserves every stock index and adds ordinary AP_* entries at the end. Exact special-slot rules vary by equipment category, so Aura/Stance creation remains disabled until that category is proven.";
        }
    }

    void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(AdditionalBox.Text, out int additional) || additional is < 0 or > 16)
        {
            MessageBox.Show(this, "Additional ordinary slots must be a whole number from 0 to 16.", "Invalid slot count", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AdditionalOrdinarySlots = additional;
        AuraSlots = AuraCombo.SelectedIndex;
        NewSlotPolarity = PolarityCombo.SelectedValue as string ?? "AP_ANY";
        DialogResult = true;
    }
}
