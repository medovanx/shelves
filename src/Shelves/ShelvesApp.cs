using Microsoft.Win32;

namespace Shelves;

/// <summary>
/// The running app: the tray icon, every shelf window, Explorer's desktop view, and the timers and hooks that
/// keep shelved icons hidden and shelves above the desktop.
/// </summary>
sealed class ShelvesApp : ApplicationContext
{
    public const string ShowMessage = "Shelves.Show", ExitMessage = "Shelves.Exit", ImportMessage = "Shelves.ImportFences";
    /// <summary>Where layout, settings and snapshots live. SHELVES_DATA points a test run somewhere else.</summary>
    public static readonly string Dir = Environment.GetEnvironmentVariable("SHELVES_DATA") is { Length: > 0 } d
        ? d : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shelves");
    static string LayoutPath => Path.Combine(Dir, "layout.json");
    static string SettingsPath => Path.Combine(Dir, "settings.json");

    public Settings Settings { get; }
    public Layout Layout { get; private set; }
    public Theme Theme { get; set; } = Theme.Current;
    public Wallpaper Wallpaper { get; } = new();
    public DesktopView View { get; } = new();
    public IconKeeper Keeper { get; }
    public SnapshotStore Snapshots { get; } = new(Path.Combine(Dir, "snapshots"));
    public List<ShelfWindow> Windows { get; } = [];
    public string DesktopDir { get; } = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    readonly string publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

    readonly MessageWindow messages;
    readonly NotifyIcon tray;
    readonly DesktopInput input;
    readonly System.Windows.Forms.Timer checkTimer = new() { Interval = 1000 };
    readonly System.Windows.Forms.Timer saveTimer = new() { Interval = 800 };
    readonly System.Windows.Forms.Timer slowTimer = new() { Interval = 60_000 };
    readonly List<FileSystemWatcher> watchers = [];
    readonly Native.WinEventProc winEvent;
    IntPtr foregroundHook;
    List<Rectangle> iconRects = [];
    bool hiddenAll;
    bool autoArrangeWasOn;
    bool exiting;

    /// <summary>For --shots: draws shelves off screen, without Explorer, hooks, tray or saving.</summary>
    public bool ShotMode { get; }

    public ShelvesApp(bool updated, bool shot = false)
    {
        ShotMode = shot;
        Directory.CreateDirectory(Dir);
        Settings = shot ? new Settings() : Settings.Load(SettingsPath);
        if (!shot) { L.Apply(Settings.Language); SaveSettings(); } // writes back any upgrade from an older version
        Layout = shot ? new Layout() : LayoutStore.Load(LayoutPath);
        Keeper = new IconKeeper(View, () => Layout);
        Keeper.HomesChanged += SaveSoon;
        Keeper.Synced += _ => { RefreshIconRects(); Refresh(); };

        messages = new MessageWindow(this);
        tray = new NotifyIcon { Icon = AppIcon(), Text = L.T("AppName"), Visible = !shot };
        tray.MouseUp += (_, e) => { if (e.Button is MouseButtons.Right or MouseButtons.Left) ShowTrayMenu(); };

        input = new DesktopInput(messages.Invoker, EmptyDesktopAt, hook: !shot) { Theme = () => Theme };
        input.BoxDrawn += OnBoxDrawn;
        input.DesktopDoubleClick += () => { if (Settings.DoubleClickHides) ToggleHideAll(); };

        // Show desktop (Win+D) and clicks on the desktop raise it over everything: lift the shelves with it.
        winEvent = (_, _, hwnd, _, _, _, _) =>
        {
            var c = Native.ClassOf(hwnd);
            if (c is "Progman" or "WorkerW" or "SysListView32" or "SHELLDLL_DefView") PlaceSoon();
        };
        if (!shot) foregroundHook = Native.SetWinEventHook(3 /* EVENT_SYSTEM_FOREGROUND */, 3, IntPtr.Zero, winEvent, 0, 0, 0 /* OUTOFCONTEXT */);

        checkTimer.Tick += (_, _) => Tick();
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); SaveNow(); };
        slowTimer.Tick += (_, _) => SlowTick();
        Wallpaper.Changed += () => { foreach (var w in Windows) w.Repaint(); };
        // Signing out or shutting down: put every icon back first, so the desktop is never left with hidden icons.
        SystemEvents.SessionEnding += (_, _) => { SaveNow(); RestoreExplorer(); };
        SystemEvents.DisplaySettingsChanged += (_, _) => messages.Invoker.BeginInvoke(DisplayChanged);
        SystemEvents.UserPreferenceChanged += (_, e) => messages.Invoker.BeginInvoke(() => PreferenceChanged(e.Category));

        if (shot) return;
        WatchDesktop(DesktopDir);
        WatchDesktop(publicDesktop);
        Updater.CleanupOld();
        if (updated) tray.ShowBalloonTip(4000, L.T("AppName"), L.F("Updated", Updater.CurrentText), ToolTipIcon.Info);
        if (FencesRunning()) tray.ShowBalloonTip(8000, L.T("AppName"), L.T("FencesRunning"), ToolTipIcon.Warning);
        Autostart.Apply(Settings.StartWithWindows);

        Attach();
        foreach (var m in Layout.Shelves) AddWindow(m);
        checkTimer.Start();
        slowTimer.Start();
        SlowTick();
    }

    static Icon AppIcon()
    {
        using var s = typeof(ShelvesApp).Assembly.GetManifestResourceStream("app.ico");
        return s != null ? new Icon(s, SystemInformation.SmallIconSize) : SystemIcons.Application;
    }

    // ---- Explorer ----------------------------------------------------------------------------------------------

    /// <summary>Connects to Explorer's desktop and hides shelved icons. Retried by the timer until it works.</summary>
    bool Attach()
    {
        if (!View.Attach()) return false;
        uint flags = View.FolderFlags;
        if ((flags & DesktopView.FWF_AUTOARRANGE) != 0)
        {
            // Auto arrange would put parked icons straight back; it returns when Shelves exits.
            autoArrangeWasOn = true;
            View.SetFolderFlags(DesktopView.FWF_AUTOARRANGE, 0);
            tray.ShowBalloonTip(6000, L.T("AppName"), L.T("AutoArrangeOff"), ToolTipIcon.Info);
        }
        Keeper.Sync();
        var present = new HashSet<string>(Keeper.Last.Select(i => i.Name), StringComparer.OrdinalIgnoreCase);
        if (Health.LooksScrambled(Layout, present))
        {
            int missing = Layout.AllShelved().Count(n => !present.Contains(n));
            tray.ShowBalloonTip(8000, L.T("AppName"), L.F("MissingItems", missing), ToolTipIcon.Warning);
        }
        if (hiddenAll) Native.ShowWindow(View.ListView, 0);
        PlaceAll();
        return true;
    }

    /// <summary>Explorer restarted (the taskbar came back): find the new desktop and hide icons again.</summary>
    public void ExplorerRestarted()
    {
        View.Detach();
        var retry = new System.Windows.Forms.Timer { Interval = 1500 };
        int tries = 0;
        retry.Tick += (_, _) =>
        {
            if (Attach() || ++tries > 20) { retry.Stop(); retry.Dispose(); }
        };
        retry.Start();
    }

    void Tick()
    {
        if (!View.Attached) { if (!exiting) Attach(); return; }
        Keeper.Check();
        // While the desktop is in front (Show desktop), keep the shelves above it.
        if (DesktopInFront()) PlaceAll();
    }

    static bool DesktopInFront() => Native.ClassOf(Native.GetForegroundWindow()) is "Progman" or "WorkerW" or "SysListView32" or "SHELLDLL_DefView";

    /// <summary>
    /// Show desktop raises the desktop in steps, some after the foreground change is reported: place the shelves
    /// now and again a little later, so the last word is theirs.
    /// </summary>
    void PlaceSoon()
    {
        PlaceAll();
        foreach (int ms in new[] { 150, 400, 900 })
        {
            var t = new System.Windows.Forms.Timer { Interval = ms };
            t.Tick += (_, _) => { t.Stop(); t.Dispose(); PlaceAll(); };
            t.Start();
        }
    }

    void SlowTick()
    {
        Wallpaper.Poll();
        if (Snapshots.NeedsDaily(DateTime.Now) && Layout.Shelves.Count > 0)
            Snapshots.Save(Layout, L.T("AutoSnapshot"), auto: true, DateTime.Now);
        if (Settings.CheckUpdates && (DateTime.UtcNow - Settings.LastUpdateCheck).TotalHours >= 24)
            _ = CheckUpdatesQuietly();
    }

    void RefreshIconRects()
    {
        var origin = View.Origin;
        iconRects = Keeper.Last
            .Where(i => !DesktopView.IsParked(i.Pos))
            .Select(i => View.Bounds(i.Index) is { } r ? r with { X = r.X + origin.X, Y = r.Y + origin.Y } : Rectangle.Empty)
            .Where(r => !r.IsEmpty).ToList();
    }

    /// <summary>True when the point is on Explorer's desktop and on no icon (and no icon is being renamed).</summary>
    bool EmptyDesktopAt(Point p) => IsDesktopAt(p) && !iconRects.Any(r => r.Contains(p)) && !View.Renaming;

    public bool IsDesktopAt(Point p)
    {
        var h = Native.WindowFromPoint(new Native.POINT { X = p.X, Y = p.Y });
        return h != IntPtr.Zero && (h == View.ListView || h == View.DefView || Native.ClassOf(h) is "Progman" or "WorkerW");
    }

    public bool IsDesktopItem(string name) => Keeper.Last.Any(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));

    static bool FencesRunning()
    {
        try { return System.Diagnostics.Process.GetProcessesByName("rundll32").Any(p => { try { return p.MainModule?.FileName != null && HasFences(p.Id); } catch { return false; } }); }
        catch { return false; }

        static bool HasFences(int pid)
        {
            try { return System.Diagnostics.Process.GetProcessById(pid).Modules.Cast<System.Diagnostics.ProcessModule>().Any(m => m.FileName.Contains(@"\Stardock\Fences\", StringComparison.OrdinalIgnoreCase)); }
            catch { return false; }
        }
    }

    void WatchDesktop(string dir)
    {
        if (!Directory.Exists(dir)) return;
        var w = new FileSystemWatcher(dir) { IncludeSubdirectories = false, EnableRaisingEvents = true };
        w.Renamed += (_, e) => messages.Invoker.BeginInvoke(() =>
        {
            IconCache.Forget(e.OldFullPath);
            if (Layout.Rename(e.OldFullPath, e.FullPath)) SaveSoon();
            Keeper.Sync();
        });
        // A deleted item stays in its shelf's list but is not shown: launchers often delete a shortcut and write
        // it again on update, and it should come back to the same shelf.
        w.Deleted += (_, e) => messages.Invoker.BeginInvoke(() =>
        {
            IconCache.Forget(e.FullPath);
            Keeper.Sync();
        });
        w.Changed += (_, e) => messages.Invoker.BeginInvoke(() => IconCache.Forget(e.FullPath));
        watchers.Add(w);
    }

    // ---- Shelves -----------------------------------------------------------------------------------------------

    public ShelfWindow AddWindow(ShelfModel m)
    {
        var w = new ShelfWindow(this, m);
        Windows.Add(w);
        if (!hiddenAll && !ShotMode) w.Show();
        return w;
    }

    public void PlaceAll() { foreach (var w in Windows) w.PlaceAboveDesktop(); }

    public void Refresh() { foreach (var w in Windows) w.LoadItems(); }

    void OnBoxDrawn(Rectangle r, Point at)
    {
        var m = new ContextMenuStrip { RightToLeft = L.Ar ? RightToLeft.Yes : RightToLeft.No };
        var create = new ToolStripMenuItem(L.T("CreateShelfHere")) { Tag = new ThemedMenuRenderer.MenuGlyph(Art.IconAdd) };
        create.Click += (_, _) => NewShelf(r);
        m.Items.Add(create);
        m.Items.Add(new ToolStripMenuItem(L.T("Cancel")));
        ThemedMenuRenderer.Style(m, Theme);
        m.Closed += (_, _) => messages.Invoker.BeginInvoke(() => m.Dispose());
        m.Show(at);
    }

    /// <summary>A new, empty shelf over a screen rectangle; its title is ready to type.</summary>
    public ShelfWindow NewShelf(Rectangle r)
    {
        var scr = Screen.FromRectangle(r);
        var work = new Box(scr.WorkingArea.X, scr.WorkingArea.Y, scr.WorkingArea.Width, scr.WorkingArea.Height);
        float s = scr.Bounds.Width > 0 ? GetScale(scr) : 1;
        var b = new Box(r.X, r.Y, Math.Max(r.Width, (int)(160 * s)), Math.Max(r.Height, (int)(120 * s))).ClampInto(work);
        var m = new ShelfModel
        {
            Monitor = scr.DeviceName,
            Rel = ShelfGeometry.ToRel(b, work),
            IconSize = Settings.DefaultIconSize,
        };
        var edge = ShelfGeometry.NearEdge(b, work, (int)(12 * s));
        m.Edge = edge;
        Layout.Shelves.Add(m);
        SaveSoon();
        var w = AddWindow(m);
        w.BeginInvoke(w.StartTitleRename);
        return w;
    }

    static float GetScale(Screen s)
    {
        var mon = MonitorFromPoint(new Native.POINT { X = s.Bounds.X + 1, Y = s.Bounds.Y + 1 }, 2);
        return GetDpiForMonitor(mon, 0, out uint dx, out _) == 0 ? dx / 96f : 1f;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Native.POINT p, uint flags);
    [System.Runtime.InteropServices.DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr mon, int type, out uint dx, out uint dy);

    public void NewShelfNear(ShelfWindow? w)
    {
        var scr = w?.Monitor ?? Screen.PrimaryScreen!;
        float s = GetScale(scr);
        var wa = scr.WorkingArea;
        var r = new Rectangle(wa.X + wa.Width / 2 - (int)(160 * s), wa.Y + wa.Height / 2 - (int)(120 * s), (int)(320 * s), (int)(240 * s));
        NewShelf(r);
    }

    public void DeleteShelf(ShelfWindow w)
    {
        string title = w.Model.Title.Length > 0 ? w.Model.Title : L.T("DefaultTitle");
        if (MessageBox.Show(L.F("DeleteShelfAsk", title), L.T("AppName"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2, L.Ar ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0) != DialogResult.OK)
            return;
        foreach (var n in w.Model.Items.ToList()) { Layout.Unshelve(n); Keeper.Unhide(n); }
        Layout.Shelves.Remove(w.Model);
        Windows.Remove(w);
        w.Close();
        w.Dispose();
        SaveSoon();
    }

    /// <summary>Hides (or shows again) every shelf and every desktop icon.</summary>
    public void ToggleHideAll()
    {
        hiddenAll = !hiddenAll;
        if (View.Attached) Native.ShowWindow(View.ListView, hiddenAll ? 0 : 5 /* SW_SHOWNA */);
        foreach (var w in Windows) { if (hiddenAll) w.Hide(); else { w.Show(); w.PlaceAboveDesktop(); } }
    }

    void DisplayChanged()
    {
        foreach (var w in Windows) w.Relayout();
        Wallpaper.Reset();
        Keeper.Sync();
    }

    void PreferenceChanged(UserPreferenceCategory c)
    {
        if (c is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Desktop)
        {
            Theme = Theme.Current;
            Wallpaper.Reset();
        }
    }

    // ---- Saving, snapshots -------------------------------------------------------------------------------------

    public void SaveSoon() { if (ShotMode) return; saveTimer.Stop(); saveTimer.Start(); }

    void SaveNow()
    {
        try { LayoutStore.Save(LayoutPath, Layout); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Error(ex); }
    }

    public void SaveSettings()
    {
        try { Settings.Save(SettingsPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Error(ex); }
    }

    public void SaveSnapshot()
    {
        Snapshots.Save(Layout, L.F("SnapshotName", DateTime.Now.ToString("g", L.Culture)), auto: false, DateTime.Now);
    }

    public void RestoreSnapshot(Snapshot snap)
    {
        if (MessageBox.Show(L.F("RestoreSnapshotAsk", snap.Name), L.T("AppName"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2, L.Ar ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0) != DialogResult.OK)
            return;
        Snapshots.Save(Layout, L.T("AutoSnapshot"), auto: true, DateTime.Now); // so the restore can be undone
        var next = snap.Layout.Clone();
        // Keep every saved home: icons parked now must still find their way back.
        foreach (var (k, v) in Layout.Home) next.Home.TryAdd(k, v);
        foreach (var w in Windows.ToList()) { w.Close(); w.Dispose(); }
        Windows.Clear();
        Layout = LayoutStore.Migrate(next);
        Keeper.Sync();
        foreach (var m in Layout.Shelves) AddWindow(m);
        SaveSoon();
    }

    /// <summary>Adds Stardock Fences' fences as shelves (a snapshot is taken first, so it can be undone).</summary>
    public void ImportFences()
    {
        var shelves = FencesReader.Read(Settings.DefaultIconSize);
        if (shelves.Count == 0) return;
        Snapshots.Save(Layout, L.T("AutoSnapshot"), auto: true, DateTime.Now);
        foreach (var m in shelves)
        {
            // Importing again replaces the shelf of the same name rather than adding a second one.
            foreach (var w in Windows.Where(w => w.Model.Title == m.Title && w.Model.Portal == null).ToList())
            {
                Layout.Shelves.Remove(w.Model);
                Windows.Remove(w);
                w.Close();
                w.Dispose();
            }
            // An icon belongs to one shelf only.
            foreach (var n in m.Items) Layout.Unshelve(n);
            Layout.Shelves.Add(m);
            AddWindow(m);
        }
        Keeper.Sync();
        SaveNow();
        tray.ShowBalloonTip(5000, L.T("AppName"), L.F("ImportedFences", shelves.Count), ToolTipIcon.Info);
    }

    /// <summary>Every icon back on the desktop and no shelves (after asking).</summary>
    public void RestoreDesktop()
    {
        if (MessageBox.Show(L.T("RestoreDesktopAsk"), L.T("AppName"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2, L.Ar ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0) != DialogResult.OK)
            return;
        Snapshots.Save(Layout, L.T("AutoSnapshot"), auto: true, DateTime.Now);
        foreach (var w in Windows.ToList()) { w.Close(); w.Dispose(); }
        Windows.Clear();
        Layout.Shelves.Clear();
        Keeper.RestoreAll();
        Layout.Home.Clear();
        Keeper.Sync();
        SaveNow();
    }

    // ---- Tray --------------------------------------------------------------------------------------------------

    void ShowTrayMenu()
    {
        var m = new ContextMenuStrip { RightToLeft = L.Ar ? RightToLeft.Yes : RightToLeft.No };
        ToolStripMenuItem Item(string text, string? glyph, Action click)
        {
            var it = new ToolStripMenuItem(text) { Tag = glyph == null ? null : new ThemedMenuRenderer.MenuGlyph(glyph) };
            it.Click += (_, _) => click();
            return it;
        }
        var header = new ToolStripMenuItem(L.T("AppName") + "  " + L.F("Version", Updater.CurrentText))
        {
            Enabled = false,
            Image = AppIcon().ToBitmap(),
        };
        m.Items.Add(header);
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(Item(L.T("NewShelf"), Art.IconAdd, () => NewShelfNear(null)));
        m.Items.Add(Item(L.T(hiddenAll ? "ShowAll" : "HideAll"), Art.IconHide, ToggleHideAll));

        var snaps = new ToolStripMenuItem(L.T("Snapshots")) { Tag = new ThemedMenuRenderer.MenuGlyph(Art.IconSnapshot) };
        snaps.DropDownItems.Add(Item(L.T("SaveSnapshot"), Art.IconAdd, SaveSnapshot));
        var list = Snapshots.List();
        if (list.Count > 0) snaps.DropDownItems.Add(new ToolStripSeparator());
        foreach (var (_, snap) in list.Take(15))
        {
            string label = snap.Auto ? $"{L.T("AutoSnapshot")}  {snap.Time.ToString("g", L.Culture)}" : snap.Name;
            snaps.DropDownItems.Add(Item(label, null, () => RestoreSnapshot(snap)));
        }
        if (list.Count == 0) snaps.DropDownItems.Add(new ToolStripMenuItem(L.T("NoSnapshots")) { Enabled = false });
        m.Items.Add(snaps);
        m.Items.Add(Item(L.T("RestoreDesktop"), Art.IconRestore, RestoreDesktop));
        if (FencesReader.Available()) m.Items.Add(Item(L.T("ImportFences"), Art.IconAdd, ImportFences));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(Item(L.T("Settings"), Art.IconSettings, OpenSettings));
        m.Items.Add(Item(L.T("CheckForUpdates"), Art.IconUpdate, () => OpenSettings(updates: true)));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(Item(L.T("Exit"), Art.IconExit, ExitThread));
        ThemedMenuRenderer.Style(m, Theme);
        m.Closed += (_, _) => messages.Invoker.BeginInvoke(() => m.Dispose());
        // A tray menu needs the foreground, or it will not close when clicking elsewhere.
        Native.SetForegroundWindow(messages.Handle);
        m.Show(Cursor.Position);
    }

    SettingsForm? settings;

    public void OpenSettings() => OpenSettings(false);

    public void OpenSettings(bool updates)
    {
        if (settings is { IsDisposed: false }) { settings.Activate(); if (updates) settings.ShowUpdates(); return; }
        settings = new SettingsForm(this);
        settings.FormClosed += (_, _) => settings = null;
        settings.Show();
        if (updates) settings.ShowUpdates();
    }

    /// <summary>Settings changed: language, look, roll-up behaviour.</summary>
    public void ApplySettings()
    {
        SaveSettings();
        L.Apply(Settings.Language);
        tray.Text = L.T("AppName");
        Autostart.Apply(Settings.StartWithWindows);
        foreach (var w in Windows) w.Repaint();
    }

    async Task CheckUpdatesQuietly()
    {
        Settings.LastUpdateCheck = DateTime.UtcNow;
        SaveSettings();
        try
        {
            if (await Updater.CheckAsync() is { } rel)
            {
                tray.BalloonTipClicked -= OpenUpdates;
                tray.BalloonTipClicked += OpenUpdates;
                tray.ShowBalloonTip(8000, L.T("AppName"), L.F("UpdateAvailable", rel.Version.ToString(3)), ToolTipIcon.Info);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { }
    }

    void OpenUpdates(object? s, EventArgs e) => OpenSettings(updates: true);

    /// <summary>Called before an update installs: a snapshot to fall back on.</summary>
    public void BeforeUpdate()
    {
        Snapshots.Save(Layout, L.T("AutoSnapshot"), auto: true, DateTime.Now);
        SaveNow();
    }

    // ---- Exit --------------------------------------------------------------------------------------------------

    /// <summary>Puts the desktop back the way Windows had it: every icon visible, auto arrange as it was.</summary>
    public void RestoreExplorer()
    {
        try
        {
            if (!View.Attached) View.Attach();
            if (View.Attached)
            {
                Keeper.RestoreAll();
                Native.ShowWindow(View.ListView, 5);
                if (autoArrangeWasOn) View.SetFolderFlags(DesktopView.FWF_AUTOARRANGE, DesktopView.FWF_AUTOARRANGE);
            }
        }
        catch (Exception ex) { Log.Error(ex); }
    }

    protected override void ExitThreadCore()
    {
        if (exiting) return;
        exiting = true;
        checkTimer.Stop();
        slowTimer.Stop();
        SaveNow();
        RestoreExplorer();
        if (foregroundHook != IntPtr.Zero) Native.UnhookWinEvent(foregroundHook);
        input.Dispose();
        foreach (var w in watchers) w.Dispose();
        foreach (var w in Windows.ToList()) { w.Close(); w.Dispose(); }
        tray.Visible = false;
        tray.Dispose();
        View.Dispose();
        messages.DestroyHandle();
        base.ExitThreadCore();
    }

    /// <summary>A hidden top-level window: receives the Explorer-restart broadcast and the "show" message from a second launch.</summary>
    sealed class MessageWindow : NativeWindow
    {
        readonly ShelvesApp app;
        readonly uint taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
        readonly uint show = Native.RegisterWindowMessage(ShowMessage);
        readonly uint exit = Native.RegisterWindowMessage(ExitMessage);
        readonly uint import = Native.RegisterWindowMessage(ImportMessage);
        public Control Invoker { get; } = new();

        public MessageWindow(ShelvesApp app)
        {
            this.app = app;
            Invoker.CreateControl();
            _ = Invoker.Handle;
            CreateHandle(new CreateParams { Caption = "Shelves.Messages" });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == (int)taskbarCreated) app.ExplorerRestarted();
            else if (m.Msg == (int)show) app.OpenSettings();
            else if (m.Msg == (int)exit) app.ExitThread();
            else if (m.Msg == (int)import) app.ImportFences();
            base.WndProc(ref m);
        }
    }
}

/// <summary>The Run key entry that starts Shelves with Windows.</summary>
static class Autostart
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Apply(bool on)
    {
#if DEBUG
        return; // a development build never registers itself
#endif
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key, writable: true);
            if (k == null) return;
            if (on) k.SetValue("Shelves", $"\"{Environment.ProcessPath}\"");
            else if (k.GetValue("Shelves") != null) k.DeleteValue("Shelves");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { Log.Error(ex); }
    }
}
