using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MetadataPatchEditor.Core;

namespace MetadataPatchEditor;

/// <summary>Applies the shared friendly-label and hover-help model to standalone enum dropdowns.</summary>
public static class MetadataOptionCombo
{
    public static void Configure(ComboBox combo, string key, IEnumerable<string> values,
        string fieldDescription)
    {
        combo.ItemsSource = values.Select(value => MetadataOptionHelp.Describe(key, value, fieldDescription)).ToList();
        combo.SelectedValuePath = nameof(MetadataOptionInfo.Value);
        combo.ItemTemplate = (DataTemplate)Application.Current.FindResource("MetadataOptionTemplate");
        combo.SetBinding(FrameworkElement.ToolTipProperty, new Binding("SelectedItem.Tooltip")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.Self)
        });
        ToolTipService.SetInitialShowDelay(combo, 250);
        ToolTipService.SetShowDuration(combo, 30000);
    }
}
