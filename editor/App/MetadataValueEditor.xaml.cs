using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MetadataPatchEditor.Core;

namespace MetadataPatchEditor;

/// <summary>A single metadata value rendered according to its proven field type.</summary>
public partial class MetadataValueEditor : UserControl
{
    bool _syncingBoolean;
    readonly ObservableCollection<MetadataOptionInfo> _describedOptions = new();

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(MetadataValueEditor),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));
    public static readonly DependencyProperty EditorKindProperty = DependencyProperty.Register(
        nameof(EditorKind), typeof(string), typeof(MetadataValueEditor),
        new PropertyMetadata("Text", OnEditorKindChanged));
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options), typeof(IEnumerable), typeof(MetadataValueEditor), new PropertyMetadata(null, OnOptionContextChanged));
    public static readonly DependencyProperty FieldKeyProperty = DependencyProperty.Register(
        nameof(FieldKey), typeof(string), typeof(MetadataValueEditor), new PropertyMetadata("", OnOptionContextChanged));
    public static readonly DependencyProperty FieldDescriptionProperty = DependencyProperty.Register(
        nameof(FieldDescription), typeof(string), typeof(MetadataValueEditor), new PropertyMetadata("", OnOptionContextChanged));

    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string EditorKind { get => (string)GetValue(EditorKindProperty); set => SetValue(EditorKindProperty, value); }
    public IEnumerable? Options { get => (IEnumerable?)GetValue(OptionsProperty); set => SetValue(OptionsProperty, value); }
    public string FieldKey { get => (string)GetValue(FieldKeyProperty); set => SetValue(FieldKeyProperty, value); }
    public string FieldDescription { get => (string)GetValue(FieldDescriptionProperty); set => SetValue(FieldDescriptionProperty, value); }
    public IEnumerable<MetadataOptionInfo> DescribedOptions => _describedOptions;

    public MetadataValueEditor()
    {
        InitializeComponent();
        RebuildOptions();
        UpdateEditor();
        UpdateBoolean();
    }

    static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MetadataValueEditor)d).UpdateBoolean();

    static void OnEditorKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MetadataValueEditor)d).UpdateEditor();

    static void OnOptionContextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MetadataValueEditor)d).RebuildOptions();

    void RebuildOptions()
    {
        _describedOptions.Clear();
        if (Options == null) return;
        foreach (object? option in Options)
        {
            string value = option?.ToString() ?? "";
            if (value.Length == 0) continue;
            _describedOptions.Add(MetadataOptionHelp.Describe(FieldKey, value, FieldDescription));
        }
    }

    void UpdateEditor()
    {
        if (TextEditor == null) return;
        TextEditor.Visibility = EditorKind == "Text" ? Visibility.Visible : Visibility.Collapsed;
        EnumEditor.Visibility = EditorKind == "Enum" ? Visibility.Visible : Visibility.Collapsed;
        NumberEditor.Visibility = EditorKind == "Number" ? Visibility.Visible : Visibility.Collapsed;
        BooleanEditor.Visibility = EditorKind == "Boolean" ? Visibility.Visible : Visibility.Collapsed;
    }

    void UpdateBoolean()
    {
        if (OffButton == null) return;
        _syncingBoolean = true;
        OnButton.IsChecked = Value == "1";
        OffButton.IsChecked = Value != "1";
        _syncingBoolean = false;
    }

    void On_Click(object sender, RoutedEventArgs e) { if (!_syncingBoolean) Value = "1"; }
    void Off_Click(object sender, RoutedEventArgs e) { if (!_syncingBoolean) Value = "0"; }
    void Increment_Click(object sender, RoutedEventArgs e) => Adjust(+1);
    void Decrement_Click(object sender, RoutedEventArgs e) => Adjust(-1);

    void NumberBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Up) { Adjust(+1); e.Handled = true; }
        else if (e.Key == Key.Down) { Adjust(-1); e.Handled = true; }
    }

    void NumberEditor_MouseEnter(object sender, MouseEventArgs e) => SpinnerButtons.Visibility = Visibility.Visible;
    void NumberEditor_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!NumberBox.IsKeyboardFocusWithin) SpinnerButtons.Visibility = Visibility.Collapsed;
    }
    void NumberBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => SpinnerButtons.Visibility = Visibility.Visible;
    void NumberBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!NumberEditor.IsMouseOver) SpinnerButtons.Visibility = Visibility.Collapsed;
    }

    void Adjust(int direction)
    {
        if (!decimal.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return;
        decimal step = NaturalStep(Value);
        Value = (number + direction * step).ToString("0.############################", CultureInfo.InvariantCulture);
    }

    public static decimal NaturalStep(string value)
    {
        int dot = value.IndexOf('.');
        if (dot < 0) return 1m;
        int decimals = Math.Min(4, value.Length - dot - 1);
        decimal step = 1m;
        for (int i = 0; i < decimals; i++) step /= 10m;
        return step;
    }
}
