using System.Text.Json;
using System.Text.Json.Serialization;
using Shelves.Core.Geometry;

namespace Shelves.Core.Model;

public enum IconSize { Small, Medium, Large }
public enum SortBy { None, Name, Type, Date }

/// <summary>An integer point (an icon position in Explorer's desktop list view).</summary>
public readonly record struct Pt(int X, int Y);

/// <summary>One shelf as saved.</summary>
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
    /// <summary>Shell parsing names of the desktop items in this shelf, in display order.</summary>
    public List<string> Items { get; set; } = [];
    /// <summary>A folder shown live instead of desktop items, or null.</summary>
    public string? Portal { get; set; }

    public ShelfModel Clone() => new()
    {
        Id = Id, Title = Title, Monitor = Monitor, Rel = Rel, Edge = Edge, Rolled = Rolled,
        IconSize = IconSize, Sort = Sort, Items = [.. Items], Portal = Portal,
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

    public ShelfModel? ShelfOf(string item) =>
        Shelves.FirstOrDefault(s => s.Portal == null && s.Items.Contains(item, StringComparer.OrdinalIgnoreCase));

    public IEnumerable<string> AllShelved() => Shelves.Where(s => s.Portal == null).SelectMany(s => s.Items);

    public Layout Clone() => new()
    {
        Version = Version,
        Shelves = Shelves.Select(s => s.Clone()).ToList(),
        Home = new(Home, StringComparer.OrdinalIgnoreCase),
    };

    /// <summary>Takes an item out of whichever shelf has it. Returns that shelf, or null.</summary>
    public ShelfModel? Unshelve(string item)
    {
        var s = ShelfOf(item);
        s?.Items.RemoveAll(i => string.Equals(i, item, StringComparison.OrdinalIgnoreCase));
        return s;
    }

    /// <summary>Puts an item in <paramref name="target"/> (at <paramref name="index"/>, or the end), out of any other shelf.</summary>
    public void Shelve(string item, ShelfModel target, int index = -1)
    {
        Unshelve(item);
        if (index < 0 || index > target.Items.Count) target.Items.Add(item);
        else target.Items.Insert(index, item);
    }

    /// <summary>A file was renamed on the desktop: follow it.</summary>
    public bool Rename(string from, string to)
    {
        bool hit = false;
        foreach (var s in Shelves.Where(s => s.Portal == null))
            for (int i = 0; i < s.Items.Count; i++)
                if (string.Equals(s.Items[i], from, StringComparison.OrdinalIgnoreCase)) { s.Items[i] = to; hit = true; }
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
        foreach (var s in l.Shelves) { s.Items ??= []; s.Title ??= ""; s.Monitor ??= ""; }
        l.Version = Layout.CurrentVersion;
        return l;
    }

    public static void Save(string path, Layout l) => Json.Write(path, l);
}
