using System.Runtime.InteropServices;

namespace Shelves.UI.Shelf;

/// <summary>
/// One shelf on the desktop: a per-pixel-alpha window kept directly above the desktop (below every app window),
/// that rolls up to its title, or to a vertical tab on a side edge, and holds desktop icons or a folder's files.
/// This part holds the window, its geometry, rolling up and z-order; the painting, the icons and the menus are
/// in the other ShelfWindow files.
/// </summary>
sealed partial class ShelfWindow : Form
{
    readonly ShelvesApp app;
    public ShelfModel Model { get; }

    /// <summary>The shelf when open, in screen pixels. While rolled up the window is smaller (see <see cref="Target"/>).</summary>
    Box open;
    /// <summary>Roll state shown now: 0 rolled up, 1 open. Animates toward the model's state (or a peek).</summary>
    double shown = 1;
    bool peeking;
    readonly System.Windows.Forms.Timer anim = new() { Interval = 10 };
    readonly System.Windows.Forms.Timer hover = new() { Interval = 30 };
    DateTime animStart; double animFrom;
    DateTime? enteredAt, leftAt;

    public ShelfWindow(ShelvesApp app, ShelfModel model)
    {
        this.app = app;
        Model = model;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AllowDrop = true;
        KeyPreview = true;
        shown = model.Rolled ? 0 : 1;
        anim.Tick += (_, _) => Animate();
        hover.Tick += (_, _) => HoverTick();
        open = FromModel();
        var b = Target();
        Bounds = new Rectangle(b.X, b.Y, b.W, b.H);
        LoadItems();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80 /* WS_EX_TOOLWINDOW: no taskbar, no Alt+Tab */ | 0x80000 /* WS_EX_LAYERED */;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NoWindowsFrame(Handle);
        Render();
        PlaceAboveDesktop();
        hover.Start();
    }

    /// <summary>
    /// Windows 11 gives top-level windows its own shadow, rounded corners and accent border, drawn outside what the
    /// shelf paints. A shelf draws its own edge, so all three are turned off.
    /// </summary>
    static void NoWindowsFrame(IntPtr hwnd)
    {
        int disabled = 1; // DWMNCRP_DISABLED: no non-client rendering, so no shadow
        Native.DwmSetWindowAttribute(hwnd, 2 /* DWMWA_NCRENDERING_POLICY */, ref disabled, sizeof(int));
        int square = 1; // DWMWCP_DONOTROUND
        Native.DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref square, sizeof(int));
        int none = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        Native.DwmSetWindowAttribute(hwnd, 34 /* DWMWA_BORDER_COLOR */, ref none, sizeof(int));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { tabTimer.Dispose(); anim.Dispose(); hover.Dispose(); openFrame?.Dispose(); watcher?.Dispose(); portalTimer.Dispose(); }
        base.Dispose(disposing);
    }

    public float S => IsHandleCreated ? Native.GetDpiForWindow(Handle) / 96f : DeviceDpi / 96f;
    int TitleH => (int)(30 * S);
    int TabW => (int)(30 * S);
    int Pad => (int)(8 * S);

    // ---- Geometry ---------------------------------------------------------------------------------------------

    /// <summary>The work area of the monitor the model names, or the main monitor while that one is unplugged.</summary>
    Screen ModelScreen() =>
        Screen.AllScreens.FirstOrDefault(s => s.DeviceName == Model.Monitor) ?? Screen.PrimaryScreen!;

    static Box ToBox(Rectangle r) => new(r.X, r.Y, r.Width, r.Height);
    static Rectangle ToRect(Box b) => new(b.X, b.Y, b.W, b.H);

    Box Work => ToBox(Screen.FromRectangle(ToRect(open)).WorkingArea);

    Box FromModel()
    {
        var work = ToBox(ModelScreen().WorkingArea);
        if (Model.Rel.W <= 0 || Model.Rel.H <= 0) return new Box(work.X + 100, work.Y + 100, 320, 240).ClampInto(work);
        var b = ShelfGeometry.FromRel(Model.Rel, work);
        return Model.Edge == Edge.None ? b : ShelfGeometry.DockTo(b, work, Model.Edge);
    }

    /// <summary>Saves the open bounds into the model, relative to the monitor they are on.</summary>
    void ToModel()
    {
        var scr = Screen.FromRectangle(ToRect(open));
        Model.Monitor = scr.DeviceName;
        Model.Rel = ShelfGeometry.ToRel(open, ToBox(scr.WorkingArea));
        app.SaveSoon();
    }

    /// <summary>Monitors, resolution or DPI changed: back to the same relative spot (or the main monitor).</summary>
    public void Relayout()
    {
        open = FromModel();
        LayoutItems();
        Apply();
    }

    Box Rolled => ShelfGeometry.Rolled(open, Model.Edge, TitleH, TabW);

    /// <summary>The window's rectangle for the current roll state.</summary>
    Box Target() => shown >= 1 ? open : ShelfGeometry.Lerp(Rolled, open, Ease(shown));

    static double Ease(double t) => 1 - Math.Pow(1 - t, 3);

    bool IsOpen => shown >= 1;
    bool SideTab => ShelfGeometry.IsSide(Model.Edge) && shown <= 0.5;

    /// <summary>Moves and repaints the window for the current state.</summary>
    void Apply()
    {
        if (!IsHandleCreated) return;
        Render();
    }

    /// <summary>Redraws with the current theme, settings and wallpaper.</summary>
    public void Repaint() { LayoutItems(); Apply(); }

    /// <summary>Opens the title for typing (a new shelf).</summary>
    public void StartTitleRename() => BeginTitleRename();

    // ---- Rolling up -------------------------------------------------------------------------------------------

    public void SetRolled(bool rolled)
    {
        Model.Rolled = rolled;
        peeking = false;
        app.SaveSoon();
        AnimateTo(rolled ? 0 : 1);
    }

    void Peek(bool on)
    {
        if (peeking == on || !Model.Rolled) return;
        peeking = on;
        AnimateTo(on ? 1 : 0);
    }

    double animTarget = 1;

    void AnimateTo(double target)
    {
        animTarget = target;
        if (!app.Settings.AnimateRoll || !Visible) { shown = target; anim.Stop(); LayoutItems(); Apply(); return; }
        animFrom = shown;
        animStart = DateTime.UtcNow;
        if (!anim.Enabled) CacheOpenFrame();
        anim.Start();
    }

    void Animate()
    {
        double t = Math.Min(1, (DateTime.UtcNow - animStart).TotalMilliseconds / 150.0);
        shown = animFrom + (animTarget - animFrom) * t;
        if (t >= 1) { shown = animTarget; anim.Stop(); openFrame?.Dispose(); openFrame = null; }
        Apply();
    }

    /// <summary>Hover to peek: opens a rolled-up shelf after the hover delay, rolls it back after the leave delay.</summary>
    void HoverTick()
    {
        if (!Model.Rolled || !app.Settings.HoverToPeek || !Visible) { enteredAt = leftAt = null; return; }
        var now0 = Target(); // the real size: WinForms' Bounds does not follow UpdateLayeredWindow
        var inside = ToRect(now0).Contains(Cursor.Position);
        var now = DateTime.UtcNow;
        if (inside)
        {
            leftAt = null;
            enteredAt ??= now;
            if (!peeking && (now - enteredAt.Value).TotalMilliseconds >= app.Settings.HoverDelayMs) Peek(true);
        }
        else
        {
            enteredAt = null;
            if (!peeking || Busy) { leftAt = null; return; }
            leftAt ??= now;
            if ((now - leftAt.Value).TotalMilliseconds >= app.Settings.LeaveDelayMs) Peek(false);
        }
    }

    /// <summary>Something that must keep a peeking shelf open: a drag, a menu, a rename.</summary>
    bool Busy => drag != DragKind.None || menuOpen || renameBox != null || dropHover;

    // ---- Z-order: directly above the desktop -------------------------------------------------------------------

    /// <summary>
    /// Puts the shelf right above the desktop window. Normally that is the bottom of the z-order; after Show desktop
    /// (Win+D) the desktop is raised over everything, and the shelf must rise with it or it is covered.
    /// </summary>
    public void PlaceAboveDesktop()
    {
        if (!IsHandleCreated) return;
        var after = AboveDesktop();
        if (after == Handle) return;
        Native.SetWindowPos(Handle, after, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    /// <summary>The window to insert after so the shelf sits just above the desktop (Progman).</summary>
    static IntPtr AboveDesktop()
    {
        var progman = Native.FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return Native.HWND_BOTTOM;
        var prev = GetWindow(progman, 3 /* GW_HWNDPREV */);
        return prev == IntPtr.Zero ? (IntPtr)0 /* HWND_TOP */ : prev;
    }

    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case Native.WM_WINDOWPOSCHANGING:
            {
                var wp = Marshal.PtrToStructure<Native.WINDOWPOS>(m.LParam);
                if ((wp.flags & Native.SWP_NOZORDER) == 0)
                {
                    // Never come forward over app windows (clicking a shelf would otherwise raise it).
                    var after = AboveDesktop();
                    if (after == Handle) wp.flags |= Native.SWP_NOZORDER;
                    else wp.hwndInsertAfter = after;
                    Marshal.StructureToPtr(wp, m.LParam, false);
                }
                break;
            }
            case Native.WM_DPICHANGED:
                // Keep our own bounds (the shelf's size is set in physical pixels); just redraw at the new scale.
                IconCache.Forget();
                LayoutItems();
                Apply();
                m.Result = IntPtr.Zero;
                return;
        }
        if (ShellOps.HandleMenuMessage(ref m)) return;
        base.WndProc(ref m);
    }
}
