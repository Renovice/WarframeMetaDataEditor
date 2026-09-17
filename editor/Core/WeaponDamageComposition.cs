using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MetadataPatchEditor.Core;

public sealed record WeaponDamageComponent(string Key, string Name, decimal Damage);

public sealed class WeaponFireControl
{
    internal EeObject Node { get; }
    internal string RootKey { get; }
    readonly Dictionary<string, string?> _original;

    public string Path { get; }
    public string Name { get; }
    public int BehaviorIndex { get; }
    public string FireRateRpm { get; set; }
    public string ReloadSeconds { get; set; }
    public string ChargeSeconds { get; set; }
    public string BurstDelaySeconds { get; set; }
    public decimal ShotsPerSecond => Number(FireRateRpm) / 60m;
    public bool HasReloadTime => Node.Scalar("reloadTime") != null;
    public bool HasChargeTime => Node.Scalar("ChargeTime") != null;
    public bool HasBurstDelay => Node.Scalar("BurstDelay") != null;
    public bool IsChanged => FireRateRpm != (_original["fireRate"] ?? "")
        || ReloadSeconds != (_original["reloadTime"] ?? "")
        || ChargeSeconds != (_original["ChargeTime"] ?? "")
        || BurstDelaySeconds != (_original["BurstDelay"] ?? "");

    internal WeaponFireControl(string path, string name, string rootKey, int behaviorIndex, EeObject node)
    {
        Path = path;
        Name = name;
        RootKey = rootKey;
        BehaviorIndex = behaviorIndex;
        Node = node;
        _original = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["fireRate"] = node.Scalar("fireRate"),
            ["reloadTime"] = node.Scalar("reloadTime"),
            ["ChargeTime"] = node.Scalar("ChargeTime"),
            ["BurstDelay"] = node.Scalar("BurstDelay")
        };
        FireRateRpm = _original["fireRate"] ?? "";
        ReloadSeconds = _original["reloadTime"] ?? "";
        ChargeSeconds = _original["ChargeTime"] ?? "";
        BurstDelaySeconds = _original["BurstDelay"] ?? "";
    }

    public string? Validate()
    {
        if (!Positive(FireRateRpm)) return $"{Name}: fire rate must be a positive RPM value.";
        if (HasReloadTime && !NonNegative(ReloadSeconds)) return $"{Name}: reload time must be zero or greater.";
        if (HasChargeTime && !NonNegative(ChargeSeconds)) return $"{Name}: charge time must be zero or greater.";
        if (HasBurstDelay && !NonNegative(BurstDelaySeconds)) return $"{Name}: burst delay must be zero or greater.";
        return null;
    }

    internal void Apply()
    {
        Node.SetScalar("fireRate", FireRateRpm);
        if (HasReloadTime) Node.SetScalar("reloadTime", ReloadSeconds);
        if (HasChargeTime) Node.SetScalar("ChargeTime", ChargeSeconds);
        if (HasBurstDelay) Node.SetScalar("BurstDelay", BurstDelaySeconds);
    }

    static bool Positive(string value) => Number(value) > 0;
    static bool NonNegative(string value)
        => decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed) && parsed >= 0;
    static decimal Number(string? value)
        => decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed) ? parsed : 0;
}

public sealed class WeaponDamageProfile
{
    internal EeObject Node { get; }
    internal string RootKey { get; }
    internal IReadOnlyDictionary<string, decimal> Original { get; }
    readonly string? _originalStatusChance;

    public string Path { get; }
    public string Name { get; }
    public int BehaviorIndex { get; }
    public bool UsesExplicitDamage { get; }
    public List<WeaponDamageComponent> Components { get; private set; }
    public decimal TotalDamage => Components.Sum(component => component.Damage);
    public bool HasStatusChance => _originalStatusChance != null;
    public string StatusChancePercent { get; set; }
    public string NativeStatusChance => TryPercent(StatusChancePercent, out decimal percent)
        ? Format(percent / 100m) : "—";
    public bool DamageIsChanged => WeaponDamageComposition.ComponentTypes.Any(type =>
        Original.GetValueOrDefault(type) != Components.First(component => component.Key == type).Damage);
    public bool StatusChanceIsChanged => HasStatusChance
        && StatusChancePercent.Trim() != Format(Number(_originalStatusChance) * 100m);
    public bool IsChanged => DamageIsChanged || StatusChanceIsChanged;

    internal WeaponDamageProfile(string path, string name, string rootKey, EeObject node)
    {
        Path = path;
        Name = name;
        BehaviorIndex = ParseBehaviorIndex(path);
        RootKey = rootKey;
        Node = node;
        UsesExplicitDamage = node.Scalar("UseNewFormat") == "1";
        Components = ReadComponents(node);
        Original = Components.ToDictionary(component => component.Key, component => component.Damage, StringComparer.Ordinal);
        _originalStatusChance = node.Scalar("ProcChance");
        StatusChancePercent = _originalStatusChance == null ? "" : Format(Number(_originalStatusChance) * 100m);
    }

    public void SetDamage(string key, decimal damage)
    {
        if (damage < 0) throw new ArgumentOutOfRangeException(nameof(damage));
        Components = Components.Select(component => component.Key == key ? component with { Damage = damage } : component).ToList();
    }

    internal void Apply()
    {
        if (DamageIsChanged)
        {
            Node.SetScalar("UseNewFormat", "1", "Type");
            foreach (var component in Components)
                Node.SetScalar(component.Key, Format(component.Damage), "Amount");
            Node.SetScalar("Amount", Format(TotalDamage));
        }
        if (StatusChanceIsChanged && TryPercent(StatusChancePercent, out decimal percent))
            Node.SetScalar("ProcChance", Format(percent / 100m));
    }

    public string? ValidateStatusChance()
    {
        if (!HasStatusChance || !StatusChanceIsChanged) return null;
        if (!TryPercent(StatusChancePercent, out decimal percent) || percent < 0)
            return $"{Name}: status chance must be a non-negative percentage.";
        return null;
    }

    static List<WeaponDamageComponent> ReadComponents(EeObject node)
    {
        decimal amount = Number(node.Scalar("Amount"));
        var raw = WeaponDamageComposition.ComponentTypes.ToDictionary(
            key => key, key => Number(node.Scalar(key)), StringComparer.Ordinal);
        decimal rawTotal = raw.Values.Sum();
        bool explicitValues = node.Scalar("UseNewFormat") == "1" || rawTotal > 1.0001m || amount == 0;
        var absolute = raw.ToDictionary(pair => pair.Key,
            pair => explicitValues ? pair.Value : pair.Value * amount, StringComparer.Ordinal);

        if (absolute.Values.Sum() == 0 && amount > 0)
        {
            string type = WeaponDamageComposition.ComponentKey(node.Scalar("Type"));
            if (absolute.ContainsKey(type)) absolute[type] = amount;
        }

        return WeaponDamageComposition.ComponentTypes
            .Select(key => new WeaponDamageComponent(key, WeaponDamageComposition.FriendlyName(key), absolute[key]))
            .ToList();
    }

    static decimal Number(string? raw)
        => decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value) ? value : 0;

    static bool TryPercent(string? raw, out decimal value)
        => decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    public static string Format(decimal value)
        => value.ToString("0.############################", CultureInfo.InvariantCulture);

    static int ParseBehaviorIndex(string path)
    {
        var match = Regex.Match(path, @"Behaviors\.(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
    }
}

/// <summary>
/// Converts a weapon's selected AttackData profile to the native explicit-damage format while
/// preserving its containing top-level metadata block. Zero components remain valid and can be
/// assigned positive damage to create multi-element combinations such as Magnetic + Viral.
/// </summary>
public sealed class WeaponDamageComposition
{
    public static readonly string[] ComponentTypes =
    [
        "DT_IMPACT", "DT_PUNCTURE", "DT_SLASH", "DT_FIRE", "DT_FREEZE",
        "DT_ELECTRICITY", "DT_POISON", "DT_EXPLOSION", "DT_RADIATION", "DT_GAS",
        "DT_MAGNETIC", "DT_VIRAL", "DT_CORROSIVE", "DT_RADIANT", "DT_SENTIENT"
    ];

    readonly EeObject _root;
    public IReadOnlyList<WeaponDamageProfile> Profiles { get; }
    public IReadOnlyList<WeaponFireControl> FireControls { get; }

    WeaponDamageComposition(EeObject root, IReadOnlyList<WeaponDamageProfile> profiles,
        IReadOnlyList<WeaponFireControl> fireControls)
    {
        _root = root;
        Profiles = profiles;
        FireControls = fireControls;
    }

    public static WeaponDamageComposition Parse(string composedText)
    {
        string body = string.Join('\n', composedText.Replace("\r", "").Split('\n')
            .Where(line => !line.TrimStart().StartsWith('>'))) + "\n";
        EeObject root = EeParser.Parse(body);
        var candidates = new List<(string Path, string Root, EeObject Node)>();
        var fireCandidates = new List<(string Path, string Root, int BehaviorIndex, string StateKind, EeObject Node)>();
        Traverse(root, "", "", candidates, fireCandidates);
        var duplicateNames = new Dictionary<string, int>(StringComparer.Ordinal);
        var profiles = new List<WeaponDamageProfile>();
        foreach (var candidate in candidates)
        {
            string baseName = ProfileName(candidate.Path);
            int number = duplicateNames.GetValueOrDefault(baseName) + 1;
            duplicateNames[baseName] = number;
            string name = number == 1 ? baseName : $"{baseName} · profile {number}";
            profiles.Add(new WeaponDamageProfile(candidate.Path, name, candidate.Root, candidate.Node));
        }
        var controls = fireCandidates.Select(candidate => new WeaponFireControl(candidate.Path,
            FireControlName(candidate.BehaviorIndex, candidate.StateKind), candidate.Root,
            candidate.BehaviorIndex, candidate.Node)).ToList();
        return new WeaponDamageComposition(root, profiles, controls);
    }

    public IReadOnlyDictionary<string, string> BuildChangedTopLevelBlocks()
    {
        var changed = Profiles.Where(profile => profile.IsChanged).ToList();
        foreach (var profile in changed) profile.Apply();
        var changedControls = FireControls.Where(control => control.IsChanged).ToList();
        foreach (var control in changedControls) control.Apply();
        return changed.Select(profile => profile.RootKey)
            .Concat(changedControls.Select(control => control.RootKey)).Distinct(StringComparer.Ordinal)
            .ToDictionary(key => key, key => EeParser.UnparseValue(_root.Value(key)!), StringComparer.Ordinal);
    }

    public WeaponDamageComposition Clone()
        => Parse(EeParser.UnparseRoot(_root));

    public static string FriendlyName(string key) => key switch
    {
        "DT_IMPACT" => "Impact", "DT_PUNCTURE" => "Puncture", "DT_SLASH" => "Slash",
        "DT_FIRE" => "Heat", "DT_FREEZE" => "Cold", "DT_ELECTRICITY" => "Electricity",
        "DT_POISON" => "Toxin", "DT_EXPLOSION" => "Blast", "DT_RADIATION" => "Radiation",
        "DT_GAS" => "Gas", "DT_MAGNETIC" => "Magnetic", "DT_VIRAL" => "Viral",
        "DT_CORROSIVE" => "Corrosive", "DT_RADIANT" => "Void", "DT_SENTIENT" => "Tau",
        _ => key
    };

    internal static string ComponentKey(string? type) => type switch
    {
        "DT_VOID" => "DT_RADIANT",
        null => "",
        _ => type
    };

    static void Traverse(EeNode node, string path, string rootKey,
        List<(string Path, string Root, EeObject Node)> profiles,
        List<(string Path, string Root, int BehaviorIndex, string StateKind, EeObject Node)> fireControls)
    {
        if (node is EeObject obj)
        {
            foreach (var entry in obj.Entries)
            {
                string nextPath = path.Length == 0 ? entry.Key : path + "." + entry.Key;
                string nextRoot = rootKey.Length == 0 ? entry.Key : rootKey;
                if (entry.Value is EeObject damage
                    && entry.Key is "AttackData" or "AlternateAttackData" or "RadialDamage" or "DamageOverTime"
                    && damage.Value("Amount") is EeScalar && damage.Value("Type") is EeScalar)
                    profiles.Add((nextPath, nextRoot, damage));
                if (entry.Value is EeObject state && entry.Key.StartsWith("state:", StringComparison.Ordinal)
                    && state.Scalar("fireRate") != null)
                    fireControls.Add((nextPath, nextRoot, BehaviorIndex(nextPath), entry.Key["state:".Length..], state));
                Traverse(entry.Value, nextPath, nextRoot, profiles, fireControls);
            }
        }
        else if (node is EeArray array)
        {
            for (int index = 0; index < array.Items.Count; index++)
                Traverse(array.Items[index], path + "." + index.ToString(CultureInfo.InvariantCulture), rootKey, profiles, fireControls);
        }
    }

    static string ProfileName(string path)
    {
        var behavior = Regex.Match(path, @"Behaviors\.(\d+)");
        string prefix = behavior.Success
            ? $"Fire mode {int.Parse(behavior.Groups[1].Value, CultureInfo.InvariantCulture) + 1}"
            : "Weapon";
        string kind = path.Contains("AlternateAttackData", StringComparison.Ordinal) ? "alternate damage"
                    : path.Contains("RadialDamage", StringComparison.Ordinal) ? "radial damage"
                    : path.Contains("DamageOverTime", StringComparison.Ordinal) ? "damage over time"
                    : "direct damage";
        return prefix + " · " + kind;
    }

    static int BehaviorIndex(string path)
    {
        var match = Regex.Match(path, @"Behaviors\.(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
    }

    static string FireControlName(int behaviorIndex, string stateKind)
    {
        string prefix = behaviorIndex >= 0 ? $"Fire mode {behaviorIndex + 1}" : "Weapon";
        string kind = stateKind.Replace("Weapon", "", StringComparison.Ordinal)
            .Replace("StateBehavior", "", StringComparison.Ordinal)
            .Replace("Behavior", "", StringComparison.Ordinal);
        kind = Regex.Replace(kind, "(?<=[a-z0-9])(?=[A-Z])", " ");
        return prefix + " · " + (kind.Length == 0 ? "firing controls" : kind.Trim());
    }
}

abstract class EeNode;
sealed class EeScalar(string value) : EeNode { public string Value { get; set; } = value; }
sealed record EeEntry(string Key, EeNode Value);
sealed class EeArray : EeNode { public List<EeNode> Items { get; } = []; }
sealed class EeObject : EeNode
{
    public List<EeEntry> Entries { get; } = [];
    public EeNode? Value(string key) => Entries.FirstOrDefault(entry => entry.Key == key)?.Value;
    public string? Scalar(string key) => Value(key) is EeScalar scalar ? scalar.Value : null;

    public void Add(string key, EeNode value) => Entries.Add(new EeEntry(key, value));

    public void SetScalar(string key, string value, string? before = null)
    {
        int existing = Entries.FindIndex(entry => entry.Key == key);
        if (existing >= 0) { Entries[existing] = new EeEntry(key, new EeScalar(value)); return; }
        int index = before == null ? -1 : Entries.FindIndex(entry => entry.Key == before);
        if (index < 0) Entries.Add(new EeEntry(key, new EeScalar(value)));
        else Entries.Insert(index, new EeEntry(key, new EeScalar(value)));
    }
}

static class EeParser
{
    public static EeObject Parse(string data)
    {
        var root = new EeObject();
        var stack = new Stack<EeNode>();
        stack.Push(root);
        string key = "";
        var buffer = new StringBuilder();
        bool pendingObjectOrArray = false;
        bool quoted = false;

        void Push(EeNode value)
        {
            if (stack.Peek() is EeArray array) array.Items.Add(value);
            else if (stack.Peek() is EeObject obj) { obj.Add(key, value); key = ""; }
            else throw new FormatException("Scalar cannot contain another metadata node.");
        }
        void PushAndAscend(EeNode value) { Push(value); stack.Push(value); }
        void Discharge()
        {
            Push(new EeScalar(buffer.ToString()));
            buffer.Clear();
        }

        foreach (char character in data)
        {
            if (quoted)
            {
                if (character == '"') { quoted = false; Discharge(); }
                else if (character != '\t') buffer.Append(character);
                continue;
            }
            if (character == '=')
            {
                if (pendingObjectOrArray)
                {
                    pendingObjectOrArray = false;
                    PushAndAscend(new EeObject());
                }
                key = buffer.ToString().Trim();
                buffer.Clear();
            }
            else if (character == '{')
            {
                buffer.Clear();
                if (pendingObjectOrArray) { pendingObjectOrArray = false; PushAndAscend(new EeArray()); }
                pendingObjectOrArray = true;
            }
            else if (character == ',')
            {
                if (pendingObjectOrArray)
                {
                    pendingObjectOrArray = false;
                    PushAndAscend(new EeArray());
                    string trimmed = buffer.ToString().Trim();
                    buffer.Clear(); buffer.Append(trimmed);
                    Discharge();
                }
                else
                {
                    string trimmed = buffer.ToString().Trim();
                    buffer.Clear(); buffer.Append(trimmed);
                    if (buffer.Length > 0) Discharge();
                }
            }
            else if (character == '}')
            {
                if (pendingObjectOrArray) { pendingObjectOrArray = false; PushAndAscend(new EeArray()); }
                string trimmed = buffer.ToString().Trim();
                buffer.Clear(); buffer.Append(trimmed);
                if (buffer.Length > 0) Discharge();
                if (stack.Count <= 1) throw new FormatException("Unexpected metadata closing brace.");
                stack.Pop();
            }
            else if (character == '"')
            {
                if (pendingObjectOrArray) { pendingObjectOrArray = false; PushAndAscend(new EeArray()); }
                if (buffer.Length == 0) quoted = true; else buffer.Append(character);
            }
            else if (character == '\n')
            {
                if (!pendingObjectOrArray)
                {
                    string trimmed = buffer.ToString().Trim();
                    buffer.Clear(); buffer.Append(trimmed);
                    if (buffer.Length > 0) Discharge();
                }
            }
            else if (character != '\r') buffer.Append(character);
        }
        if (stack.Count != 1 || buffer.Length != 0 || key.Length != 0)
            throw new FormatException("Incomplete EE metadata document.");
        return root;
    }

    public static string UnparseValue(EeNode node)
    {
        var output = new StringBuilder();
        Unparse(node, output);
        return output.ToString();
    }

    public static string UnparseRoot(EeObject root)
    {
        var output = new StringBuilder();
        foreach (var entry in root.Entries)
        {
            output.Append(entry.Key).Append('=');
            Unparse(entry.Value, output);
            output.Append('\n');
        }
        return output.ToString();
    }

    static void Unparse(EeNode node, StringBuilder output)
    {
        switch (node)
        {
            case EeScalar scalar:
                output.Append(scalar.Value.Length == 0 ? "\"\"" : scalar.Value);
                break;
            case EeObject obj:
                output.Append("{\n");
                foreach (var entry in obj.Entries)
                {
                    output.Append(entry.Key).Append('=');
                    Unparse(entry.Value, output);
                    output.Append('\n');
                }
                output.Append('}');
                break;
            case EeArray array:
                output.Append('{');
                foreach (var item in array.Items)
                {
                    output.Append('\n');
                    Unparse(item, output);
                    output.Append(',');
                }
                if (array.Items.Count > 0) { output.Length--; output.Append('\n'); }
                output.Append('}');
                break;
        }
    }
}
