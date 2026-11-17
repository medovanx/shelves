using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Shelves.DevTools;

/// <summary>--render-icon &lt;dir&gt;: writes app.ico (16 to 256 px, PNG entries) and icon-512.png from <see cref="Art.Mark"/>.</summary>
static class IconMaker
{
    public static int Run(string dir)
    {
        Directory.CreateDirectory(dir);
        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
        var pngs = sizes.Select(s =>
        {
            using var bmp = Render(s);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }).ToArray();

        using (var fs = File.Create(Path.Combine(dir, "app.ico")))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] % 256)); w.Write((byte)(sizes[i] % 256)); w.Write((byte)0); w.Write((byte)0);
                w.Write((ushort)1); w.Write((ushort)32); w.Write(pngs[i].Length); w.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (var p in pngs) w.Write(p);
        }
        using (var big = Render(512)) big.Save(Path.Combine(dir, "icon-512.png"), ImageFormat.Png);
        return 0;
    }

    public static Bitmap Render(int n)
    {
        var bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        float inset = n <= 24 ? 0 : n * 0.03f;
        var tile = new RectangleF(inset, inset, n - inset * 2, n - inset * 2);
        using (var path = Art.RoundRect(tile, n * 0.22f))
        {
            using (var grad = new LinearGradientBrush(tile, Art.BrandLight, Art.BrandDark, LinearGradientMode.ForwardDiagonal))
                g.FillPath(grad, path);
            using var glow = new LinearGradientBrush(tile, Color.FromArgb(46, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical);
            g.FillPath(glow, path);
        }
        float m = n <= 24 ? n * 0.2f : n * 0.22f;
        Art.Mark(g, new RectangleF(m, m * 1.05f, n - m * 2, n - m * 2), Color.White);
        return bmp;
    }
}
