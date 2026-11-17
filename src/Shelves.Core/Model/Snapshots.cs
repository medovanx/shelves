namespace Shelves.Core.Model;

/// <summary>A saved copy of the layout.</summary>
public sealed class Snapshot
{
    public string Name { get; set; } = "";
    public DateTime Time { get; set; }
    /// <summary>Taken by Shelves (daily, before an update) rather than by the user. Only these are rotated out.</summary>
    public bool Auto { get; set; }
    public Layout Layout { get; set; } = new();
}

/// <summary>Snapshots as JSON files in one folder.</summary>
public sealed class SnapshotStore(string dir)
{
    public const int KeepAuto = 10;

    public IReadOnlyList<(string Path, Snapshot Snap)> List() =>
        Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*.json")
                .Select(p => (p, Json.Read<Snapshot>(p)))
                .Where(x => x.Item2 != null)
                .Select(x => (x.p, x.Item2!))
                .OrderByDescending(x => x.Item2.Time)
                .ToList()
            : [];

    public string Save(Layout layout, string name, bool auto, DateTime now)
    {
        var snap = new Snapshot { Name = name, Time = now, Auto = auto, Layout = layout.Clone() };
        string path = Path.Combine(dir, $"{now:yyyyMMdd-HHmmss-fff}{(auto ? "-auto" : "")}.json");
        Json.Write(path, snap);
        if (auto) Rotate();
        return path;
    }

    /// <summary>Keeps the newest <see cref="KeepAuto"/> automatic snapshots; the user's own are never removed.</summary>
    public void Rotate()
    {
        foreach (var (p, _) in List().Where(x => x.Snap.Auto).Skip(KeepAuto))
            try { File.Delete(p); } catch (IOException) { }
    }

    /// <summary>True when no automatic snapshot was taken on <paramref name="today"/>'s date.</summary>
    public bool NeedsDaily(DateTime today) => !List().Any(x => x.Snap.Auto && x.Snap.Time.Date == today.Date);

    public void Delete(string path) { try { File.Delete(path); } catch (IOException) { } }
}

public static class Health
{
    /// <summary>
    /// After Explorer restarts or the desktop is reshuffled: true when most shelved items can no longer be found
    /// among the desktop's items (at least 3, and more than half), a sign the layout is not what the user had.
    /// </summary>
    public static bool LooksScrambled(Layout l, IReadOnlySet<string> present)
    {
        var shelved = l.AllShelved().ToList();
        int missing = shelved.Count(i => !present.Contains(i));
        return missing >= 3 && missing * 2 > shelved.Count;
    }
}
