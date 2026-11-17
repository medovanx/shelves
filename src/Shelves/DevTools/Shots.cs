using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Shelves.DevTools;

/// <summary>
/// --shots &lt;dir&gt;: README images drawn off screen on a made-up wallpaper, with shelves showing sample files
/// from a temporary folder. Nothing of the user's own desktop, icons or wallpaper is ever in them.
/// </summary>
static class Shots
{
    public static int Run(string dir)
    {
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("SHELVES_GLASS", "1");
        string samples = MakeSamples();
        // An empty data folder, so no real layout or snapshot of the user's ever shows up.
        Environment.SetEnvironmentVariable("SHELVES_DATA", Path.Combine(samples, "data"));
        try
        {
            foreach (bool dark in new[] { true, false })
                foreach (var lang in new[] { "en", "ar" })
                {
                    L.Ar = lang == "ar";
                    Native.ForceDark = dark;
                    Hero(Path.Combine(dir, $"hero-{(dark ? "dark" : "light")}-{lang}.png"), samples, dark);
                }
            foreach (var lang in new[] { "en", "ar" })
            {
                L.Ar = lang == "ar";
                Native.ForceDark = true;
                SettingsPages(dir, lang);
            }
        }
        finally
        {
            Native.ForceDark = null;
            try { Directory.Delete(samples, true); } catch { }
        }
        return 0;
    }

    /// <summary>A desktop with five shelves: two floating, one on each side edge, one on the bottom edge.</summary>
    static void Hero(string path, string samples, bool dark)
    {
        var mon = Screen.PrimaryScreen!.Bounds;
        int W = mon.Width, H = mon.Height;
        using var wall = MakeWallpaper(W, H, dark);
        using var small = new Bitmap(wall, W / 8, H / 8);
        Wallpaper.Blur(small);

        var app = new ShelvesApp(false, shot: true) { Theme = Theme.Current };
        app.Wallpaper.Override = small;
        app.Settings.Opacity = 50;
        string P(string sub) => Path.Combine(samples, sub);
        float s = 1;
        var specs = new (ShelfModel m, Box box, double roll, bool hot)[]
        {
            (new ShelfModel { Title = L.Ar ? "العمل" : "Work", Portal = P("Work") }, new Box(mon.X + 360, mon.Y + 90, 420, 340), 1, true),
            (new ShelfModel { Title = L.Ar ? "الوسائط" : "Media", Portal = P("Media") }, new Box(mon.X + 830, mon.Y + 90, 380, 240), 1, false),
            (new ShelfModel { Title = L.Ar ? "المشاريع" : "Projects", Portal = P("Projects"), Edge = Edge.Left }, new Box(mon.X, mon.Y + 300, 300, 360), 1, false),
            (new ShelfModel { Title = L.Ar ? "الأدوات" : "Tools", Portal = P("Tools"), Edge = Edge.Right, Rolled = true }, new Box(mon.Right - 300, mon.Y + 260, 300, 330), 0, false),
            (new ShelfModel { Title = L.Ar ? "الأرشيف" : "Archive", Portal = P("Archive"), Edge = Edge.Bottom, Rolled = true, IconSize = IconSize.Small }, new Box(mon.X + 520, mon.Bottom - 48 - 260, 440, 260), 0, false),
        };
        using var canvas = new Bitmap(W, H - 48, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.DrawImage(wall, 0, 0);
            foreach (var (m, box, roll, hot) in specs)
            {
                var w = new ShelfWindow(app, m);
                w.SetHover(hot);
                using var bmp = w.Snapshot(box, roll);
                var at = ShelfGeometry.Lerp(ShelfGeometry.Rolled(box, m.Edge, (int)(30 * s), (int)(30 * s)), box, roll);
                g.DrawImage(bmp, at.X - mon.X, at.Y - mon.Y);
                w.Dispose();
            }
        }
        // The interesting part of the screen, at full size.
        var crop = new Rectangle(0, 50, W, H - 48 - 50);
        using var outBmp = canvas.Clone(crop, PixelFormat.Format32bppArgb);
        outBmp.Save(path, ImageFormat.Png);
        app.Wallpaper.Override = null;
    }

    static Bitmap MakeWallpaper(int w, int h, bool dark)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var a = dark ? Color.FromArgb(28, 30, 70) : Color.FromArgb(150, 170, 245);
        var b = dark ? Color.FromArgb(70, 30, 90) : Color.FromArgb(245, 190, 215);
        using (var grad = new LinearGradientBrush(new Rectangle(0, 0, w, h), a, b, 35f))
            g.FillRectangle(grad, 0, 0, w, h);
        // Soft shapes, so the frosted glass has something to blur.
        var blobs = new (float x, float y, float r, Color c)[]
        {
            (0.18f, 0.25f, 0.22f, dark ? Color.FromArgb(120, 60, 120, 255) : Color.FromArgb(140, 90, 150, 255)),
            (0.55f, 0.20f, 0.18f, dark ? Color.FromArgb(110, 230, 90, 160) : Color.FromArgb(130, 255, 140, 180)),
            (0.85f, 0.55f, 0.25f, dark ? Color.FromArgb(110, 40, 180, 200) : Color.FromArgb(130, 90, 210, 230)),
            (0.35f, 0.85f, 0.20f, dark ? Color.FromArgb(100, 250, 170, 60) : Color.FromArgb(130, 255, 205, 110)),
        };
        foreach (var (x, y, r, c) in blobs)
        {
            float rad = r * w;
            using var path = new GraphicsPath();
            path.AddEllipse(x * w - rad, y * h - rad, rad * 2, rad * 2);
            using var pg = new PathGradientBrush(path) { CenterColor = c, SurroundColors = [Color.FromArgb(0, c)] };
            g.FillPath(pg, path);
        }
        return bmp;
    }

    /// <summary>Sample files with ordinary names, so the shelves show real shell icons.</summary>
    static string MakeSamples()
    {
        string root = Path.Combine(Path.GetTempPath(), "ShelvesShots-" + Guid.NewGuid().ToString("N")[..8]);
        var sets = new Dictionary<string, string[]>
        {
            ["Work"] = ["Quarterly report.docx", "Budget.xlsx", "Roadmap.pdf", "Meeting notes.txt", "Pitch.pptx", "Contracts/"],
            ["Media"] = ["Sunset.png", "Hills.png", "Holiday.mp4", "Podcast.mp3"],
            ["Projects"] = ["Website/", "Mobile app/", "Design.png", "Ideas.txt"],
            ["Tools"] = ["Scripts/", "setup.ps1", "config.json"],
            ["Archive"] = ["2025/", "2024/", "Old photos.zip"],
        };
        foreach (var (folder, files) in sets)
        {
            string d = Path.Combine(root, folder);
            Directory.CreateDirectory(d);
            foreach (var f in files)
            {
                if (f.EndsWith('/')) { Directory.CreateDirectory(Path.Combine(d, f.TrimEnd('/'))); continue; }
                string p = Path.Combine(d, f);
                if (f.EndsWith(".png")) SamplePicture(p, f.GetHashCode());
                else File.WriteAllText(p, "");
            }
        }
        return root;
    }

    /// <summary>A small landscape-like picture, so image files get a thumbnail.</summary>
    static void SamplePicture(string path, int seed)
    {
        var rnd = new Random(seed);
        using var bmp = new Bitmap(256, 192);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var sky1 = Color.FromArgb(rnd.Next(60, 140), rnd.Next(100, 180), 230);
        using (var sky = new LinearGradientBrush(new Rectangle(0, 0, 256, 192), sky1, Color.FromArgb(250, 190, 140), 90f))
            g.FillRectangle(sky, 0, 0, 256, 192);
        using (var sun = new SolidBrush(Color.FromArgb(255, 230, 150))) g.FillEllipse(sun, 150, 50, 50, 50);
        using var hill = new SolidBrush(Color.FromArgb(60, 110 + rnd.Next(40), 90));
        g.FillPolygon(hill, [new Point(0, 192), new Point(0, 120), new Point(90, 80), new Point(170, 130), new Point(256, 100), new Point(256, 192)]);
        bmp.Save(path, ImageFormat.Png);
    }

    static void SettingsPages(string dir, string lang)
    {
        var app = new ShelvesApp(false, shot: true) { Theme = Theme.Current };
        var f = new SettingsForm(app);
        f.Show();
        for (int i = 0; i < f.PageCount; i++)
        {
            f.ShowPageForShot(i);
            Save(f, Path.Combine(dir, $"settings-{i}-{lang}.png"));
        }
        f.Close();
    }

    /// <summary>Captures the window as it really looks on screen, but only when nothing covers any part of it.</summary>
    static void Save(Form f, string path)
    {
        f.TopMost = true;
        f.BringToFront();
        f.Refresh();
        for (int i = 0; i < 10; i++) { Application.DoEvents(); Thread.Sleep(30); }
        var r = f.Bounds;
        if (Native.DwmGetWindowAttribute(f.Handle, 9, out Native.RECT wr, System.Runtime.InteropServices.Marshal.SizeOf<Native.RECT>()) == 0)
            r = Rectangle.FromLTRB(wr.Left, wr.Top, wr.Right, wr.Bottom);
        foreach (var pt in new[] { new Point(r.Left + 8, r.Top + 8), new Point(r.Right - 8, r.Top + 8), new Point(r.Left + r.Width / 2, r.Top + r.Height / 2),
                                   new Point(r.Left + 8, r.Bottom - 8), new Point(r.Right - 8, r.Bottom - 8) })
            if (Native.GetAncestor(Native.WindowFromPoint(new Native.POINT { X = pt.X, Y = pt.Y }), 2 /* GA_ROOT */) != f.Handle)
            {
                Console.Error.WriteLine($"Skipped {Path.GetFileName(path)}: the window was covered.");
                return;
            }
        using var bmp = new Bitmap(r.Width, r.Height);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
        bmp.Save(path, ImageFormat.Png);
    }
}
