using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Shelves.Desktop;

/// <summary>
/// A blurred copy of each monitor's wallpaper, drawn behind shelves as their frosted glass. Windows' own blur does
/// not reach per-pixel-alpha windows, and the only thing behind a shelf is the wallpaper, so drawing it ourselves
/// looks the same and works on every build.
/// </summary>
sealed class Wallpaper : IDisposable
{
    const int Shrink = 8; // blur at an eighth of the size: fast, and the upscale smooths it further

    readonly Dictionary<Rectangle, (string key, Bitmap img)> cache = [];

    /// <summary>Raised when a monitor's wallpaper changed (a new picture, slideshow, colour).</summary>
    public event Action? Changed;

    /// <summary>The blurred wallpaper of the monitor containing <paramref name="monitor"/>, at an eighth of its size.</summary>
    /// <summary>For --shots: a made-up wallpaper (already blurred) used for every monitor.</summary>
    public Bitmap? Override { get; set; }

    public Bitmap? Blurred(Rectangle monitor)
    {
        if (Override != null) return Override;
        try
        {
            var (path, color, pos) = Info(monitor);
            string key = $"{path}|{color.ToArgb()}|{pos}|{(File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0)}";
            if (cache.TryGetValue(monitor, out var hit) && hit.key == key) return hit.img;
            var img = Render(monitor, path, color, pos);
            if (cache.Remove(monitor, out var old)) old.img.Dispose();
            cache[monitor] = (key, img);
            return img;
        }
        catch (Exception ex) when (ex is COMException or IOException or ArgumentException or OutOfMemoryException)
        {
            Log.Error(ex);
            return null;
        }
    }

    /// <summary>Re-reads every wallpaper; raises <see cref="Changed"/> if any differs from the cached one.</summary>
    public void Poll()
    {
        bool changed = false;
        foreach (var m in cache.Keys.ToList())
        {
            var before = cache[m].key;
            Blurred(m);
            changed |= cache.TryGetValue(m, out var now) && now.key != before;
        }
        if (changed) Changed?.Invoke();
    }

    public void Reset()
    {
        foreach (var v in cache.Values) v.img.Dispose();
        cache.Clear();
        Changed?.Invoke();
    }

    public void Dispose() { foreach (var v in cache.Values) v.img.Dispose(); cache.Clear(); }

    static (string path, Color color, int pos) Info(Rectangle monitor)
    {
        var dw = (IDesktopWallpaper)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD"))!)!;
        try
        {
            dw.GetMonitorDevicePathCount(out uint n);
            string? id = null;
            for (uint i = 0; i < n; i++)
            {
                dw.GetMonitorDevicePathAt(i, out var mid);
                if (dw.GetMonitorRECT(mid, out var r) != 0) continue;
                if (Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom) == monitor) { id = mid; break; }
            }
            string path = "";
            if (id != null) dw.GetWallpaper(id, out path);
            dw.GetBackgroundColor(out uint c);
            dw.GetPosition(out int pos);
            return (path ?? "", Color.FromArgb((int)(c & 0xFF), (int)((c >> 8) & 0xFF), (int)((c >> 16) & 0xFF)), pos);
        }
        finally { Marshal.ReleaseComObject(dw); }
    }

    /// <summary>For --compose: the monitor's wallpaper at full size, not blurred.</summary>
    public Bitmap? Sharp(Rectangle monitor)
    {
        try { var (path, color, pos) = Info(monitor); return Render(monitor, path, color, pos, 1, blur: false); }
        catch (Exception ex) when (ex is COMException or IOException or ArgumentException) { return null; }
    }

    static Bitmap Render(Rectangle monitor, string path, Color color, int pos) => Render(monitor, path, color, pos, Shrink, blur: true);

    static Bitmap Render(Rectangle monitor, string path, Color color, int pos, int Shrink, bool blur)
    {
        int w = Math.Max(1, monitor.Width / Shrink), h = Math.Max(1, monitor.Height / Shrink);
        var small = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(small))
        {
            g.Clear(color);
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            if (path.Length > 0 && File.Exists(path))
            {
                using var src = LoadImage(path);
                var dst = new RectangleF(0, 0, w, h);
                float iw = src.Width, ih = src.Height;
                switch (pos)
                {
                    case 0: // center
                        g.DrawImage(src, (w - iw / Shrink) / 2, (h - ih / Shrink) / 2, iw / Shrink, ih / Shrink);
                        break;
                    case 1: // tile
                        for (float y = 0; y < h; y += ih / Shrink)
                            for (float x = 0; x < w; x += iw / Shrink)
                                g.DrawImage(src, x, y, iw / Shrink, ih / Shrink);
                        break;
                    case 2: // stretch
                        g.DrawImage(src, dst);
                        break;
                    case 3: // fit
                    {
                        float k = Math.Min(w / iw, h / ih);
                        g.DrawImage(src, (w - iw * k) / 2, (h - ih * k) / 2, iw * k, ih * k);
                        break;
                    }
                    default: // fill, span
                    {
                        float k = Math.Max(w / iw, h / ih);
                        g.DrawImage(src, (w - iw * k) / 2, (h - ih * k) / 2, iw * k, ih * k);
                        break;
                    }
                }
            }
        }
        if (blur) { BoxBlur(small, 3); BoxBlur(small, 3); BoxBlur(small, 3); }
        return small;
    }

    static Image LoadImage(string path)
    {
        // Read into memory so the wallpaper file is not kept open.
        var bytes = File.ReadAllBytes(path);
        return Image.FromStream(new MemoryStream(bytes));
    }

    /// <summary>One horizontal and one vertical box-blur pass; three passes approximate a gaussian.</summary>
    /// <summary>Blurs a bitmap in place the way wallpapers are (for --shots).</summary>
    public static void Blur(Bitmap bmp) { BoxBlur(bmp, 3); BoxBlur(bmp, 3); BoxBlur(bmp, 3); }

    static unsafe void BoxBlur(Bitmap bmp, int r)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            int w = bmp.Width, h = bmp.Height, stride = data.Stride / 4;
            var src = (uint*)data.Scan0;
            var tmp = new uint[w * h];
            Pass(src, tmp, w, h, stride, r, horizontal: true);
            fixed (uint* t = tmp) Pass(t, null, w, h, w, r, horizontal: false, dst: src, dstStride: stride);
        }
        finally { bmp.UnlockBits(data); }
    }

    static unsafe void Pass(uint* src, uint[]? tmp, int w, int h, int stride, int r, bool horizontal, uint* dst = null, int dstStride = 0)
    {
        int outer = horizontal ? h : w, inner = horizontal ? w : h;
        for (int o = 0; o < outer; o++)
        {
            int sa = 0, sr = 0, sg = 0, sb = 0, n = 2 * r + 1;
            for (int i = -r; i <= r; i++)
            {
                uint p = At(src, stride, horizontal, o, Math.Clamp(i, 0, inner - 1));
                sa += (int)(p >> 24); sr += (int)((p >> 16) & 0xFF); sg += (int)((p >> 8) & 0xFF); sb += (int)(p & 0xFF);
            }
            for (int i = 0; i < inner; i++)
            {
                uint v = (uint)(sa / n) << 24 | (uint)(sr / n) << 16 | (uint)(sg / n) << 8 | (uint)(sb / n);
                int x = horizontal ? i : o, y = horizontal ? o : i;
                if (tmp != null) tmp[y * w + x] = v; else dst[y * dstStride + x] = v;
                uint pOut = At(src, stride, horizontal, o, Math.Clamp(i - r, 0, inner - 1));
                uint pIn = At(src, stride, horizontal, o, Math.Clamp(i + r + 1, 0, inner - 1));
                sa += (int)(pIn >> 24) - (int)(pOut >> 24);
                sr += (int)((pIn >> 16) & 0xFF) - (int)((pOut >> 16) & 0xFF);
                sg += (int)((pIn >> 8) & 0xFF) - (int)((pOut >> 8) & 0xFF);
                sb += (int)(pIn & 0xFF) - (int)(pOut & 0xFF);
            }
        }
    }

    static unsafe uint At(uint* src, int stride, bool horizontal, int o, int i) =>
        horizontal ? src[o * stride + i] : src[i * stride + o];
}

[ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IDesktopWallpaper
{
    void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, [MarshalAs(UnmanagedType.LPWStr)] string path);
    void GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, [MarshalAs(UnmanagedType.LPWStr)] out string path);
    void GetMonitorDevicePathAt(uint index, [MarshalAs(UnmanagedType.LPWStr)] out string id);
    void GetMonitorDevicePathCount(out uint count);
    [PreserveSig] int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string id, out Native.RECT r);
    void SetBackgroundColor(uint color);
    void GetBackgroundColor(out uint color);
    void SetPosition(int pos);
    void GetPosition(out int pos);
}
