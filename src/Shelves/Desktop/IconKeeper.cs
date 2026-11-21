namespace Shelves.Desktop;

/// <summary>
/// Keeps Explorer's copies of shelved icons out of sight and puts them back when they leave a shelf. Explorer
/// reloads its saved layout on F5 and after a restart, which brings parked icons back; <see cref="Check"/> notices
/// (cheaply, without COM) and parks them again.
/// </summary>
sealed class IconKeeper(DesktopView view, Func<Layout> layout)
{
    /// <summary>Raised when the layout's saved home positions change, so it can be saved.</summary>
    public event Action? HomesChanged;
    /// <summary>Raised after a full sync with the names of every desktop item, so shelves can refresh.</summary>
    public event Action<IReadOnlyList<DesktopItem>>? Synced;

    int lastCount = -1, lastParked = -1;
    /// <summary>The shelved icons' list-view indices at the last full sync, with their off-screen spots.</summary>
    Dictionary<int, Pt> slotByIndex = [];
    /// <summary>Set when icons were re-hidden by index (fast path); a full sync confirms by name soon after.</summary>
    public bool NeedsConfirm { get; private set; }
    string lastNames = "";

    /// <summary>While true (an icon drag is running), checks do nothing, so Explorer's drop is not undone mid-way.</summary>
    public bool Paused { get; set; }
    public IReadOnlyList<DesktopItem> Last { get; private set; } = [];

    /// <summary>Full pass: parks every shelved icon that is showing, puts back any parked icon that is no longer shelved.</summary>
    public IReadOnlyList<DesktopItem> Sync()
    {
        if (!view.Attached) return Last;
        var l = layout();
        var shelved = new HashSet<string>(l.AllShelved(), StringComparer.OrdinalIgnoreCase);
        var items = view.Items();
        bool homes = false;
        int parked = 0, slot = 0;
        var slots = new Dictionary<int, Pt>();
        // Spots taken by icons that stay visible, so a returning icon never lands on top of one.
        var taken = items.Where(i => !shelved.Contains(i.Name) && !DesktopView.IsParked(i.Pos)).Select(i => i.Pos).ToList();
        foreach (var it in items)
        {
            bool isParked = DesktopView.IsParked(it.Pos);
            if (shelved.Contains(it.Name))
            {
                if (!isParked)
                {
                    // Only a newly shelved icon gets its home saved. One that already has a home was hidden before
                    // and moved back by Explorer (F5, a drop), so where it is now is not where it belongs.
                    if (l.Home.TryAdd(it.Name, it.Pos)) homes = true;
                    view.Move(it.Index, DesktopView.ParkSlot(slot, view.Spacing));
                }
                slots[it.Index] = DesktopView.ParkSlot(slot, view.Spacing);
                slot++;
                parked++;
            }
            else if (isParked)
            {
                // Out of every shelf but still hidden (a shelf was deleted while Explorer was busy, or Shelves
                // stopped mid-way): bring it home.
                var to = l.Home.TryGetValue(it.Name, out var h) && !taken.Contains(h) && OnScreen(h) ? h : FreeSpot(taken);
                view.Move(it.Index, to);
                taken.Add(to);
                homes |= l.Home.Remove(it.Name);
            }
        }
        lastCount = view.Count;
        lastParked = parked;
        slotByIndex = slots;
        NeedsConfirm = false;
        if (homes) HomesChanged?.Invoke();
        Last = view.Items();
        // Shelves only need redrawing when the set of desktop items changed, not when icons merely moved.
        string names = string.Join("|", Last.Select(i => i.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        if (names != lastNames)
        {
            lastNames = names;
            Synced?.Invoke(Last);
        }
        return Last;
    }

    /// <summary>
    /// Cheap check, run on a timer and on list-view events: the item count or the number of parked icons changed
    /// (F5, a new or deleted file, Explorer moving icons). Runs a full <see cref="Sync"/> if so.
    /// </summary>
    public bool Check()
    {
        if (!view.Attached || Paused) return false;
        int count = view.Count;
        var positions = view.Positions();
        int parked = positions.Count(DesktopView.IsParked);
        if (count == lastCount && parked == lastParked) return false;
        // Explorer pulled hidden icons on screen (it does on every drop) but no file came or went, so the list-view
        // indices from the last sync still hold: queue the moves straight back without waiting on Explorer. A full
        // sync by name follows once things settle (NeedsConfirm), in case the indices were reordered (a sort).
        if (count == lastCount && parked < lastParked && slotByIndex.Count > 0)
        {
            foreach (var (i, spot) in slotByIndex)
                if (i < positions.Length && !DesktopView.IsParked(positions[i])) view.PostMove(i, spot);
            NeedsConfirm = true;
            return true;
        }
        // Files came or went: indices may have shifted, so find icons by name.
        Sync();
        return true;
    }

    Dictionary<string, Pt>? visibleBefore;

    /// <summary>Notes where every visible icon is, before a drag on the desktop (see <see cref="RestoreShoved"/>).</summary>
    public void RememberVisible()
    {
        if (!view.Attached) return;
        visibleBefore = view.Items().Where(i => !DesktopView.IsParked(i.Pos))
            .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Pos, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// After a drop: while handling it, Explorer pulls every hidden icon on screen and pushes visible icons aside to
    /// make room. Puts each visible icon that was not dragged (not selected) back where it was before the drag,
    /// unless a dropped icon now sits there. Returns how many it moved back.
    /// </summary>
    public int RestoreShoved(Point press = default, Point release = default)
    {
        var before = visibleBefore;
        visibleBefore = null;
        if (before == null || !view.Attached || Paused) return 0;
        var items = view.Items();
        var selected = view.SelectedIndices();
        var sp = view.Spacing;
        var dropped = items.Where(i => selected.Contains(i.Index)).Select(i => i.Pos).ToList();
        int moved = 0;
        // The dragged icons go where the mouse took them: their old spot moved by the drag, which is where Explorer
        // puts them too unless its shuffle pushed them elsewhere.
        int dx = release.X - press.X, dy = release.Y - press.Y;
        if (dx != 0 || dy != 0)
            foreach (var it in items.Where(i => selected.Contains(i.Index)))
            {
                if (!before.TryGetValue(it.Name, out var was)) continue;
                var target = new Pt(was.X + dx, was.Y + dy);
                if (target == it.Pos || DesktopView.IsParked(target)) continue;
                view.Move(it.Index, target);
                moved++;
            }
        foreach (var it in items)
        {
            if (selected.Contains(it.Index) || DesktopView.IsParked(it.Pos)) continue;
            if (!before.TryGetValue(it.Name, out var was) || was == it.Pos) continue;
            // The user dropped an icon onto this one's spot: leave it where Explorer moved it.
            if (dropped.Any(d => Math.Abs(d.X - was.X) < sp.Width / 2 && Math.Abs(d.Y - was.Y) < sp.Height / 2)) continue;
            view.Move(it.Index, was);
            moved++;
        }
        return moved;
    }

    /// <summary>True when every shelved icon found at the last sync is hidden right now (cheap: list-view messages only).</summary>
    public bool AllHidden => view.Attached && view.Positions().Count(DesktopView.IsParked) >= lastParked;

    /// <summary>Shows one icon again: at <paramref name="listViewPos"/> if given (a drop point), else where it was.</summary>
    public void Unhide(string name, Pt? listViewPos = null)
    {
        var l = layout();
        var items = view.Items();
        var it = items.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
        if (it == null) { if (l.Home.Remove(name)) HomesChanged?.Invoke(); return; }
        var taken = items.Where(i => !DesktopView.IsParked(i.Pos)).Select(i => i.Pos).ToList();
        var to = listViewPos ?? (l.Home.TryGetValue(name, out var h) && !taken.Contains(h) && OnScreen(h) ? h : FreeSpot(taken));
        view.Move(it.Index, to);
        l.Home.Remove(name);
        lastParked = Math.Max(0, lastParked - 1);
        HomesChanged?.Invoke();
    }

    /// <summary>Puts every parked icon back (Shelves is exiting, or the user asked to restore the desktop).</summary>
    public void RestoreAll()
    {
        if (!view.Attached) return;
        var l = layout();
        var items = view.Items();
        var taken = items.Where(i => !DesktopView.IsParked(i.Pos)).Select(i => i.Pos).ToList();
        foreach (var it in items.Where(i => DesktopView.IsParked(i.Pos)))
        {
            var to = l.Home.TryGetValue(it.Name, out var h) && !taken.Contains(h) && OnScreen(h) ? h : FreeSpot(taken);
            view.Move(it.Index, to);
            taken.Add(to);
        }
        lastParked = 0;
    }

    /// <summary>
    /// Snaps every visible icon to the desktop grid, as Windows' "Align icons to grid" does: icons already on a cell
    /// keep it, the others go to the nearest free cell on their monitor. Returns how many moved.
    /// </summary>
    public int SnapVisible()
    {
        if (!view.Attached || Paused) return 0;
        var sp = view.Spacing;
        var o = view.Origin;
        int w = Math.Max(sp.Width, 1), h = Math.Max(sp.Height, 1);
        var visible = view.Items().Where(i => !DesktopView.IsParked(i.Pos)).ToList();
        var taken = new HashSet<Pt>();
        var loose = new List<DesktopItem>();
        foreach (var it in visible)
        {
            var c = NearestCell(it.Pos, o, w, h, out _);
            if (c == it.Pos && taken.Add(c)) continue; // on a cell already
            loose.Add(it);
        }
        int moved = 0;
        foreach (var it in loose)
        {
            var c = NearestCell(it.Pos, o, w, h, out var area);
            var to = FreeCellNear(c, area, o, w, h, taken);
            taken.Add(to);
            if (to == it.Pos) continue;
            view.Move(it.Index, to);
            moved++;
        }
        return moved;
    }

    /// <summary>The grid cell nearest a list-view position, on the monitor it is on; Explorer's grid starts 11 px in and 2 px down.</summary>
    static Pt NearestCell(Pt p, Point origin, int w, int h, out Rectangle area)
    {
        var center = new Point(origin.X + p.X + w / 2, origin.Y + p.Y + h / 2);
        area = Screen.FromPoint(center).WorkingArea;
        int x0 = area.Left - origin.X + 11, y0 = area.Top - origin.Y + 2;
        int cols = Math.Max(1, (area.Width - 11) / w), rows = Math.Max(1, (area.Height - 2) / h);
        int col = Math.Clamp((int)Math.Round((p.X - x0) / (double)w), 0, cols - 1);
        int row = Math.Clamp((int)Math.Round((p.Y - y0) / (double)h), 0, rows - 1);
        return new Pt(x0 + col * w, y0 + row * h);
    }

    /// <summary>The free cell closest to <paramref name="c"/> on the same monitor (rings outward), or <paramref name="c"/> itself if all are taken.</summary>
    static Pt FreeCellNear(Pt c, Rectangle area, Point origin, int w, int h, HashSet<Pt> taken)
    {
        if (!taken.Contains(c)) return c;
        int x0 = area.Left - origin.X + 11, y0 = area.Top - origin.Y + 2;
        int cols = Math.Max(1, (area.Width - 11) / w), rows = Math.Max(1, (area.Height - 2) / h);
        int c0 = (c.X - x0) / w, r0 = (c.Y - y0) / h;
        for (int ring = 1; ring < Math.Max(cols, rows); ring++)
        {
            Pt? best = null;
            double bestD = double.MaxValue;
            for (int dc = -ring; dc <= ring; dc++)
                for (int dr = -ring; dr <= ring; dr++)
                {
                    if (Math.Max(Math.Abs(dc), Math.Abs(dr)) != ring) continue;
                    int col = c0 + dc, row = r0 + dr;
                    if (col < 0 || row < 0 || col >= cols || row >= rows) continue;
                    var p = new Pt(x0 + col * w, y0 + row * h);
                    if (taken.Contains(p)) continue;
                    double d = Math.Pow(dc * w, 2) + Math.Pow(dr * h, 2);
                    if (d < bestD) { bestD = d; best = p; }
                }
            if (best is { } found) return found;
        }
        return c;
    }

    /// <summary>
    /// True when a saved list-view position still lands on a monitor. Monitors come and go (and the list view's
    /// origin moves with the leftmost one), so an old position can point off screen; such icons get a free spot.
    /// </summary>
    bool OnScreen(Pt p)
    {
        var o = view.Origin;
        var sp = view.Spacing;
        var r = new Rectangle(o.X + p.X, o.Y + p.Y, sp.Width, sp.Height);
        return Screen.AllScreens.Any(s => s.WorkingArea.Contains(r.X + r.Width / 2, r.Y + r.Height / 2));
    }

    /// <summary>The first grid cell (column by column from the top-left, like Explorer) with no visible icon in it.</summary>
    Pt FreeSpot(List<Pt> taken)
    {
        var sp = view.Spacing;
        var screen = Screen.PrimaryScreen!.WorkingArea;
        var o = view.Origin;
        int x0 = screen.Left - o.X + 11, y0 = screen.Top - o.Y + 2;
        for (int x = x0; x + sp.Width <= screen.Right - o.X; x += sp.Width)
            for (int y = y0; y + sp.Height <= screen.Bottom - o.Y; y += sp.Height)
                if (!taken.Any(t => Math.Abs(t.X - x) < sp.Width / 2 && Math.Abs(t.Y - y) < sp.Height / 2))
                    return new Pt(x, y);
        return new Pt(x0, y0);
    }
}
