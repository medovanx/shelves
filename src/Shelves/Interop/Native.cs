using System.Runtime.InteropServices;

namespace Shelves.Interop;

static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int CX, CY; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)] public struct TRACKMOUSEEVENT { public int cbSize; public uint dwFlags; public IntPtr hwndTrack; public uint dwHoverTime; }
    [StructLayout(LayoutKind.Sequential)] public struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }
    [StructLayout(LayoutKind.Sequential)] public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct AccentPolicy { public int AccentState, AccentFlags, GradientColor, AnimationId; }
    [StructLayout(LayoutKind.Sequential)] public struct WindowCompositionAttribData { public int Attribute; public IntPtr Data; public int SizeOfData; }

    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    public delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
    public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string? cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? name);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr hwnd, ref POINT p);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT tme);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string msg);
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] public static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttribData data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);

    public static readonly IntPtr HWND_BOTTOM = 1;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    public const int WM_WINDOWPOSCHANGING = 0x46, WM_MOUSEACTIVATE = 0x21, WM_NCHITTEST = 0x84, WM_DISPLAYCHANGE = 0x7E,
        WM_SETTINGCHANGE = 0x1A, WM_DPICHANGED = 0x2E0, WM_MOUSELEAVE = 0x2A3;
    public const int MA_NOACTIVATE = 3;

    public static string ClassOf(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>For --shots: render light or dark whatever Windows uses.</summary>
    public static bool? ForceDark { get; set; }

    /// <summary>True when Windows uses a dark system theme.</summary>
    public static bool SystemDark()
    {
        if (ForceDark is { } forced) return forced;
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return k?.GetValue("SystemUsesLightTheme") is not int v || v == 0;
    }

    /// <summary>False when the user turned off transparency effects in Windows.</summary>
    public static bool TransparencyOn()
    {
        if (Environment.GetEnvironmentVariable("SHELVES_GLASS") == "1") return true; // for testing the glass

        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return k?.GetValue("EnableTransparency") is not int v || v != 0;
    }

    /// <summary>False when Windows animations are off (Settings, Accessibility, Visual effects).</summary>
    public static bool AnimationsOn()
    {
        bool on = true;
        SystemParametersInfo(0x1042 /* SPI_GETCLIENTAREAANIMATION */, 0, ref on, 0);
        return on;
    }
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, ref bool value, uint ini);

    public static void RoundCorners(IntPtr hwnd, bool dark, bool round = true)
    {
        int pref = round ? 2 : 1; // DWMWCP_ROUND or DWMWCP_DONOTROUND
        DwmSetWindowAttribute(hwnd, 33, ref pref, sizeof(int));
        int d = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref d, sizeof(int));
    }

    /// <summary>Blurs what is behind the window (accent 3 is blur, 4 acrylic). Off when <paramref name="on"/> is false.</summary>
    public static void Blur(IntPtr hwnd, bool on, bool acrylic = false, Color tint = default)
    {
        var accent = new AccentPolicy
        {
            AccentState = !on ? 0 : acrylic ? 4 : 3,
            AccentFlags = 2,
            GradientColor = (tint.A << 24) | (tint.B << 16) | (tint.G << 8) | tint.R,
        };
        int size = Marshal.SizeOf(accent);
        var p = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, p, false);
            var data = new WindowCompositionAttribData { Attribute = 19 /* WCA_ACCENT_POLICY */, Data = p, SizeOfData = size };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally { Marshal.FreeHGlobal(p); }
    }

    /// <summary>Puts a 32bpp premultiplied bitmap on a layered window at a screen position.</summary>
    public static void PushLayered(IntPtr hwnd, Bitmap bmp, int x, int y, byte alpha = 255)
    {
        var screen = GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(screen);
        var hbmp = bmp.GetHbitmap(Color.FromArgb(0));
        var old = SelectObject(mem, hbmp);
        try
        {
            var sz = new SIZE { CX = bmp.Width, CY = bmp.Height };
            var src = new POINT();
            var dst = new POINT { X = x, Y = y };
            var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 };
            UpdateLayeredWindow(hwnd, screen, ref dst, ref sz, mem, ref src, 0, ref blend, 2 /* ULW_ALPHA */);
        }
        finally
        {
            SelectObject(mem, old);
            DeleteObject(hbmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
