namespace Shelves.UI.Shelf;

sealed partial class ShelfWindow
{
    int hoverTab = -1;        // tab under the mouse, or -2 for the "+" button
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
        for (int i = 0; i < tabs.Count; i++)
        {
            var r = tabs[i];
            r.Offset(shift);
            bool active = i == Model.Active, over = i == hoverTab || i == dragTab;
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

    void MoveTab(int i, int by)
    {
        int j = i + by;
        if (j < 0 || j >= Model.Tabs.Count) return;
        (Model.Tabs[i], Model.Tabs[j]) = (Model.Tabs[j], Model.Tabs[i]);
        if (Model.Active == i) Model.Active = j; else if (Model.Active == j) Model.Active = i;
        app.SaveSoon();
        Apply();
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
