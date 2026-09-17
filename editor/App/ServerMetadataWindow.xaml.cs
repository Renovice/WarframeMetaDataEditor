using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using MetadataPatchEditor.Core;
using Microsoft.Win32;

namespace MetadataPatchEditor;

public partial class ServerMetadataWindow : Window
{
    readonly string _displayName;
    readonly string _uniqueName;
    readonly ObservableCollection<ServerMetadataFieldRow> _rows = new();
    IReadOnlyList<ServerDatasetMatch> _matches = [];

    public string ServerRoot => ServerPathBox.Text.Trim();
    public string Dataset { get; private set; } = "";
    public IReadOnlyList<object> EntryPath { get; private set; } = [];
    public IReadOnlyDictionary<string, JsonNode?> Changes { get; private set; } = new Dictionary<string, JsonNode?>();

    public ServerMetadataWindow(string displayName, string uniqueName, string? detectedServer)
    {
        InitializeComponent();
        _displayName = displayName;
        _uniqueName = uniqueName;
        FieldGrid.ItemsSource = _rows;
        ServerPathBox.Text = detectedServer ?? "";
        TargetText.Text = $"{displayName}\n{uniqueName}";
        ReloadMatches();
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
        ReloadMatches();
    }

    void ReloadMatches()
    {
        _rows.Clear();
        MatchCombo.ItemsSource = null;
        DisablePackageButton.IsEnabled = false;
        if (!UpgradeIdentity.IsServerRoot(ServerRoot))
        {
            StatusText.Text = "Select a valid SpaceNinjaServer folder to discover matching server datasets.";
            return;
        }
        try
        {
            StatusText.Text = "Scanning Public Export datasets…";
            _matches = ServerMetadataPatches.FindMatches(ServerRoot, _uniqueName);
            MatchCombo.ItemsSource = _matches.Select(match => match.DisplayPath).ToArray();
            if (_matches.Count == 0)
            {
                StatusText.Text = "No exact Public Export entry maps to this client type. A server override cannot be generated automatically for it.";
                return;
            }
            MatchCombo.SelectedIndex = 0;
            StatusText.Text = $"Found {_matches.Count} exact server mapping(s).";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Dataset scan failed: " + ex.Message;
        }
    }

    void Match_Changed(object sender, SelectionChangedEventArgs e)
    {
        _rows.Clear();
        if (MatchCombo.SelectedIndex < 0 || MatchCombo.SelectedIndex >= _matches.Count) return;
        var match = _matches[MatchCombo.SelectedIndex];
        foreach (var property in match.Entry.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var json = property.Value?.ToJsonString() ?? "null";
            _rows.Add(new ServerMetadataFieldRow
            {
                Field = property.Key,
                JsonType = JsonType(property.Value),
                OriginalJson = json,
                ValueJson = json,
            });
        }
        try
        {
            DisablePackageButton.IsEnabled = ServerMetadataPatches.HasEnabledServerPackage(ServerRoot, match.Dataset, match.Path);
            StatusText.Text = DisablePackageButton.IsEnabled
                ? "A matching generic server package is currently ENABLED."
                : $"Loaded {match.Entry.Count} server fields from {match.Dataset}.";
        }
        catch (Exception ex)
        {
            DisablePackageButton.IsEnabled = false;
            StatusText.Text = "Package state invalid: " + ex.Message;
        }
    }

    void AddField_Click(object sender, RoutedEventArgs e)
    {
        var field = NewFieldNameBox.Text.Trim();
        if (field.Length == 0) { StatusText.Text = "Enter a non-empty server field name."; return; }
        if (_rows.Any(row => row.Field == field)) { StatusText.Text = $"Field {field} already exists in the grid."; return; }
        try
        {
            _ = JsonNode.Parse(NewFieldValueBox.Text);
        }
        catch (Exception ex) { StatusText.Text = "New field JSON is invalid: " + ex.Message; return; }
        _rows.Add(new ServerMetadataFieldRow
        {
            Field = field,
            JsonType = "new",
            OriginalJson = "(new field)",
            ValueJson = NewFieldValueBox.Text,
        });
        NewFieldNameBox.Clear();
        StatusText.Text = $"Added new field {field}; it will be included in the package.";
    }

    void DisablePackage_Click(object sender, RoutedEventArgs e)
    {
        if (MatchCombo.SelectedIndex < 0 || MatchCombo.SelectedIndex >= _matches.Count) return;
        var match = _matches[MatchCombo.SelectedIndex];
        try
        {
            var destination = ServerMetadataPatches.DisableServerPackage(ServerRoot, match.Dataset, match.Path);
            DisablePackageButton.IsEnabled = false;
            StatusText.Text = $"Disabled safely: {destination}. Restart OpenWF to restore vanilla.";
        }
        catch (Exception ex) { StatusText.Text = "Disable refused: " + ex.Message; }
    }

    void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (!UpgradeIdentity.IsServerRoot(ServerRoot)) { StatusText.Text = "Select a valid SpaceNinjaServer folder."; return; }
        if (MatchCombo.SelectedIndex < 0 || MatchCombo.SelectedIndex >= _matches.Count) { StatusText.Text = "Select a mapped server entry."; return; }
        var changed = _rows.Where(row => row.IsChanged).ToArray();
        if (changed.Length == 0) { StatusText.Text = "Change or add at least one server field."; return; }

        var values = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var row in changed)
        {
            try { values[row.Field] = JsonNode.Parse(row.ValueJson); }
            catch (Exception ex) { StatusText.Text = $"{row.Field} is not valid JSON: {ex.Message}"; return; }
            if (row.OriginalJson != "(new field)" && JsonType(values[row.Field]) != row.JsonType &&
                JsonType(values[row.Field]) != "null" && row.JsonType != "null")
            {
                StatusText.Text = $"{row.Field} changes JSON type from {row.JsonType} to {JsonType(values[row.Field])}; use the original type.";
                return;
            }
        }
        var match = _matches[MatchCombo.SelectedIndex];
        try { _ = ServerMetadataPatches.BuildDefinitionJson(match.Dataset, match.Path, values); }
        catch (Exception ex) { StatusText.Text = "Package validation failed: " + ex.Message; return; }
        Dataset = match.Dataset;
        EntryPath = match.Path;
        Changes = values;
        DialogResult = true;
    }

    static string JsonType(JsonNode? node) => node switch
    {
        null => "null",
        JsonObject => "object",
        JsonArray => "array",
        JsonValue value when value.TryGetValue<bool>(out _) => "boolean",
        JsonValue value when value.TryGetValue<string>(out _) => "string",
        JsonValue => "number",
        _ => "unknown",
    };

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

public sealed class ServerMetadataFieldRow : INotifyPropertyChanged
{
    public string Field { get; init; } = "";
    public string JsonType { get; init; } = "";
    public string OriginalJson { get; init; } = "";
    string _valueJson = "";
    public string ValueJson
    {
        get => _valueJson;
        set
        {
            if (_valueJson == value) return;
            _valueJson = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValueJson)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChanged)));
        }
    }
    public bool IsChanged => ValueJson != OriginalJson;
    public event PropertyChangedEventHandler? PropertyChanged;
}
