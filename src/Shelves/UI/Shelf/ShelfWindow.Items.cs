namespace Shelves.UI.Shelf;

sealed partial class ShelfWindow
{
    readonly List<ShelfItem> items = [];
    readonly HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);
    ShelfItem? hoverItem;
    int scroll;
    string? anchor;

    FileSystemWatcher? watcher;
    readonly System.Windows.Forms.Timer portalTimer = new() { Interval = 300 };

    int IconPx => (int)((Model.IconSize switch { IconSize.Small => 32, IconSize.Large => 64, _ => 48 }) * S);
    int CellW => (int)((Model.IconSize switch { IconSize.Small => 76, IconSize.Large => 108, _ => 90 }) * S);
    int LabelH => (int)(16 * S * app.Settings.LabelLines);
    int CellH => IconPx + (int)(14 * S) + LabelH;

    public IEnumerable<string> ItemNames => items.Select(i => i.Name);

    /// <summary>Reads the shelf's items: its desktop icons that exist, or the portal folder's files.</summary>
    public void LoadItems()
    {
        var old = items.ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
        items.Clear();
        IEnumerable<string> names;
        if (Model.Portal is { } dir)
        {
            WatchPortal(dir);
            names = Directory.Exists(dir)
                ? SafeList(dir)
                : [];
        }
        else
        {
            var present = new HashSet<string>(app.Keeper.Last.Select(i => i.Name), StringComparer.OrdinalIgnoreCase);
            names = Model.Items.Where(present.Contains);
        }
        foreach (var n in names)
            items.Add(old.TryGetValue(n, out var it) ? it : new ShelfItem(n, IconCache.Label(n)));
        Sort();
        selected.IntersectWith(items.Select(i => i.Name));
        LayoutItems();
        if (IsHandleCreated) Apply();
    }

    static IEnumerable<string> SafeList(string dir)
    {
        try
        {
            var di = new DirectoryInfo(dir);
            return di.EnumerateFileSystemInfos()
                .Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                .OrderBy(f => f is FileInfo).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(f => f.FullName).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    void WatchPortal(string dir)
    {
        if (watcher?.Path == dir) return;
        watcher?.Dispose();
        watcher = null;
        if (!Directory.Exists(dir)) return;
        watcher = new FileSystemWatcher(dir) { IncludeSubdirectories = false, EnableRaisingEvents = true };
        portalTimer.Tick -= PortalTick;
        portalTimer.Tick += PortalTick;
        FileSystemEventHandler h = (_, e) => BeginInvoke(() => { IconCache.Forget(e.FullPath); portalTimer.Stop(); portalTimer.Start(); });
        watcher.Created += h; watcher.Deleted += h; watcher.Changed += h;
        watcher.Renamed += (_, e) => BeginInvoke(() => { IconCache.Forget(e.OldFullPath); portalTimer.Stop(); portalTimer.Start(); });
    }

    void PortalTick(object? s, EventArgs e) { portalTimer.Stop(); LoadItems(); }

    void Sort()
    {
        switch (Model.Sort)
        {
            case SortBy.Name:
                items.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase));
                break;
            case SortBy.Type:
                items.Sort((a, b) =>
                {
                    int c = string.Compare(TypeKey(a.Name), TypeKey(b.Name), StringComparison.OrdinalIgnoreCase);
                    return c != 0 ? c : string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase);
                });
                break;
            case SortBy.Date:
                items.Sort((a, b) => Modified(b.Name).CompareTo(Modified(a.Name)));
                break;
        }
    }

    // Folders and virtual items first, then by extension.
    static string TypeKey(string n) => n.StartsWith("::") ? "0" : Directory.Exists(n) ? "1" : "2" + Path.GetExtension(n);
    static DateTime Modified(string n) { try { return File.GetLastWriteTimeUtc(n); } catch { return DateTime.MinValue; } }

    /// <summary>Places icons in a grid that fills the shelf's width.</summary>
    void LayoutItems()
    {
        var c = ContentRect;
        int cw = CellW, ch = CellH;
        int cols = Math.Max(1, c.Width / cw);
        int w = c.Width / cols;
        for (int i = 0; i < items.Count; i++)
        {
            int col = i % cols, row = i / cols;
            if (L.Ar) col = cols - 1 - col;
            items[i].Cell = new Rectangle(c.X + col * w + (w - cw) / 2, c.Y + row * ch, cw, ch);
        }
        scroll = Math.Clamp(scroll, 0, Math.Max(0, ContentHeight - c.Height));
    }

    int ContentHeight => items.Count == 0 ? 0 : items.Max(i => i.Cell.Bottom) - ContentRect.Y;

    /// <summary>A window point in the open shelf's coordinates (content stays put while the window rolls).</summary>
    Point ToOpen(Point p)
    {
        var shift = ContentShift(Target());
        return new Point(p.X - shift.X, p.Y - shift.Y);
    }

    ShelfItem? ItemAt(Point window)
    {
        if (!IsOpen && !peeking) return null;
        var p = ToOpen(window);
        if (!ContentRect.Contains(p)) return null;
        p.Y += scroll;
        return items.FirstOrDefault(i => Rectangle.Inflate(i.Cell, -(int)(4 * S), 0).Contains(p));
    }

    List<string> SelectedNames() => items.Where(i => selected.Contains(i.Name)).Select(i => i.Name).ToList();

    /// <summary>The selected items as the shell needs them: from the desktop's own folder, or the portal's.</summary>
    ItemGroup? Group(IReadOnlyList<string> names) =>
        Model.Portal == null ? ItemGroup.FromDesktop(app.View, names) : ItemGroup.FromPaths(names);

    void OpenItem(ShelfItem it) => ShellOps.Open(Handle, it.Name);

    void ShellMenu(Point screen)
    {
        var names = SelectedNames();
        if (names.Count == 0) return;
        using var group = Group(names);
        if (group == null) return;
        menuOpen = true;
        try
        {
            string? verb = ShellOps.ContextMenu(Handle, group, screen, (ModifierKeys & Keys.Shift) != 0);
            if (verb == "rename" && names.Count == 1) BeginRename(items.First(i => i.Name == names[0]));
        }
        finally { menuOpen = false; }
        app.Keeper.Check();
    }

    void DeleteSelected()
    {
        var names = SelectedNames();
        if (names.Count == 0) return;
        using var group = Group(names);
        if (group != null) ShellOps.Invoke(Handle, group, "delete");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (renameBox != null) return;
        switch (e.KeyCode)
        {
            case Keys.Enter:
                foreach (var it in items.Where(i => selected.Contains(i.Name)).Take(15)) OpenItem(it);
                break;
            case Keys.F2:
                if (selected.Count == 1) BeginRename(items.First(i => selected.Contains(i.Name)));
                else if (selected.Count == 0) BeginTitleRename();
                break;
            case Keys.Delete:
                DeleteSelected();
                break;
            case Keys.A when e.Control:
                selected.UnionWith(items.Select(i => i.Name));
                Apply();
                break;
            case Keys.Escape:
                selected.Clear();
                Apply();
                break;
            case Keys.Apps:
                if (selected.Count > 0)
                {
                    var c = items.First(i => selected.Contains(i.Name)).Cell;
                    ShellMenu(PointToScreen(new Point(c.X + c.Width / 2, c.Y + c.Height / 2 - scroll)));
                }
                break;
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        int max = Math.Max(0, ContentHeight - ContentRect.Height);
        if (max == 0) return;
        scroll = Math.Clamp(scroll - e.Delta * CellH / 240, 0, max);
        Apply();
    }
}
