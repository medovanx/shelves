using System.Drawing.Drawing2D;

namespace Shelves.UI;

/// <summary>Settings in the Windows 11 Settings style: a sidebar of pages, each a rounded card.</summary>
sealed class SettingsForm : Form
{
    readonly ShelvesApp app;
    readonly Settings s;
    readonly Theme t = Theme.Current;

    // General
    readonly ThemedCombo cmbLang = Combo();
    readonly ToggleSwitch tglStartup = new(), tglDoubleClick = new(), tglDesktopMenu = new(), tglGrid = new(), tglDraw = new();
    // Appearance
    readonly ThemedNumber numOpacity = Num(0, 100), numRadius = Num(0, 16), numLines = Num(1, 3);
    readonly SwatchPicker swatches = new();
    readonly Segmented segAlign = new(L.T("AlignLeft"), L.T("AlignCenter"), L.T("AlignRight")) { Width = 260 };
    readonly Segmented segSize = new(L.T("SizeSmall"), L.T("SizeMedium"), L.T("SizeLarge")) { Width = 260 };
    // Roll up
    readonly ToggleSwitch tglPeek = new(), tglAnimate = new();
    readonly ThemedNumber numHover = Num(0, 3000), numLeave = Num(0, 5000);
    // Snapshots
    readonly FlowLayoutPanel snapList = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Padding.Empty };
    readonly ThemedButton btnSnapshot = new() { AutoSize = true, Height = 30 };
    // Updates
    readonly ToggleSwitch tglAutoCheck = new();
    readonly ThemedButton btnCheck = new() { AutoSize = true, Height = 30 };
    readonly ProgressLine updateBar = new() { Width = 200, Height = 30, Visible = false };
    readonly IconButton btnGitHub = new() { Width = 150, Height = 32, Glyph = Art.IconOpen }, btnNotes = new() { Width = 150, Height = 32, Glyph = Art.IconOpen };
    readonly Label lblUpdate = new() { AutoSize = false, Size = new Size(400, 30), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Tag = "dim", Cursor = Cursors.Default };
    Updater.Release? pending;
    bool busy;

    readonly ThemedButton btnSave = new() { Size = new Size(110, 34) }, btnCancel = new() { Size = new Size(110, 34) };
    readonly List<(string glyph, string title, Control page)> pages = [];
    readonly NavList nav;
    readonly Panel content = new() { Dock = DockStyle.Fill, Padding = new Padding(20, 20, 20, 0) };

    public SettingsForm(ShelvesApp app)
    {
        this.app = app;
        s = app.Settings;
        Text = $"{L.T("Settings")} - {L.T("AppName")}";
        using (var st = typeof(SettingsForm).Assembly.GetManifestResourceStream("app.ico"))
            if (st != null) Icon = new Icon(st);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(780, 560);
        Font = new Font("Segoe UI", 9.75f);
        BackColor = t.Window;
        ForeColor = t.Text;
        if (L.Ar) RightToLeft = RightToLeft.Yes;

        BuildGeneral();
        BuildAppearance();
        BuildRollUp();
        BuildSnapshots();
        BuildUpdates();
        BuildAbout();

        nav = new NavList(pages.Select(p => (p.glyph, p.title)).ToArray(), t) { Dock = L.Ar ? DockStyle.Right : DockStyle.Left, Width = 210 };
        nav.SelectedChanged += ShowPage;

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 62, Padding = new Padding(20, 12, 20, 16) };
        btnSave.Text = L.T("Save");
        btnCancel.Text = L.T("Cancel");
        btnSave.Click += (_, _) => Save();
        btnCancel.Click += (_, _) => Close();
        AcceptButton = btnSave;
        CancelButton = btnCancel;
        var buttons = new FlowLayoutPanel { Dock = L.Ar ? DockStyle.Left : DockStyle.Right, AutoSize = true, FlowDirection = L.Ar ? FlowDirection.LeftToRight : FlowDirection.RightToLeft, WrapContents = false };
        buttons.Controls.AddRange([btnSave, btnCancel]);
        footer.Controls.Add(buttons);

        foreach (var p in pages) { p.page.Visible = false; content.Controls.Add(p.page); }
        Controls.Add(content);
        Controls.Add(footer);
        Controls.Add(nav);

        Styler.Apply(this, t);
        Styler.Primary(btnSave, t);
        ShowPage(0);
        SyncEnabled();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.RoundCorners(Handle, t.Dark);
        int caption = ColorTranslator.ToWin32(t.Window);
        Native.DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int)); // DWMWA_CAPTION_COLOR
    }

    public int PageCount => pages.Count;
    public void ShowPageForShot(int i) => ShowPage(i);

    public void ShowUpdates() => ShowPage(pages.FindIndex(p => p.title == L.T("Updates")));

    void ShowPage(int i)
    {
        if (i < 0) return;
        for (int k = 0; k < pages.Count; k++) pages[k].page.Visible = k == i;
        nav.Selected = i;
    }

    // ---------- pages ----------

    void AddPage(string glyph, string title, params Control[] sections)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        var heading = new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI Semibold", 17f), Margin = new Padding(2, 0, 0, 12) };
        flow.Controls.Add(heading);
        foreach (var sec in sections) { sec.Width = 526; sec.Margin = new Padding(0, 0, 0, 12); flow.Controls.Add(sec); }
        pages.Add((glyph, title, flow));
    }

    void BuildGeneral()
    {
        cmbLang.Items.AddRange([L.T("LangAuto"), "English", "العربية"]);
        cmbLang.SelectedIndex = s.Language switch { "en" => 1, "ar" => 2, _ => 0 };
        tglStartup.Checked = s.StartWithWindows;
        tglDesktopMenu.Checked = DesktopMenu.Installed();
        tglDoubleClick.Checked = s.DoubleClickHides;
        tglGrid.Checked = s.AlignToGrid;
        tglDraw.Checked = s.DrawDesktop;
        AddPage(Art.IconSettings, L.T("General"), Card(L.T("General"), Art.IconSettings,
            Row(L.T("Language"), cmbLang),
            Row(L.T("StartWithWindows"), tglStartup),
            Row(L.T("DesktopMenu"), tglDesktopMenu),
            Row(L.T("AlignToGrid"), tglGrid),
            Row(L.T("DrawDesktop"), tglDraw),
            Row(L.T("DoubleClickHides"), tglDoubleClick)));
    }

    void BuildAppearance()
    {
        numOpacity.Value = s.Opacity;
        numOpacity.Increment = 5;
        swatches.Theme = t;
        swatches.Value = s.Tint;
        numRadius.Value = s.CornerRadius;
        numLines.Value = s.LabelLines;
        segAlign.Selected = (int)s.TitleAlign;
        segSize.Selected = (int)s.DefaultIconSize;
        AddPage("", L.T("Appearance"), Card(L.T("Appearance"), "",
            Row(L.T("ShelfColor"), swatches),
            Row(L.T("Opacity"), numOpacity),
            Row(L.T("CornerRadius"), numRadius),
            Row(L.T("TitleAlign"), segAlign),
            Row(L.T("LabelLines"), numLines),
            Row(L.T("DefaultIconSize"), segSize)));
    }

    void BuildRollUp()
    {
        tglPeek.Checked = s.HoverToPeek;
        tglAnimate.Checked = s.AnimateRoll;
        tglPeek.CheckedChanged += (_, _) => SyncEnabled();
        numHover.Value = s.HoverDelayMs;
        numHover.Increment = 50;
        numLeave.Value = s.LeaveDelayMs;
        numLeave.Increment = 50;
        AddPage(Art.IconRoll, L.T("RollUpPage"), Card(L.T("RollUpPage"), Art.IconRoll,
            Row(L.T("AnimateRoll"), tglAnimate),
            Row(L.T("HoverToPeek"), tglPeek),
            Row(L.T("HoverDelay"), numHover),
            Row(L.T("LeaveDelay"), numLeave)));
    }

    void BuildSnapshots()
    {
        btnSnapshot.Text = L.T("SaveSnapshot");
        btnSnapshot.Click += (_, _) => { app.SaveSnapshot(); FillSnapshots(); };
        var hint = new Label { Text = L.T("SnapshotsHint"), AutoSize = true, MaximumSize = new Size(490, 0), Tag = "dim", Margin = new Padding(0, 0, 0, 8) };
        FillSnapshots();
        AddPage(Art.IconSnapshot, L.T("SnapshotsPage"), Card(L.T("SnapshotsPage"), Art.IconSnapshot, hint, btnSnapshot, snapList));
    }

    void FillSnapshots()
    {
        snapList.SuspendLayout();
        foreach (Control c in snapList.Controls.Cast<Control>().ToList()) c.Dispose();
        var list = app.Snapshots.List();
        if (list.Count == 0)
            snapList.Controls.Add(new Label { Text = L.T("NoSnapshots"), AutoSize = true, Tag = "dim", Margin = new Padding(0, 8, 0, 0) });
        foreach (var (path, snap) in list.Take(20))
        {
            string label = snap.Auto ? $"{L.T("AutoSnapshot")}  {snap.Time.ToString("g", L.Culture)}" : snap.Name;
            var restore = new ThemedButton { Text = L.T("Restore"), AutoSize = true, Height = 27 };
            var delete = new ThemedButton { Text = L.T("Delete"), AutoSize = true, Height = 27 };
            restore.Click += (_, _) => { app.RestoreSnapshot(snap); FillSnapshots(); };
            delete.Click += (_, _) => { app.Snapshots.Delete(path); FillSnapshots(); };
            snapList.Controls.Add(Row(label, Flow(restore, delete)));
        }
        Styler.Apply(snapList, t);
        snapList.ResumeLayout();
    }

    void BuildUpdates()
    {
        tglAutoCheck.Checked = s.CheckUpdates;
        btnCheck.Text = L.T("CheckNow");
        var version = new Label { Text = L.F("Version", Updater.CurrentText), AutoSize = true, Font = new Font("Segoe UI Semibold", 11f), Margin = new Padding(0, 0, 0, 8) };
        AddPage(Art.IconUpdate, L.T("Updates"), Card(L.T("Updates"), Art.IconUpdate,
            version,
            Row(L.T("AutoCheck"), tglAutoCheck),
            Flow(btnCheck, updateBar, lblUpdate),
            Flow(btnGitHub, btnNotes)));
        btnGitHub.Text = L.T("GitHubRepo");
        btnNotes.Text = L.T("ReleaseNotes");
        btnGitHub.Click += (_, _) => Updater.OpenPage(Updater.RepoPage);
        btnNotes.Click += (_, _) => Updater.OpenPage(Updater.ReleasesPage);
        btnGitHub.Theme = btnNotes.Theme = updateBar.Theme = t;
        btnCheck.Click += async (_, _) => await CheckAsync();
        lblUpdate.Click += async (_, _) => { if (pending != null) await InstallAsync(pending); };
    }

    async Task CheckAsync()
    {
        if (busy) return;
        busy = true;
        SetBusy(true, L.T("CheckingUpdates"), null);
        try
        {
            pending = await Updater.CheckAsync();
            s.LastUpdateCheck = DateTime.UtcNow;
            SetBusy(false, pending == null ? L.F("UpToDate", Updater.CurrentText) : L.F("UpdateAvailable", pending.Version.ToString(3)), null);
            lblUpdate.Cursor = pending != null ? Cursors.Hand : Cursors.Default;
            lblUpdate.ForeColor = pending != null ? t.Accent : t.Dim;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            SetBusy(false, L.T("UpdateCheckFailed"), null);
        }
        finally { busy = false; }
    }

    async Task InstallAsync(Updater.Release rel)
    {
        if (busy) return;
        busy = true;
        app.BeforeUpdate();
        string ver = rel.Version.ToString(3);
        var progress = new Progress<Updater.Progress>(p =>
        {
            if (p.Verifying) SetBusy(true, L.T("UpdateVerifying"), null);
            else SetBusy(true, L.F("UpdateDownloading", ver, p.Total > 0 ? $"{L.Size(p.Done)} / {L.Size(p.Total)}" : L.Size(p.Done)),
                         p.Total > 0 ? p.Done / (double)p.Total : null);
        });
        if (await Updater.InstallAsync(rel, progress))
        {
            SetBusy(true, L.F("UpdateRestarting", ver), 1);
            app.ExitThread();
            return;
        }
        SetBusy(false, L.T("UpdateFailed"), null);
        Updater.OpenPage(rel.PageUrl);
        busy = false;
    }

    void SetBusy(bool on, string status, double? fraction)
    {
        if (IsDisposed) return;
        updateBar.Visible = on;
        btnCheck.Visible = !on;
        updateBar.Value = fraction;
        lblUpdate.Text = status;
        lblUpdate.ForeColor = t.Dim;
    }

    void BuildAbout()
    {
        var name = new Label { Text = L.T("AppName"), AutoSize = true, Font = new Font("Segoe UI Semibold", 13f), Margin = new Padding(0, 0, 0, 4) };
        var ver = new Label { Text = L.F("Version", Updater.CurrentText), AutoSize = true, Tag = "dim", Margin = new Padding(0, 0, 0, 8) };
        var text = new Label { Text = L.T("AboutText"), AutoSize = true, MaximumSize = new Size(490, 0), Margin = new Padding(0, 0, 0, 8) };
        var copy = new Label { Text = "MIT License, medovanx", AutoSize = true, Tag = "dim" };
        AddPage("", L.T("About"), Card(L.T("About"), "", name, ver, text, copy));
    }

    void SyncEnabled()
    {
        numHover.Enabled = numLeave.Enabled = tglPeek.Checked;
    }

    void Save()
    {
        string oldLang = s.Language;
        s.Language = cmbLang.SelectedIndex switch { 1 => "en", 2 => "ar", _ => "" };
        s.StartWithWindows = tglStartup.Checked;
        if (tglDesktopMenu.Checked != DesktopMenu.Installed())
        {
            // Machine-wide, so Windows asks for admin rights; if refused, it stays as it was.
            if (!DesktopMenu.Set(tglDesktopMenu.Checked))
                MessageBox.Show(this, L.T("DesktopMenuFailed"), L.T("AppName"), MessageBoxButtons.OK, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1,
                    L.Ar ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0);
        }
        s.DesktopMenu = DesktopMenu.Installed();
        s.DoubleClickHides = tglDoubleClick.Checked;
        bool gridChanged = s.AlignToGrid != tglGrid.Checked;
        bool drawChanged = s.DrawDesktop != tglDraw.Checked;
        s.Opacity = (int)numOpacity.Value;
        s.Tint = swatches.Value;
        s.CornerRadius = (int)numRadius.Value;
        s.LabelLines = (int)numLines.Value;
        s.TitleAlign = (TitleAlign)segAlign.Selected;
        s.DefaultIconSize = (IconSize)segSize.Selected;
        s.HoverToPeek = tglPeek.Checked;
        s.AnimateRoll = tglAnimate.Checked;
        s.HoverDelayMs = (int)numHover.Value;
        s.LeaveDelayMs = (int)numLeave.Value;
        s.CheckUpdates = tglAutoCheck.Checked;
        Settings.Migrate(s);
        if (gridChanged) app.SetAlignToGrid(tglGrid.Checked);
        if (drawChanged) app.SetDrawDesktop(tglDraw.Checked);
        app.ApplySettings();
        Close();
    }

    // ---------- layout helpers ----------

    static ThemedCombo Combo(int width = 220) => new() { Width = width };
    static ThemedNumber Num(int min, int max) => new() { Minimum = min, Maximum = max, DecimalPlaces = 0, Width = 120, Increment = 1 };

    Section Card(string title, string glyph, params Control[] rows)
    {
        var sec = new Section(title) { Glyph = glyph, Theme = t, RightToLeft = RightToLeft };
        sec.MinimumSize = new Size(526, 0);
        sec.MaximumSize = new Size(526, 0);
        var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Location = new Point(16, 44), MinimumSize = new Size(494, 0), MaximumSize = new Size(494, 0), RightToLeft = RightToLeft };
        foreach (var r in rows) stack.Controls.Add(r);
        sec.Controls.Add(stack);
        return sec;
    }

    /// <summary>Label on the near side, control on the far side.</summary>
    static TableLayoutPanel Row(string label, Control c)
    {
        var tl = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Width = 494, Height = Math.Max(36, (c.AutoSize ? c.PreferredSize.Height : c.Height) + 8), Margin = new Padding(0) };
        tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tl.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, MaximumSize = new Size(300, 0) }, 0, 0);
        c.Anchor = AnchorStyles.Right;
        c.Margin = new Padding(0, c.Margin.Top, 0, c.Margin.Bottom);
        tl.Controls.Add(c, 1, 0);
        return tl;
    }

    static FlowLayoutPanel Flow(params Control[] cs)
    {
        var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 4) };
        int tallest = cs.Max(c => c.Height);
        for (int i = 0; i < cs.Length; i++)
        {
            int top = (tallest - cs[i].Height) / 2;
            cs[i].Margin = new Padding(0, top, i < cs.Length - 1 ? 8 : 0, 0);
            p.Controls.Add(cs[i]);
        }
        return p;
    }

    /// <summary>Sidebar: brand header and page list with an accent selection pill.</summary>
    sealed class NavList : Control
    {
        readonly (string glyph, string title)[] items;
        readonly Theme t;
        int selected, hover = -1;
        public event Action<int>? SelectedChanged;

        public NavList((string, string)[] items, Theme t)
        {
            this.items = items;
            this.t = t;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        public int Selected { get => selected; set { selected = value; Invalidate(); } }

        float S => DeviceDpi / 96f;
        Rectangle Item(int i) => new((int)(10 * S), (int)((112 + i * 44) * S), Width - (int)(20 * S), (int)(40 * S));

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = Enumerable.Range(0, items.Length).FirstOrDefault(i => Item(i).Contains(e.Location), -1);
            if (h != hover) { hover = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            for (int i = 0; i < items.Length; i++)
                if (Item(i).Contains(e.Location)) { selected = i; Invalidate(); SelectedChanged?.Invoke(i); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool rtl = RightToLeft == RightToLeft.Yes;

            // Brand header: the icon's gradient with the shelves mark.
            var head = new Rectangle(0, 0, Width, (int)(92 * S));
            using (var grad = new LinearGradientBrush(head, Art.BrandLight, Art.BrandDark, LinearGradientMode.ForwardDiagonal))
                g.FillRectangle(grad, head);
            using (var below = new SolidBrush(t.Dark ? Color.FromArgb(26, 28, 28) : Color.FromArgb(236, 239, 238)))
                g.FillRectangle(below, 0, head.Bottom, Width, Height - head.Bottom);

            int gs = (int)(32 * S), gx = rtl ? Width - (int)(18 * S) - gs : (int)(18 * S);
            Art.Mark(g, new RectangleF(gx, (head.Height - gs) / 2f, gs, gs), Color.White);
            var textRect = rtl ? new Rectangle((int)(10 * S), 0, gx - (int)(20 * S), head.Height)
                               : new Rectangle(gx + gs + (int)(12 * S), 0, Width - gx - gs - (int)(22 * S), head.Height);
            var tf = TextFormatFlags.VerticalCenter | (rtl ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left);
            using var fName = new Font("Segoe UI Semibold", 12f);
            using var fVer = new Font("Segoe UI", 8.5f);
            TextRenderer.DrawText(g, L.T("AppName"), fName, new Rectangle(textRect.X, textRect.Y - (int)(9 * S), textRect.Width, textRect.Height), Color.White, tf);
            TextRenderer.DrawText(g, "v" + Updater.CurrentText, fVer, new Rectangle(textRect.X, textRect.Y + (int)(12 * S), textRect.Width, textRect.Height), Color.FromArgb(220, 226, 255), tf);

            using var fIcon = new Font(Art.IconFontName, 11f);
            using var fItem = new Font("Segoe UI", 10f);
            using var fItemB = new Font("Segoe UI Semibold", 10f);
            for (int i = 0; i < items.Length; i++)
            {
                var r = Item(i);
                if (i == selected || i == hover)
                {
                    using var p = Art.RoundRect(r, 6 * S);
                    using var b = new SolidBrush(i == selected ? Color.FromArgb(t.Dark ? 44 : 30, t.Accent) : Color.FromArgb(t.Dark ? 22 : 14, t.Text));
                    g.FillPath(b, p);
                }
                if (i == selected)
                {
                    var bar = new Rectangle(rtl ? r.Right - (int)(3 * S) : r.Left, r.Top + (int)(11 * S), (int)(3 * S), r.Height - (int)(22 * S));
                    using var bb = new SolidBrush(t.Accent);
                    g.FillRectangle(bb, bar);
                }
                int ix = rtl ? r.Right - (int)(40 * S) : r.Left + (int)(12 * S);
                var ir = new Rectangle(ix, r.Top, (int)(28 * S), r.Height);
                Art.GlyphCentered(g, items[i].glyph, 15 * S, ir, i == selected ? t.Accent : t.Dim);
                var lr = rtl ? new Rectangle(r.Left + (int)(8 * S), r.Top, r.Width - (int)(56 * S), r.Height)
                             : new Rectangle(r.Left + (int)(48 * S), r.Top, r.Width - (int)(56 * S), r.Height);
                TextRenderer.DrawText(g, items[i].title, i == selected ? fItemB : fItem, lr, t.Text, tf | TextFormatFlags.EndEllipsis);
            }
        }
    }
}
