using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Shelves.Desktop;

/// <summary>
/// Watches the mouse over the empty desktop with a low-level hook, without touching Explorer: a right-drag draws
/// a new shelf, a double-click hides everything. A plain right-click is held back and then replayed, so
/// Explorer's own desktop menu still opens.
/// </summary>
sealed class DesktopInput : IDisposable
{
    static readonly IntPtr Magic = 0x5348454C; // "SHEL": marks the clicks we replay, so the hook lets them through

    readonly Func<Point, bool> emptyDesktop;
    readonly Control invoker;
    readonly Native.HookProc proc;
    IntPtr hook;
    enum State { None, Pending, Drawing }
    State state;
    Point start;
    Point lastDown;
    long lastDownTicks;
    readonly RubberBand band = new();

    /// <summary>A box was drawn on the desktop (screen rectangle, and where the button was released).</summary>
    public event Action<Rectangle, Point>? BoxDrawn;
    public event Action? DesktopDoubleClick;
    /// <summary>The left button went up after being pressed on the desktop (an icon may have been dropped).</summary>
    public event Action<bool>? DesktopRelease;
    /// <summary>Set by the app: the desktop list view, frozen for the moment a dragged icon is dropped.</summary>
    public Func<IntPtr> ListView { get; set; } = () => IntPtr.Zero;
    /// <summary>Set by the app: true while icons are shelved (only then is there anything to hide).</summary>
    public Func<bool> AnyShelved { get; set; } = () => false;
    Point pressedAt;
    /// <summary>Set by the app: true when a point is on Explorer's desktop at all (icon or not).</summary>
    public Func<Point, bool> IsDesktop { get; set; } = _ => false;
    bool pressedOnDesktop;
    public Func<Theme> Theme { get; set; } = () => Shelves.UI.Styling.Theme.Current;

    public DesktopInput(Control invoker, Func<Point, bool> emptyDesktop, bool hook = true)
    {
        this.invoker = invoker;
        this.emptyDesktop = emptyDesktop;
        proc = Hook;
        if (hook) this.hook = Native.SetWindowsHookEx(14 /* WH_MOUSE_LL */, proc, Native.GetModuleHandle(null), 0);
    }

    public void Dispose()
    {
        if (hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        band.Dispose();
    }

    IntPtr Hook(int code, IntPtr w, IntPtr l)
    {
        if (code < 0) return Native.CallNextHookEx(hook, code, w, l);
        var info = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(l);
        if (info.dwExtraInfo == Magic) return Native.CallNextHookEx(hook, code, w, l);
        var pt = new Point(info.pt.X, info.pt.Y);
        try
        {
            switch ((int)w)
            {
                case 0x204: // WM_RBUTTONDOWN
                    if (state == State.None && emptyDesktop(pt))
                    {
                        state = State.Pending;
                        start = pt;
                        return 1;
                    }
                    break;
                case 0x200: // WM_MOUSEMOVE
                    if (state == State.Pending && (Math.Abs(pt.X - start.X) > 6 || Math.Abs(pt.Y - start.Y) > 6))
                        state = State.Drawing;
                    if (state == State.Drawing)
                    {
                        var r = Rect(start, pt);
                        invoker.BeginInvoke(() => band.Show(r, Theme()));
                    }
                    break;
                case 0x205: // WM_RBUTTONUP
                    if (state == State.Pending)
                    {
                        state = State.None;
                        // Not a drag after all: give Explorer its right-click back.
                        invoker.BeginInvoke(ReplayRightClick);
                        return 1;
                    }
                    if (state == State.Drawing)
                    {
                        state = State.None;
                        var r = Rect(start, pt);
                        invoker.BeginInvoke(() => { band.Hide(); BoxDrawn?.Invoke(r, pt); });
                        return 1;
                    }
                    break;
                case 0x202: // WM_LBUTTONUP
                    if (pressedOnDesktop)
                    {
                        pressedOnDesktop = false;
                        bool dragged = Math.Abs(pt.X - pressedAt.X) > 4 || Math.Abs(pt.Y - pressedAt.Y) > 4;
                        // A drop is about to make Explorer pull hidden icons back on screen. Lock the list view's
                        // drawing first, so they never show. WM_SETREDRAW does not hold: Explorer turns drawing back on
                        // while it handles the drop. LockWindowUpdate takes effect at once and only we lift it.
                        bool frozen = dragged && AnyShelved() && ListView() is var lv && lv != IntPtr.Zero
                                      && Native.LockWindowUpdate(lv);
                        invoker.BeginInvoke(() => DesktopRelease?.Invoke(frozen));
                    }
                    break;
                case 0x201: // WM_LBUTTONDOWN
                {
                    pressedOnDesktop = IsDesktop(pt);
                    pressedAt = pt;
                    long now = Environment.TickCount64;
                    bool empty = emptyDesktop(pt);
                    int dx = Native.GetSystemMetrics(36 /* SM_CXDOUBLECLK */), dy = Native.GetSystemMetrics(37);
                    if (empty && now - lastDownTicks <= Native.GetDoubleClickTime()
                        && Math.Abs(pt.X - lastDown.X) <= dx && Math.Abs(pt.Y - lastDown.Y) <= dy)
                    {
                        lastDownTicks = 0;
                        invoker.BeginInvoke(() => DesktopDoubleClick?.Invoke());
                    }
                    else if (empty) { lastDownTicks = now; lastDown = pt; }
                    else lastDownTicks = 0;
                    break;
                }
            }
        }
        catch (Exception ex) { Log.Error(ex); state = State.None; }
        return Native.CallNextHookEx(hook, code, w, l);
    }

    static Rectangle Rect(Point a, Point b) =>
        Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    static void ReplayRightClick()
    {
        var inputs = new INPUT[]
        {
            new() { type = 0, mi = new MOUSEINPUT { dwFlags = 0x0008 /* RIGHTDOWN */, dwExtraInfo = Magic } },
            new() { type = 0, mi = new MOUSEINPUT { dwFlags = 0x0010 /* RIGHTUP */, dwExtraInfo = Magic } },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public MOUSEINPUT mi; }
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);

    /// <summary>The box shown while drawing a new shelf.</summary>
    sealed class RubberBand : Form
    {
        public RubberBand()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x80000 | 0x20 /* WS_EX_TRANSPARENT: clicks pass through */ | 0x08000000 /* NOACTIVATE */;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        public void Show(Rectangle r, Theme t)
        {
            if (r.Width < 2 || r.Height < 2) return;
            if (!Visible) { Bounds = r; Show(); }
            using var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float s = DeviceDpi / 96f;
                using var path = Art.RoundRect(new RectangleF(1, 1, r.Width - 2, r.Height - 2), 8 * s);
                using var fill = new SolidBrush(Color.FromArgb(60, t.Accent));
                using var pen = new Pen(Color.FromArgb(220, t.Accent), 2 * s) { DashStyle = DashStyle.Dash };
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            Native.PushLayered(Handle, bmp, r.X, r.Y);
        }
    }
}
