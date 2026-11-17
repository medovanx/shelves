using System.Text.Json;
using System.Text.Json.Serialization;
using Shelves.Core.Geometry;

namespace Shelves.Core.Model;

public enum IconSize { Small, Medium, Large }
public enum SortBy { None, Name, Type, Date }

/// <summary>An integer point (an icon position in Explorer's desktop list view).</summary>
public readonly record struct Pt(int X, int Y);

/// <summary>One tab of a shelf: its own desktop icons, or a folder shown live.</summary>
public sealed class ShelfTab
{
    public string Title { get; set; } = "";
    /// <summary>Shell parsing names of the desktop items in this tab, in display order.</summary>
    public List<string> Items { get; set; } = [];
    /// <summary>A folder shown live instead of desktop items, or null.</summary>
    public string? Portal { get; set; }

    public ShelfTab Clone() => new() { Title = Title, Items = [.. Items], Portal = Portal };
}

/// <summary>One shelf as saved. A shelf has at least one tab; the tab strip shows once there are two.</summary>
public sealed class ShelfModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    /// <summary>The monitor's device name, such as \\.\DISPLAY2. Kept even while that monitor is unplugged.</summary>
    public string Monitor { get; set; } = "";
    /// <summary>The open shelf as fractions of that monitor's work area.</summary>
    public RelBox Rel { get; set; }
    public Edge Edge { get; set; }
    public bool Rolled { get; set; }
    public IconSize IconSize { get; set; } = IconSize.Medium;
    public SortBy Sort { get; set; }
    public List<ShelfTab> Tabs { get; set; } = [new()];
    /// <summary>The tab shown now.</summary>
    public int Active { get; set; }

    /// <summary>The tab shown now (made valid if the index is out of range).</summary>
    [JsonIgnore]
    public ShelfTab Current
    {
        get
        {
            if (Tabs.Count == 0) Tabs.Add(new ShelfTab());
            Active = Math.Clamp(Active, 0, Tabs.Count - 1);
            return Tabs[Active];
        }
    }

    /// <summary>The shown tab's desktop items.</summary>
    [JsonIgnore]
    public List<string> Items { get => Current.Items; set => Current.Items = value; }

    /// <summary>The shown tab's folder, or null.</summary>
    [JsonIgnore]
    public string? Portal { get => Current.Portal; set => Current.Portal = value; }

    /// <summary>Desktop items in every tab (portal tabs hold none).</summary>
    [JsonIgnore]
    public IEnumerable<string> AllItems => Tabs.Where(t => t.Portal == null).SelectMany(t => t.Items);

    /// <summary>The tab that holds a desktop item, or null.</summary>
    public ShelfTab? TabOf(string item) =>
        Tabs.FirstOrDefault(t => t.Portal == null && t.Items.Contains(item, StringComparer.OrdinalIgnoreCase));

    // Layouts saved before tabs had the items and folder on the shelf itself: they become its first tab.
    [JsonPropertyName("Items"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? LegacyItems { get => null; set { if (value != null) FirstTab.Items = value; } }

    [JsonPropertyName("Portal"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyPortal { get => null; set { if (value != null) FirstTab.Portal = value; } }

    ShelfTab FirstTab { get { if (Tabs.Count == 0) Tabs.Add(new ShelfTab()); return Tabs[0]; } }
    /// <summary>This shelf's own colour ("#RRGGBB"), or null to use the setting for all shelves.</summary>
    public string? Tint { get; set; }
    /// <summary>This shelf's own opacity (0 to 100), or null to use the setting for all shelves.</summary>
    public int? Opacity { get; set; }

    public ShelfModel Clone() => new()
    {
        Id = Id, Title = Title, Monitor = Monitor, Rel = Rel, Edge = Edge, Rolled = Rolled,
        IconSize = IconSize, Sort = Sort, Tabs = Tabs.Select(t => t.Clone()).ToList(), Active = Active, Tint = Tint, Opacity = Opacity,
    };
}

/// <summary>Every shelf, plus where each shelved icon sat on the desktop before it was shelved.</summary>
public sealed class Layout
{
    public const int CurrentVersion = 1;
    public int Version { get; set; } = CurrentVersion;
    public List<ShelfModel> Shelves { get; set; } = [];
    /// <summary>Parsing name to the icon's desktop position before Shelves hid it, used to put it back.</summary>
    public Dictionary<string, Pt> Home { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public ShelfModel? ShelfOf(string item) => Shelves.FirstOrDefault(s => s.TabOf(item) != null);

    public IEnumerable<string> AllShelved() => Shelves.SelectMany(s => s.AllItems);

    public Layout Clone() => new()
    {
        Version = Version,
        Shelves = Shelves.Select(s => s.Clone()).ToList(),
        Home = new(Home, StringComparer.OrdinalIgnoreCase),
    };

    /// <summary>Takes an item out of whichever shelf (and tab) has it. Returns that shelf, or null.</summary>
    public ShelfModel? Unshelve(string item)
    {
        var s = ShelfOf(item);
        s?.TabOf(item)?.Items.RemoveAll(i => string.Equals(i, item, StringComparison.OrdinalIgnoreCase));
        return s;
    }

    /// <summary>
    /// Puts an item in <paramref name="target"/>'s tab <paramref name="tab"/> (its shown tab when -1), at
    /// <paramref name="index"/> or the end, out of any other shelf or tab.
    /// </summary>
    public void Shelve(string item, ShelfModel target, int index = -1, int tab = -1)
    {
        Unshelve(item);
        var t = tab >= 0 && tab < target.Tabs.Count ? target.Tabs[tab] : target.Current;
        if (index < 0 || index > t.Items.Count) t.Items.Add(item);
        else t.Items.Insert(index, item);
    }

    /// <summary>A file was renamed on the desktop: follow it.</summary>
    public bool Rename(string from, string to)
    {
        bool hit = false;
        foreach (var t in Shelves.SelectMany(s => s.Tabs).Where(t => t.Portal == null))
            for (int i = 0; i < t.Items.Count; i++)
                if (string.Equals(t.Items[i], from, StringComparison.OrdinalIgnoreCase)) { t.Items[i] = to; hit = true; }
        if (Home.Remove(from, out var p)) Home[to] = p;
        return hit;
    }
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
    };

    public static T? Read<T>(string path) where T : class
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) : null; }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }

    /// <summary>Writes to a temp file and swaps it in, so a crash mid-write never leaves half a file.</summary>
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }
}

public static class LayoutStore
{
    /// <summary>Loads the layout, upgrading older versions. A missing or broken file gives an empty layout.</summary>
    public static Layout Load(string path)
    {
        var l = Json.Read<Layout>(path) ?? new Layout();
        return Migrate(l);
    }

    public static Layout Migrate(Layout l)
    {
        // Version 1 is the first. Later versions upgrade here, one step at a time.
        l.Home = new(l.Home ?? [], StringComparer.OrdinalIgnoreCase);
        l.Shelves ??= [];
        foreach (var s in l.Shelves)
        {
            s.Tabs ??= [];
            s.Tabs.RemoveAll(t => t == null);
            if (s.Tabs.Count == 0) s.Tabs.Add(new ShelfTab());
            foreach (var t in s.Tabs) { t.Items ??= []; t.Title ??= ""; }
            s.Active = Math.Clamp(s.Active, 0, s.Tabs.Count - 1);
            s.Title ??= "";
            s.Monitor ??= "";
        }
        l.Version = Layout.CurrentVersion;
        return l;
    }

    public static void Save(string path, Layout l) => Json.Write(path, l);
}
