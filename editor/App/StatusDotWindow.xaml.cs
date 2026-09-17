using System.Windows;
using MetadataPatchEditor.Core;

namespace MetadataPatchEditor;

public partial class StatusDotWindow : Window
{
    readonly StatusDotConfiguration _configuration;

    public StatusDotWindow(string displayName, StatusDotConfiguration configuration)
    {
        InitializeComponent();
        _configuration = configuration;
        Heading.Text = configuration.Existed ? $"Edit DOT · {displayName}" : $"Add DOT · {displayName}";
        SupportLabel.Text = configuration.Existed
            ? "This handler already has a native DOT payload. Existing unexposed fields are preserved."
            : "Native schema support is verified for this handler family. The new payload is structurally valid, but a converted status still requires an in-game damage test.";

        DamageTypeEditor.Options = GameplayMetadata.DamageTypes;
        DamageTypeEditor.Value = configuration.DamageType;
        PercentBox.Text = configuration.PercentOfSourceHit;
        ConsolidationEditor.Options = GameplayMetadata.DotConsolidationModes;
        ConsolidationEditor.Value = configuration.ConsolidationMode;
        StackStyleEditor.Options = GameplayMetadata.StackStyles;
        StackStyleEditor.Value = configuration.StackStyle;
        HighestBasisEditor.Options = GameplayMetadata.HighestDotBasisModes;
        HighestBasisEditor.Value = configuration.HighestDamageBasisMode;
    }

    void Apply_Click(object sender, RoutedEventArgs e)
    {
        _configuration.DamageType = DamageTypeEditor.Value;
        _configuration.PercentOfSourceHit = PercentBox.Text.Trim();
        _configuration.ConsolidationMode = ConsolidationEditor.Value;
        _configuration.StackStyle = StackStyleEditor.Value;
        _configuration.HighestDamageBasisMode = HighestBasisEditor.Value;
        string? warning = _configuration.Validate();
        if (warning != null)
        {
            ErrorLabel.Text = warning;
            return;
        }
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
