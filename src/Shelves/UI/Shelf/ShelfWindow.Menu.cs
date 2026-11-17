namespace Shelves.UI.Shelf;

sealed partial class ShelfWindow
{
    Form? renameBox;

    void ShowShelfMenu(Point screen)
    {
        var m = new ContextMenuStrip { RightToLeft = L.Ar ? RightToLeft.Yes : RightToLeft.No };
        ToolStripMenuItem Item(string text, string? glyph, Action click, bool check = false)
        {
            var it = new ToolStripMenuItem(text) { Checked = check, Tag = glyph == null ? null : new ThemedMenuRenderer.MenuGlyph(glyph) };
            it.Click += (_, _) => click();
            return it;
        }
        ToolStripMenuItem Sub(string text, string glyph, params ToolStripItem[] children)
        {
            var it = new ToolStripMenuItem(text) { Tag = new ThemedMenuRenderer.MenuGlyph(glyph) };
            it.DropDownItems.AddRange(children);
            return it;
        }

        m.Items.Add(Item(L.T("Rename"), Art.IconRename, BeginTitleRename));
        if (!Model.Pinned)
            m.Items.Add(Item(L.T(Model.Rolled ? "Unroll" : "RollUp"), Art.IconRoll, () => SetRolled(!Model.Rolled)));
        m.Items.Add(Item(L.T("PinOpen"), Art.IconPin, TogglePin, Model.Pinned));
        m.Items.Add(Sub(L.T("Dock"), Art.IconDock,
            Item(L.T("DockLeft"), null, () => DockTo(Edge.Left), Model.Edge == Edge.Left),
            Item(L.T("DockTop"), null, () => DockTo(Edge.Top), Model.Edge == Edge.Top),
            Item(L.T("DockRight"), null, () => DockTo(Edge.Right), Model.Edge == Edge.Right),
            Item(L.T("DockBottom"), null, () => DockTo(Edge.Bottom), Model.Edge == Edge.Bottom),
            new ToolStripSeparator(),
            Item(L.T("Undock"), null, () => DockTo(Edge.None), Model.Edge == Edge.None)));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(Sub(L.T("Sort"), Art.IconSort,
            Item(L.T("SortNone"), null, () => SetSort(SortBy.None), Model.Sort == SortBy.None),
            Item(L.T("SortName"), null, () => SetSort(SortBy.Name), Model.Sort == SortBy.Name),
            Item(L.T("SortType"), null, () => SetSort(SortBy.Type), Model.Sort == SortBy.Type),
            Item(L.T("SortDate"), null, () => SetSort(SortBy.Date), Model.Sort == SortBy.Date)));
        m.Items.Add(Sub(L.T("IconSize"), Art.IconSize,
            Item(L.T("SizeSmall"), null, () => SetIconSize(IconSize.Small), Model.IconSize == IconSize.Small),
            Item(L.T("SizeMedium"), null, () => SetIconSize(IconSize.Medium), Model.IconSize == IconSize.Medium),
            Item(L.T("SizeLarge"), null, () => SetIconSize(IconSize.Large), Model.IconSize == IconSize.Large)));
        m.Items.Add(ColorMenu());
        m.Items.Add(OpacityMenu());
        m.Items.Add(new ToolStripSeparator());
        if (Model.Portal == null)
            m.Items.Add(Item(L.T("MakePortal"), Art.IconFolder, ChoosePortal));
        else
        {
            m.Items.Add(Item(L.T("OpenFolder"), Art.IconOpen, () => ShellOps.Open(Handle, Model.Portal!)));
            m.Items.Add(Item(L.T("StopPortal"), Art.IconFolder, () => { Model.Portal = null; watcher?.Dispose(); watcher = null; app.SaveSoon(); LoadItems(); }));
        }
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(Item(L.T("NewTab"), Art.IconAdd, AddTab));
        m.Items.Add(Item(L.T("NewShelf"), Art.IconAdd, () => app.NewShelfNear(this)));
        var del = Item(L.T("DeleteShelf"), Art.IconDelete, () => app.DeleteShelf(this));
        del.ForeColor = Color.FromArgb(232, 72, 85);
        del.Tag = new ThemedMenuRenderer.MenuGlyph(Art.IconDelete, Color.FromArgb(232, 72, 85));
        m.Items.Add(del);

        ThemedMenuRenderer.Style(m, app.Theme);
        menuOpen = true;
        m.Closed += (_, _) => { menuOpen = false; BeginInvoke(() => m.Dispose()); };
        m.Show(screen);
    }

    ToolStripMenuItem ColorMenu()
    {
        var menu = new ToolStripMenuItem(L.T("ShelfColor")) { Tag = new ThemedMenuRenderer.MenuGlyph("\uE790") };
        void Add(string text, string? hex, Color? swatch)
        {
            var it = new ToolStripMenuItem(text) { Checked = Model.Tint == hex };
            if (swatch is { } c) it.Tag = new ThemedMenuRenderer.MenuGlyph("\uEA3B", c); // a filled circle
            it.Click += (_, _) => SetTint(hex);
            menu.DropDownItems.Add(it);
        }
        Add(L.T("UseDefault"), null, null);
        menu.DropDownItems.Add(new ToolStripSeparator());
        Add(L.T("ThemeColor"), Colors.ToHex(app.Theme.Window.R, app.Theme.Window.G, app.Theme.Window.B), app.Theme.Window);
        foreach (var (key, hex) in Colors.Presets)
        {
            var c = Colors.Parse(hex)!.Value;
            Add(L.T("Color" + key), hex, Color.FromArgb(c.R, c.G, c.B));
        }
        menu.DropDownItems.Add(new ToolStripSeparator());
        var custom = new ToolStripMenuItem(L.T("CustomColor"))
        {
            Checked = Model.Tint != null && !Colors.Presets.Any(p => p.Hex == Model.Tint) && Model.Tint != Colors.ToHex(app.Theme.Window.R, app.Theme.Window.G, app.Theme.Window.B),
        };
        custom.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { FullOpen = true, AnyColor = true };
            if (Colors.Parse(Model.Tint) is { } c) dlg.Color = Color.FromArgb(c.R, c.G, c.B);
            menuOpen = true;
            try { if (dlg.ShowDialog(this) == DialogResult.OK) SetTint(Colors.ToHex(dlg.Color.R, dlg.Color.G, dlg.Color.B)); }
            finally { menuOpen = false; }
        };
        menu.DropDownItems.Add(custom);
        return menu;
    }

    ToolStripMenuItem OpacityMenu()
    {
        var menu = new ToolStripMenuItem(L.T("ShelfOpacity")) { Tag = new ThemedMenuRenderer.MenuGlyph("\uE706") };
        var def = new ToolStripMenuItem(L.T("UseDefault") + $"  ({app.Settings.Opacity}%)") { Checked = Model.Opacity == null };
        def.Click += (_, _) => SetOpacity(null);
        menu.DropDownItems.Add(def);
        menu.DropDownItems.Add(new ToolStripSeparator());
        foreach (int v in new[] { 0, 20, 40, 60, 80, 100 })
        {
            var it = new ToolStripMenuItem($"{v}%") { Checked = Model.Opacity == v };
            it.Click += (_, _) => SetOpacity(v);
            menu.DropDownItems.Add(it);
        }
        return menu;
    }

    void SetTint(string? hex) { Model.Tint = hex == null ? null : Colors.Normalize(hex); app.SaveSoon(); Apply(); }
    void SetOpacity(int? v) { Model.Opacity = v; app.SaveSoon(); Apply(); }

    void TogglePin()
    {
        Model.Pinned = !Model.Pinned;
        if (Model.Pinned && Model.Rolled) SetRolled(false);
        app.SaveSoon();
        Apply();
    }

    void SetSort(SortBy s) { Model.Sort = s; app.SaveSoon(); LoadItems(); }
    void SetIconSize(IconSize s) { Model.IconSize = s; app.SaveSoon(); LayoutItems(); Apply(); }

    void ChoosePortal()
    {
        using var dlg = new FolderBrowserDialog { ShowNewFolderButton = true, UseDescriptionForTitle = true, Description = L.T("MakePortal").TrimEnd('…') };
        menuOpen = true;
        try
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            // The shelf's own desktop icons go back to the desktop: a portal shows the folder instead.
            foreach (var n in Model.Items.ToList()) { app.Layout.Unshelve(n); app.Keeper.Unhide(n); }
            Model.Portal = dlg.SelectedPath;
            if (Model.Title.Length == 0) Model.Title = Path.GetFileName(dlg.SelectedPath.TrimEnd('\\')) is { Length: > 0 } t ? t : dlg.SelectedPath;
            app.SaveSoon();
            LoadItems();
        }
        finally { menuOpen = false; }
    }

    // ---- Renaming ----------------------------------------------------------------------------------------------

    void BeginTitleRename()
    {
        if (SideTab) Peek(true);
        var t = TitleRect(new Size(open.W, open.H));
        var shift = ContentShift(Target());
        t.Offset(shift);
        ShowRenameBox(Rectangle.Inflate(t, -(int)(10 * S), -(int)(4 * S)), Model.Title.Length > 0 ? Model.Title : L.T("DefaultTitle"), true, name =>
        {
            Model.Title = name.Trim();
            app.SaveSoon();
            Apply();
        });
    }

    void BeginRename(ShelfItem it)
    {
        var c = it.Cell;
        var shift = ContentShift(Target());
        c.Offset(shift.X, shift.Y - scroll);
        int top = c.Y + IconPx + (int)(8 * S);
        ShowRenameBox(new Rectangle(c.X - (int)(6 * S), top, c.Width + (int)(12 * S), (int)(24 * S)), it.Label, false, name =>
        {
            if (name.Length == 0 || name == it.Label) return;
            using var g = Group([it.Name]);
            if (g != null && ShellOps.Rename(Handle, g, name))
            {
                IconCache.Forget(it.Name);
                app.Keeper.Check();
            }
        });
    }

    void ShowRenameBox(Rectangle client, string text, bool center, Action<string> commit)
    {
        renameBox?.Close();
        var screen = RectangleToScreen(client);
        var f = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Bounds = screen,
            BackColor = app.Theme.Input,
            Owner = this,
            TopMost = false,
        };
        var tb = new TextBox
        {
            Text = text,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 9.5f),
            BackColor = app.Theme.Input,
            ForeColor = app.Theme.Text,
            TextAlign = center ? HorizontalAlignment.Center : HorizontalAlignment.Center,
            RightToLeft = L.Ar ? RightToLeft.Yes : RightToLeft.No,
            Location = new Point((int)(4 * S), (screen.Height - (int)(18 * S)) / 2),
            Width = screen.Width - (int)(8 * S),
        };
        f.Controls.Add(tb);
        bool done = false;
        void Finish(bool ok)
        {
            if (done) return;
            done = true;
            string v = tb.Text;
            renameBox = null;
            f.Close();
            if (ok) commit(v);
        }
        tb.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Finish(true); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Finish(false); }
        };
        tb.LostFocus += (_, _) => Finish(true);
        renameBox = f;
        f.Shown += (_, _) =>
        {
            Native.RoundCorners(f.Handle, app.Theme.Dark);
            f.Activate();
            tb.Focus();
            // Select the name without its extension, as Explorer does.
            int dot = center ? -1 : text.LastIndexOf('.');
            if (dot > 0) tb.Select(0, dot); else tb.SelectAll();
        };
        f.Show(this);
    }
}
