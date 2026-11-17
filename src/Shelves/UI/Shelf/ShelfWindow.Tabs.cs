namespace Shelves.UI.Shelf;

sealed partial class ShelfWindow
{
    int hoverTab = -1;        // tab under the mouse, or -2 for the "+" button
    /// <summary>Where each tab is drawn now (open coordinates); tabs slide from here to their slot.</summary>
    readonly Dictionary<ShelfTab, float> tabX = [];
    readonly System.Windows.Forms.Timer tabTimer = new() { Interval = 10 };
    float dragGrab, dragX;     // where the dragged tab was grabbed, and where its left edge is now
    int dragTab = -1;         // tab a drag is resting on
    DateTime dragTabSince;

    /// <summary>The strip shows once a shelf has two tabs or more.</summary>
    bool TabsShown => Model.Tabs.Count > 1;
    int TabH => TabsShown ? (int)(28 * S) : 0;

    /// <summary>The strip, in the open shelf's coordinates: under the title, or above it on a bottom shelf.</summary>
    Rectangle TabStrip => Model.Edge == Edge.Bottom
        ? new Rectangle(Pad, open.H - TitleH - TabH, open.W - Pad * 2, TabH)
        : new Rectangle(Pad, TitleH + (int)(2 * S), open.W - Pad * 2, TabH);

    string TabTitle(int i) => Model.Tabs[i].Title is { Length: > 0 } t ? t : L.F("TabN", i + 1);

    Font TabFont() => new("Segoe UI", 12f * S, GraphicsUnit.Pixel);

    /// <summary>Each tab's rectangle, then the "+" button's, in the open shelf's coordinates.</summary>
    (List<Rectangle> tabs, Rectangle add) TabRects()
    {
        var strip = TabStrip;
        int add = (int)(24 * S), gap = (int)(4 * S), pad = (int)(12 * S);
        using var f = TabFont();
        var widths = Model.Tabs.Select((_, i) => TextRenderer.MeasureText(TabTitle(i), f).Width + pad * 2).ToList();
        int room = strip.Width - add - gap * Model.Tabs.Count;
        if (widths.Sum() > room)
        {
            int each = Math.Max((int)(40 * S), room / Math.Max(1, widths.Count));
            widths = widths.Select(w => Math.Min(w, each)).ToList();
        }
        var rects = new List<Rectangle>();
        int x = 0;
        int h = strip.Height - (int)(4 * S), y = strip.Y + (int)(2 * S);
        foreach (var w in widths)
        {
            rects.Add(L.Ar ? new Rectangle(strip.Right - x - w, y, w, h) : new Rectangle(strip.X + x, y, w, h));
            x += w + gap;
        }
        var plus = L.Ar ? new Rectangle(strip.Right - x - add, y, add, h) : new Rectangle(strip.X + x, y, add, h);
        return (rects, plus);
    }

    /// <summary>The tab at a window point (-2 for the "+" button), or -1.</summary>
    int TabAt(Point window)
    {
        if (!TabsShown || !(IsOpen || peeking)) return -1;
        var p = ToOpen(window);
        var (tabs, add) = TabRects();
        for (int i = 0; i < tabs.Count; i++) if (tabs[i].Contains(p)) return i;
        return add.Contains(p) ? -2 : -1;
    }

    void PaintTabs(Graphics g, Box box)
    {
        if (!TabsShown) return;
        var shift = ContentShift(box);
        var (tabs, add) = TabRects();
        using var f = TabFont();
        float s = S;
        // The dragged tab is drawn last, on top of the ones sliding under it.
        var order = Enumerable.Range(0, tabs.Count).OrderBy(i => i == draggedTab && drag == DragKind.TabMove ? 1 : 0).ToList();
        foreach (int i in order)
        {
            var r = tabs[i];
            var tab = Model.Tabs[i];
            float x = i == draggedTab && drag == DragKind.TabMove ? dragX : tabX.TryGetValue(tab, out var ax) ? ax : r.X;
            r.X = (int)Math.Round(x);
            r.Offset(shift);
            bool active = i == Model.Active, over = i == hoverTab || i == dragTab || i == draggedTab;
            if (active || over)
                Art.FillRound(g, r, 6 * s, active ? Color.FromArgb(Th.Dark ? 70 : 55, Th.Accent) : Color.FromArgb(Th.Dark ? 26 : 20, TextColor));
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            // GDI+ text: TextRenderer cannot draw on a transparent bitmap.
            using var sf = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
            if (L.Ar) sf.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
            using var b = new SolidBrush(active ? TextColor : DimColor);
            g.DrawString(TabTitle(i), f, b, Rectangle.Inflate(r, -(int)(6 * s), 0), sf);
            _ = flags;
        }
        var a = add;
        a.Offset(shift);
        if (hoverTab == -2) Art.FillRound(g, a, 6 * s, Color.FromArgb(Th.Dark ? 26 : 20, TextColor));
        Art.GlyphCentered(g, Art.IconAdd, 10 * s, a, DimColor);
    }

    /// <summary>Shows tab <paramref name="i"/>.</summary>
    void SelectTab(int i)
    {
        if (i < 0 || i >= Model.Tabs.Count || i == Model.Active) return;
        Model.Active = i;
        selected.Clear();
        scroll = 0;
        app.SaveSoon();
        LoadItems();
    }

    /// <summary>Adds a tab and opens its name for typing. The first new tab also names the existing one.</summary>
    public void AddTab()
    {
        Model.Tabs.Add(new ShelfTab());
        Model.Active = Model.Tabs.Count - 1;
        if (!Model.Rolled) { /* stays open */ }
        else Peek(true);
        selected.Clear();
        scroll = 0;
        app.SaveSoon();
        LoadItems();
        BeginTabRename(Model.Active);
    }

    void BeginTabRename(int i)
    {
        var (tabs, _) = TabRects();
        if (i < 0 || i >= tabs.Count) return;
        var r = tabs[i];
        r.Offset(ContentShift(Target()));
        r = Rectangle.Inflate(r, (int)(8 * S), 0);
        ShowRenameBox(r, TabTitle(i), true, name =>
        {
            Model.Tabs[i].Title = name.Trim();
            app.SaveSoon();
            Apply();
        });
    }

    /// <summary>Removes a tab. Its desktop icons go back to the desktop; no files are touched.</summary>
    void DeleteTab(int i)
    {
        if (Model.Tabs.Count <= 1 || i < 0 || i >= Model.Tabs.Count) return;
        var tab = Model.Tabs[i];
        if (tab.Portal == null && tab.Items.Count > 0)
        {
            if (MessageBox.Show(L.F("DeleteTabAsk", TabTitle(i)), L.T("AppName"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2, L.Ar ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0) != DialogResult.OK)
                return;
            foreach (var n in tab.Items.ToList()) { tab.Items.Remove(n); app.Keeper.Unhide(n); }
        }
        Model.Tabs.RemoveAt(i);
        if (Model.Active >= i && Model.Active > 0) Model.Active--;
        selected.Clear();
        app.SaveSoon();
        LoadItems();
    }

    void ShowTabMenu(int i, Point screen)
    {
        var m = new ContextMenuStrip { RightToLeft = L.Ar ? RightToLeft.Yes : RightToLeft.No };
        ToolStripMenuItem Item(string text, string glyph, Action click)
        {
            var it = new ToolStripMenuItem(text) { Tag = new ThemedMenuRenderer.MenuGlyph(glyph) };
            it.Click += (_, _) => click();
            return it;
        }
        m.Items.Add(Item(L.T("RenameTab"), Art.IconRename, () => BeginTabRename(i)));
        m.Items.Add(Item(L.T("NewTab"), Art.IconAdd, AddTab));
        if (i > 0) m.Items.Add(Item(L.T("MoveTabLeft"), L.Ar ? "" : "", () => MoveTab(i, -1)));
        if (i < Model.Tabs.Count - 1) m.Items.Add(Item(L.T("MoveTabRight"), L.Ar ? "" : "", () => MoveTab(i, 1)));
        m.Items.Add(new ToolStripSeparator());
        var del = Item(L.T("DeleteTab"), Art.IconDelete, () => DeleteTab(i));
        del.ForeColor = Color.FromArgb(232, 72, 85);
        del.Tag = new ThemedMenuRenderer.MenuGlyph(Art.IconDelete, Color.FromArgb(232, 72, 85));
        m.Items.Add(del);
        ThemedMenuRenderer.Style(m, app.Theme);
        menuOpen = true;
        m.Closed += (_, _) => { menuOpen = false; BeginInvoke(() => m.Dispose()); };
        m.Show(screen);
    }

    void MoveTab(int i, int by) { MoveTabTo(i, i + by); app.SaveSoon(); StartTabSlide(); }

    /// <summary>Grabs tab <paramref name="i"/> at a window point, for dragging along the strip.</summary>
    void GrabTab(int i, Point window)
    {
        var (tabs, _) = TabRects();
        if (i < 0 || i >= tabs.Count) return;
        var p = ToOpen(window);
        dragGrab = p.X - tabs[i].X;
        dragX = tabs[i].X;
        foreach (var (t, k) in Model.Tabs.Select((t, k) => (t, k))) tabX[t] = tabs[k].X;
    }

    /// <summary>
    /// The dragged tab follows the mouse along the strip; once its middle passes another tab's middle it takes
    /// that place, and the others slide over to make room, as tabs do in a browser.
    /// </summary>
    void DragTabTo(Point window)
    {
        if (draggedTab < 0 || draggedTab >= Model.Tabs.Count) return;
        var (tabs, add) = TabRects();
        var strip = TabStrip;
        int w = tabs[draggedTab].Width;
        var p = ToOpen(window);
        // Within the tabs' own row: never over the "+" button.
        int lo = tabs.Min(r => r.Left), hi = tabs.Max(r => r.Right) - w;
        dragX = Math.Clamp(p.X - dragGrab, lo, Math.Max(lo, hi));
        hoverTab = -1;
        // A neighbour gives way once the dragged tab's leading edge passes its middle: its right edge for tabs after
        // it, its left edge for tabs before it (mirrored right to left). So the last place is reachable even when
        // the tabs are the same width.
        float left = dragX, right = dragX + w;
        int index = 0;
        for (int i = 0; i < tabs.Count; i++)
        {
            if (i == draggedTab) continue;
            float c = tabs[i].X + tabs[i].Width / 2f;
            bool after = i > draggedTab;
            bool passed = L.Ar ? (after ? left < c : right > c) : (after ? right > c : left < c);
            // Tabs before it stay before unless passed back over; tabs after it come before once passed.
            if (after ? passed : !passed) index++;
        }
        if (index != draggedTab)
        {
            MoveTabTo(draggedTab, index);
            draggedTab = index;
        }
        StartTabSlide();
        Apply();
    }

    /// <summary>The dragged tab was let go: it slides into its slot from where it was dropped.</summary>
    void DropTab()
    {
        if (draggedTab >= 0 && draggedTab < Model.Tabs.Count) tabX[Model.Tabs[draggedTab]] = dragX;
        draggedTab = -1;
        hoverTab = -1;
        app.SaveSoon();
        StartTabSlide();
    }

    void StartTabSlide()
    {
        if (tabTimer.Enabled) return;
        tabTimer.Tick -= TabSlideTick;
        tabTimer.Tick += TabSlideTick;
        tabTimer.Start();
    }

    /// <summary>Eases every tab (but the dragged one) toward its slot; stops once all are there.</summary>
    void TabSlideTick(object? sender, EventArgs e)
    {
        var (tabs, _) = TabRects();
        bool moving = false;
        for (int i = 0; i < tabs.Count; i++)
        {
            var t = Model.Tabs[i];
            if (i == draggedTab && drag == DragKind.TabMove) continue;
            float x = tabX.TryGetValue(t, out var v) ? v : tabs[i].X;
            float target = tabs[i].X;
            float next = Math.Abs(target - x) < 0.6f ? target : x + (target - x) * 0.3f;
            tabX[t] = next;
            moving |= next != target;
        }
        foreach (var gone in tabX.Keys.Where(k => !Model.Tabs.Contains(k)).ToList()) tabX.Remove(gone);
        Apply();
        if (!moving && !(drag == DragKind.TabMove)) tabTimer.Stop();
    }

    /// <summary>Moves tab <paramref name="from"/> to position <paramref name="to"/>, keeping the same tab shown.</summary>
    void MoveTabTo(int from, int to)
    {
        if (from < 0 || from >= Model.Tabs.Count || to < 0 || to >= Model.Tabs.Count || from == to) return;
        var shown = Model.Current;
        var tab = Model.Tabs[from];
        Model.Tabs.RemoveAt(from);
        Model.Tabs.Insert(to, tab);
        Model.Active = Model.Tabs.IndexOf(shown);
        Apply();
    }

    /// <summary>
    /// While dragging a tab: the tab whose half the cursor is past, anywhere along the strip's row (the cursor
    /// may leave the strip's height, or go past its ends).
    /// </summary>
    int TabAtStrip(Point window)
    {
        if (!TabsShown) return -1;
        var p = ToOpen(window);
        var (tabs, _) = TabRects();
        if (tabs.Count == 0) return -1;
        for (int i = 0; i < tabs.Count; i++)
            if (L.Ar ? p.X >= tabs[i].Left : p.X < tabs[i].Right) return i;
        return tabs.Count - 1;
    }

    /// <summary>During a drag, resting on a tab for a moment shows it, so icons can be dropped into it.</summary>
    void DragOverTabs(Point window)
    {
        int t = TabAt(window);
        if (t < 0) { if (dragTab != -1) { dragTab = -1; Apply(); } return; }
        if (t != dragTab) { dragTab = t; dragTabSince = DateTime.UtcNow; Apply(); return; }
        if (t != Model.Active && (DateTime.UtcNow - dragTabSince).TotalMilliseconds > 400) SelectTab(t);
    }
}
