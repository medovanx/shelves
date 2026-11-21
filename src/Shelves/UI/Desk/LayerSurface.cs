using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Shelves.UI.Desk;

/// <summary>
/// A screen-sized 32-bit picture kept for a layered window: drawn into in place and pushed to the window, only the
/// changed part each time, so redrawing one icon on a full-screen window stays cheap.
/// </summary>
sealed class LayerSurface : IDisposable
{
    readonly IntPtr dc, dib, oldObj;
    public Bitmap Bitmap { get; }
    public int Width { get; }
    public int Height { get; }

    public LayerSurface(int w, int h)
    {
        Width = Math.Max(1, w);
        Height = Math.Max(1, h);
        var screen = Native.GetDC(IntPtr.Zero);
        dc = Native.CreateCompatibleDC(screen);
        Native.ReleaseDC(IntPtr.Zero, screen);
        var bi = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = Width,
            biHeight = -Height, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        dib = CreateDIBSection(dc, ref bi, 0, out var bits, IntPtr.Zero, 0);
        oldObj = Native.SelectObject(dc, dib);
        // The same pixels seen by GDI+: premultiplied alpha, as UpdateLayeredWindow wants.
        Bitmap = new Bitmap(Width, Height, Width * 4, PixelFormat.Format32bppPArgb, bits);
    }

    /// <summary>Shows the picture's <paramref name="dirty"/> part (or all of it) in the window at <paramref name="at"/>.</summary>
    public unsafe void Push(IntPtr hwnd, Point at, Rectangle? dirty = null)
    {
        var dst = new Native.POINT { X = at.X, Y = at.Y };
        var src = new Native.POINT();
        var size = new Native.SIZE { CX = Width, CY = Height };
        var blend = new Native.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        var r = dirty is { } d ? new Native.RECT { Left = d.Left, Top = d.Top, Right = d.Right, Bottom = d.Bottom } : default;
        var info = new UPDATELAYEREDWINDOWINFO
        {
            cbSize = (uint)sizeof(UPDATELAYEREDWINDOWINFO),
            hdcDst = IntPtr.Zero,
            pptDst = &dst,
            psize = &size,
            hdcSrc = dc,
            pptSrc = &src,
            pblend = &blend,
            dwFlags = 2 /* ULW_ALPHA */,
            prcDirty = dirty == null ? null : &r,
        };
        if (!UpdateLayeredWindowIndirect(hwnd, ref info) && dirty != null)
        {
            // A dirty rectangle is refused when the window has not been shown whole once (or moved): push it all.
            info.prcDirty = null;
            UpdateLayeredWindowIndirect(hwnd, ref info);
        }
    }

    public void Dispose()
    {
        Bitmap.Dispose();
        Native.SelectObject(dc, oldObj);
        Native.DeleteObject(dib);
        Native.DeleteDC(dc);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    unsafe struct UPDATELAYEREDWINDOWINFO
    {
        public uint cbSize;
        public IntPtr hdcDst;
        public Native.POINT* pptDst;
        public Native.SIZE* psize;
        public IntPtr hdcSrc;
        public Native.POINT* pptSrc;
        public uint crKey;
        public Native.BLENDFUNCTION* pblend;
        public uint dwFlags;
        public Native.RECT* prcDirty;
    }

    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindowIndirect(IntPtr hwnd, ref UPDATELAYEREDWINDOWINFO info);
}
