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
        int parked = 0;
        // Spots taken by icons that stay visible, so a returning icon never lands on top of one.
        var taken = items.Where(i => !shelved.Contains(i.Name) && !DesktopView.IsParked(i.Pos)).Select(i => i.Pos).ToList();
        foreach (var it in items)
        {
            bool isParked = DesktopView.IsParked(it.Pos);
            if (shelved.Contains(it.Name))
            {
                if (!isParked)
                {
                    l.Home[it.Name] = it.Pos;
                    homes = true;
                    view.Move(it.Index, DesktopView.Parked);
                }
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
        if (homes) HomesChanged?.Invoke();
        Last = view.Items();
        Synced?.Invoke(Last);
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
        int parked = view.Positions().Count(DesktopView.IsParked);
        if (count == lastCount && parked == lastParked) return false;
        Sync();
        return true;
    }

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
