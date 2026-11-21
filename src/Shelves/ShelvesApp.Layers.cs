using Shelves.UI.Desk;

namespace Shelves;

/// <summary>
/// Shelves drawing the desktop itself (Settings.DrawDesktop): one <see cref="DesktopLayer"/> per monitor shows the
/// icons, and Explorer's desktop list is hidden but kept as the store of items and positions. Moves go straight
/// into that list, so they stick when Shelves exits, and Explorer never handles a drop on the desktop, which is
/// what used to shuffle icons.
/// </summary>
sealed partial class ShelvesApp
{
    public List<DesktopLayer> Layers { get; } = [];
    Pt[] layerPositions = [];
    readonly Dictionary<string, bool> dropTargets = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<int, Bitmap?> linkOverlays = [];
    /// <summary>Icons cut to the clipboard, drawn faded until they are pasted.</summary>
    public HashSet<string> CutNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Point? LastDesktopPoint { get; private set; }

    // Explorer's icon box, measured from its own list: where the box starts from an item's position, its size,
    // and the height of the part that holds the icon (the label goes below it).
    Point boxOffset = new(-11, 3);
    Size boxSize = new(75, 90);
    public int IconAreaHeight { get; private set; } = 57;
    public int DesktopIconPx { get; private set; } = 48;

    bool LayerMode => Settings.DrawDesktop && !ShotMode && View.Attached;

    /// <summary>Creates, keeps or removes the layers to match the setting and the monitors, and hides Explorer's list under them.</summary>
    void EnsureLayers()
    {
        if (!LayerMode)
        {
            if (Layers.Count == 0) return;
            foreach (var l in Layers) { l.Close(); l.Dispose(); }
            Layers.Clear();
            if (View.Attached && !hiddenAll) Native.ShowWindow(View.ListView, 5 /* SW_SHOWNA */);
            return;
        }
        var screens = Screen.AllScreens;
        foreach (var l in Layers.Where(l => !screens.Any(s => s.DeviceName == l.Screen.DeviceName)).ToList())
        {
            l.Close(); l.Dispose(); Layers.Remove(l);
        }
        foreach (var s in screens)
        {
            var l = Layers.FirstOrDefault(x => x.Screen.DeviceName == s.DeviceName);
            if (l == null)
            {
                l = new DesktopLayer(this, s);
                Layers.Add(l);
                if (!hiddenAll && !NoIcons) l.Show();
            }
            else if (l.Screen.Bounds != s.Bounds) l.SetScreen(s);
        }
        HideExplorerIcons();
        ReloadLayers(full: true);
        PlaceAll();
    }

    /// <summary>Windows' "Show desktop icons" is off (from the desktop's View menu).</summary>
    bool NoIcons => (View.FolderFlags & 0x1000 /* FWF_NOICONS */) != 0;

    void HideExplorerIcons()
    {
        if (View.Attached && Native.IsWindowVisible(View.ListView)) Native.ShowWindow(View.ListView, 0);
    }

    /// <summary>The layer mode part of the one-second tick: keep Explorer's list hidden and follow its changes.</summary>
    void TickLayers()
    {
        if (!LayerMode) { if (Layers.Count > 0) EnsureLayers(); return; }
        if (Layers.Count == 0 || Layers.Count != Screen.AllScreens.Length) { EnsureLayers(); return; }
        HideExplorerIcons();
        bool show = !hiddenAll && !NoIcons;
        foreach (var l in Layers)
        {
            if (show && !l.Visible) { l.Show(); l.PlaceAboveDesktop(); }
            else if (!show && l.Visible) l.Hide();
        }
        var now = View.Positions();
        if (!now.SequenceEqual(layerPositions)) ReloadLayers();
    }

    /// <summary>Reads Explorer's list and gives each layer the icons on its monitor.</summary>
    public void ReloadLayers(bool full = false)
    {
        if (!LayerMode || Layers.Count == 0) return;
        if (full) { Keeper.Sync(); dropTargets.Clear(); }
        var items = View.Items();
        // Icons Explorer just bumped and that are about to go back (KeepPositions): keep showing the old picture
        // rather than flash them at the wrong spots.
        if (kept is { } k && items.Any(i => k.TryGetValue(i.Name, out var want) && i.Pos != want && !DesktopView.IsParked(i.Pos)))
            return;
        layerPositions = View.Positions();
        var shelved = new HashSet<string>(Layout.AllShelved(), StringComparer.OrdinalIgnoreCase);
        var visible = items.Where(i => !DesktopView.IsParked(i.Pos) && !shelved.Contains(i.Name)).ToList();
        if (full || visible.Count > 0) Measure(visible);
        var origin = View.Origin;
        var perLayer = Layers.ToDictionary(l => l, _ => new List<LayerIcon>());
        foreach (var it in visible)
        {
            var box = new Rectangle(origin.X + it.Pos.X + boxOffset.X, origin.Y + it.Pos.Y + boxOffset.Y, boxSize.Width, boxSize.Height);
            var center = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
            var layer = Layers.FirstOrDefault(l => l.Screen.Bounds.Contains(center))
                        ?? Layers.FirstOrDefault(l => l.Screen.Bounds.IntersectsWith(box));
            if (layer == null) continue;
            var b = layer.Screen.Bounds;
            string label = View.Label(it.Index) is { Length: > 0 } t ? t : IconCache.Label(it.Name);
            perLayer[layer].Add(new LayerIcon(it.Name, it.Index, it.Pos, label, box with { X = box.X - b.X, Y = box.Y - b.Y }));
        }
        foreach (var (l, list) in perLayer) l.SetIcons(list);
    }

    /// <summary>Takes Explorer's icon size and icon box from its own list, so the icons sit where Explorer keeps them.</summary>
    void Measure(List<DesktopItem> visible)
    {
        int px = View.IconSize;
        if (px > 0 && px != DesktopIconPx) { DesktopIconPx = px; }
        var sample = visible.FirstOrDefault();
        if (sample == null || View.ItemRect(sample.Index, 1 /* LVIR_ICON */) is not { } icon || icon.Width <= 0) return;
        boxOffset = new Point(icon.X - sample.Pos.X, icon.Y - sample.Pos.Y);
        IconAreaHeight = icon.Height;
        boxSize = new Size(icon.Width, Math.Max(icon.Height, View.Spacing.Height - 13));
    }

    public DesktopLayer? LayerAt(Point screen)
    {
        var h = Native.WindowFromPoint(new Native.POINT { X = screen.X, Y = screen.Y });
        return Layers.FirstOrDefault(l => l.IsHandleCreated && l.Handle == h);
    }

    /// <summary>
    /// The highest of the layers (each sits right above Progman): shelves go right above it. Walks up from Progman,
    /// since a shelf placed before the layers can sit between them and Progman for a moment.
    /// </summary>
    IntPtr DesktopFloor()
    {
        var progman = Native.FindWindow("Progman", null);
        if (Layers.Count == 0 || progman == IntPtr.Zero) return progman;
        var handles = Layers.Where(l => l.IsHandleCreated).Select(l => l.Handle).ToHashSet();
        IntPtr h = progman, floor = progman;
        int found = 0;
        for (int guard = 0; guard < 200 && found < handles.Count; guard++)
        {
            h = GetWindowPrev(h);
            if (h == IntPtr.Zero) break;
            if (handles.Contains(h)) { floor = h; found++; }
        }
        return floor;
    }

    static IntPtr GetWindowPrev(IntPtr h) => GetWindow(h, 3 /* GW_HWNDPREV */);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

    public void NoteDesktopPoint(Point p)
    {
        LastDesktopPoint = p;
        lastDesktopMenuAt = p;
    }

    public bool IsDropTarget(string name)
    {
        if (!dropTargets.TryGetValue(name, out var v)) dropTargets[name] = v = ShellOps.IsDropTarget(View, name);
        return v;
    }

    public Bitmap? LinkOverlay(int px)
    {
        if (!linkOverlays.TryGetValue(px, out var b)) linkOverlays[px] = b = ShellOps.LinkOverlay(px);
        return b;
    }

    public void MarkCut(IEnumerable<string> names)
    {
        CutNames.Clear();
        CutNames.UnionWith(names);
        foreach (var l in Layers) l.RenderAll();
    }

    /// <summary>Every desktop item's name now (shelved ones too).</summary>
    public HashSet<string> DesktopNames() => new(View.Items().Select(i => i.Name), StringComparer.OrdinalIgnoreCase);

    /// <summary>After a shell operation (they finish on their own time): read Explorer's list again, twice.</summary>
    public void CheckSoon()
    {
        foreach (int ms in new[] { 300, 1500 })
        {
            var t = new System.Windows.Forms.Timer { Interval = ms };
            t.Tick += (_, _) => { t.Stop(); t.Dispose(); Keeper.Check(); ReloadLayers(); };
            t.Start();
        }
    }

    /// <summary>
    /// Moves desktop icons by a drag's distance. With "Align icons to grid" each lands on the nearest free cell;
    /// icons are kept on the monitor they were dropped on.
    /// </summary>
    public void MoveIcons(List<string> names, int dx, int dy, Point dropAt)
    {
        if (!View.Attached) return;
        var items = View.Items();
        var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var moving = items.Where(i => set.Contains(i.Name) && !DesktopView.IsParked(i.Pos)).ToList();
        var targets = moving.Select(i => new Pt(i.Pos.X + dx, i.Pos.Y + dy)).ToList();
        Place(items, moving, targets, Screen.FromPoint(dropAt).WorkingArea);
    }

    /// <summary>Puts icons at a screen point: the first one centred there, the others in the next free spots.</summary>
    public void PlaceAt(IReadOnlyCollection<string> names, Point screen)
    {
        if (!View.Attached || names.Count == 0) return;
        var items = View.Items();
        var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var moving = items.Where(i => set.Contains(i.Name)).ToList();
        var origin = View.Origin;
        var sp = View.Spacing;
        var first = new Pt(screen.X - origin.X - boxOffset.X - boxSize.Width / 2, screen.Y - origin.Y - boxOffset.Y - IconAreaHeight / 2);
        // Without the grid, the rest go below the first, a column at a time, a cell apart.
        var targets = moving.Select((_, k) => new Pt(first.X + k / 6 * sp.Width, first.Y + k % 6 * sp.Height)).ToList();
        Place(items, moving, targets, Screen.FromPoint(screen).WorkingArea);
    }

    void Place(List<DesktopItem> items, List<DesktopItem> moving, List<Pt> targets, Rectangle work)
    {
        var origin = View.Origin;
        var sp = View.Spacing;
        int w = Math.Max(sp.Width, 1), h = Math.Max(sp.Height, 1);
        var shelved = new HashSet<string>(Layout.AllShelved(), StringComparer.OrdinalIgnoreCase);
        var movingNames = new HashSet<string>(moving.Select(m => m.Name), StringComparer.OrdinalIgnoreCase);
        var taken = new HashSet<Pt>(items.Where(i => !DesktopView.IsParked(i.Pos) && !shelved.Contains(i.Name) && !movingNames.Contains(i.Name))
            .Select(i => IconKeeper.NearestCell(i.Pos, origin, w, h, out _)));
        // Keep each icon's box inside the work area it lands on.
        int minX = work.Left - origin.X - boxOffset.X, maxX = work.Right - origin.X - boxOffset.X - boxSize.Width;
        int minY = work.Top - origin.Y - boxOffset.Y, maxY = work.Bottom - origin.Y - boxOffset.Y - boxSize.Height;
        for (int k = 0; k < moving.Count; k++)
        {
            var p = new Pt(Math.Clamp(targets[k].X, minX, Math.Max(minX, maxX)), Math.Clamp(targets[k].Y, minY, Math.Max(minY, maxY)));
            if (Settings.AlignToGrid)
            {
                var c = IconKeeper.NearestCell(p, origin, w, h, out var area);
                p = IconKeeper.FreeCellNear(c, area, origin, w, h, taken);
                taken.Add(p);
            }
            View.Move(moving[k].Index, p);
            Layout.Home.Remove(moving[k].Name);
        }
        // Explorer keeps moves only once it saves them; a Refresh would otherwise put the icons back.
        View.SaveState();
        ReloadLayers();
    }

    /// <summary>Our icons dropped onto a folder, the Recycle Bin or a program: the shell does what Explorer would.</summary>
    public void DropOnItem(IntPtr hwnd, List<string> names, string target, Point at, Keys mods)
    {
        using var group = ItemGroup.FromDesktop(View, names);
        if (group == null) return;
        var data = ShellOps.DataObject(hwnd, group);
        var dt = ShellOps.DropTarget(hwnd, target);
        if (data == null || dt == null) return;
        uint keys = 1 /* MK_LBUTTON */ | ((mods & Keys.Control) != 0 ? 8u : 0) | ((mods & Keys.Shift) != 0 ? 4u : 0) | ((mods & Keys.Alt) != 0 ? 32u : 0);
        var p = new Native.POINT { X = at.X, Y = at.Y };
        uint effect = 7; // copy, move, link: the target picks, as with a real drag
        try
        {
            dt.DragEnter(data, keys, p, ref effect);
            if (effect == 0) { dt.DragLeave(); return; }
            dt.Drop(data, keys & ~1u, p, ref effect);
        }
        catch (System.Runtime.InteropServices.COMException ex) { Log.Error(ex); }
        CheckSoon();
    }

    /// <summary>
    /// New items (from New, Paste, or files dropped on the desktop) appear where it happened. The shell makes them
    /// on its own time, so this watches for them for a few seconds. A new one Explorer wants a name for gets our
    /// name box, since Explorer's own one would open in its hidden list.
    /// </summary>
    public void PlaceNew(HashSet<string> before, Point screen, bool rename, int waitMs = 4000)
    {
        CutNames.Clear();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var t = new System.Windows.Forms.Timer { Interval = 120 };
        t.Tick += (_, _) =>
        {
            if (sw.ElapsedMilliseconds > waitMs || !View.Attached) { t.Stop(); t.Dispose(); ReloadLayers(); return; }
            var added = DesktopNames().Where(n => !before.Contains(n)).ToList();
            if (added.Count == 0) return;
            t.Stop();
            t.Dispose();
            Keeper.Sync();
            PlaceAt(added, screen);
            if (rename && added.Count == 1 && View.Renaming)
            {
                View.CancelEdit();
                var layer = Layers.FirstOrDefault(l => l.Icons.Any(i => string.Equals(i.Name, added[0], StringComparison.OrdinalIgnoreCase)));
                layer?.BeginRename(added[0]);
            }
        };
        t.Start();
    }

    /// <summary>Shown after the mouse draws a box on the desktop with the right button.</summary>
    public void OfferNewShelf(Rectangle r, Point at) => OnBoxDrawn(r, at);

    /// <summary>Where each visible (not shelved) desktop icon is now, by name.</summary>
    /// <summary>Positions being kept after a desktop-menu command (see <see cref="KeepPositions"/>), or null.</summary>
    Dictionary<string, Pt>? kept;

    public Dictionary<string, Pt> VisiblePositions()
    {
        var shelved = new HashSet<string>(Layout.AllShelved(), StringComparer.OrdinalIgnoreCase);
        return View.Items().Where(i => !DesktopView.IsParked(i.Pos) && !shelved.Contains(i.Name))
            .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Pos, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>For a while, puts back any of these icons that Explorer moves (it reloads its list on Refresh).</summary>
    public void KeepPositions(Dictionary<string, Pt> positions, int ms)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var t = new System.Windows.Forms.Timer { Interval = 40 };
        kept = positions;
        t.Tick += (_, _) =>
        {
            if (sw.ElapsedMilliseconds > ms || !View.Attached)
            {
                t.Stop(); t.Dispose();
                if (kept == positions) kept = null;
                ReloadLayers();
                return;
            }
            bool moved = false;
            var shelved = new HashSet<string>(Layout.AllShelved(), StringComparer.OrdinalIgnoreCase);
            foreach (var it in View.Items())
            {
                // Not one of them, already in place, or shelved since (the shelf hides it).
                if (!positions.TryGetValue(it.Name, out var want) || it.Pos == want || shelved.Contains(it.Name)) continue;
                View.Move(it.Index, want);
                moved = true;
            }
            if (moved) { View.SaveState(); ReloadLayers(); }
        };
        t.Start();
    }

    /// <summary>The setting changed (tray menu or settings).</summary>
    public void SetDrawDesktop(bool on)
    {
        Settings.DrawDesktop = on;
        SaveSettings();
        EnsureLayers();
        if (!on) RefreshIconRects();
    }

    /// <summary>A crash or a kill must never leave the desktop without icons: show Explorer's list again (no COM).</summary>
    public static void EmergencyShowIcons()
    {
        try
        {
            var progman = Native.FindWindow("Progman", null);
            var dv = Native.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            var lv = dv == IntPtr.Zero ? IntPtr.Zero : Native.FindWindowEx(dv, IntPtr.Zero, "SysListView32", null);
            if (lv != IntPtr.Zero) Native.ShowWindow(lv, 5);
        }
        catch { }
    }
}
