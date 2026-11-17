using System.Drawing.Drawing2D;

namespace Shelves.UI.Styling;

/// <summary>Shared drawing: icon-font glyphs, text ink and rounded shapes.</summary>
static class Art
{
    /// <summary>Brand gradient, from the top-left of the app icon to the bottom-right.</summary>
    public static readonly Color BrandLight = Color.FromArgb(96, 120, 255), BrandDark = Color.FromArgb(44, 44, 150);

    /// <summary>The app's mark: two shelves, the top one open with three icons, the lower one rolled up.</summary>
    public static void Mark(Graphics g, RectangleF box, Color c)
    {
        float n = box.Width;
        using var b = new SolidBrush(c);
        using var soft = new SolidBrush(Color.FromArgb(c.A * 3 / 4, c));
        if (n < 22)
        {
            FillRound(g, new RectangleF(box.X, box.Y + n * 0.08f, n, n * 0.5f), n * 0.12f, c);
            FillRound(g, new RectangleF(box.X, box.Y + n * 0.7f, n, n * 0.22f), n * 0.1f, c);
            return;
        }
        // Open shelf: an outline with three icons inside.
        var top = new RectangleF(box.X, box.Y, n, n * 0.6f);
        using (var pen = new Pen(c, Math.Max(1.5f, n * 0.07f)))
        using (var path = RoundRect(top, n * 0.12f))
            g.DrawPath(pen, path);
        float cell = n * 0.2f, gap = (n - cell * 3) / 4f, cy = top.Y + n * 0.21f;
        for (int i = 0; i < 3; i++)
            FillRound(g, new RectangleF(box.X + gap + i * (cell + gap), cy, cell, cell), cell * 0.25f, c);
        // Rolled-up shelf: just its title bar.
        FillRound(g, new RectangleF(box.X, box.Y + n * 0.74f, n, n * 0.22f), n * 0.1f, c);
    }

    // Windows icon font: Segoe Fluent Icons (Win11) or Segoe MDL2 Assets (Win10) share these code points.
    public const string IconSettings = "", IconAdd = "", IconHide = "", IconSnapshot = "",
                        IconRestore = "", IconOpen = "", IconFolder = "", IconUpdate = "",
                        IconExit = "", IconRename = "", IconDelete = "", IconSort = "",
                        IconSize = "", IconPin = "", IconRoll = "", IconDock = "";

    public static readonly string IconFontName =
        new System.Drawing.Text.InstalledFontCollection().Families.Any(f => f.Name == "Segoe Fluent Icons")
            ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    public static Bitmap IconBitmap(string glyph, int px, Color c)
    {
        var bmp = new Bitmap(px, px, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var f = new Font(IconFontName, px * 0.8f, GraphicsUnit.Pixel);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var b = new SolidBrush(c);
        g.DrawString(glyph, f, b, new RectangleF(0, 1, px, px), sf);
        return bmp;
    }

    /// <summary>Draws an icon-font glyph with its real ink centred in <paramref name="box"/> (glyph boxes sit off-centre).</summary>
    public static void GlyphCentered(Graphics g, string glyph, float emPixels, RectangleF box, Color c)
    {
        using var path = new GraphicsPath();
        using var family = new FontFamily(IconFontName);
        path.AddString(glyph, family, (int)FontStyle.Regular, emPixels, PointF.Empty, StringFormat.GenericTypographic);
        var b = path.GetBounds();
        if (b.Width <= 0 || b.Height <= 0) return;
        using (var m = new Matrix())
        {
            m.Translate(box.Left + (box.Width - b.Width) / 2f - b.Left, box.Top + (box.Height - b.Height) / 2f - b.Top);
            path.Transform(m);
        }
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(c);
        g.FillPath(brush, path);
        g.SmoothingMode = old;
    }

    /// <summary>
    /// Draws an icon-font glyph scaled so its ink exactly fills <paramref name="box"/>'s height, centred in it.
    /// (A glyph's ink is smaller than its font size, so sizing by font size leaves it short and off by a fraction.)
    /// </summary>
    public static void GlyphFit(Graphics g, string glyph, RectangleF box, Color c)
    {
        using var path = new GraphicsPath();
        using var family = new FontFamily(IconFontName);
        path.AddString(glyph, family, (int)FontStyle.Regular, 100f, PointF.Empty, StringFormat.GenericTypographic);
        var b = path.GetBounds();
        if (b.Width <= 0 || b.Height <= 0) return;
        float k = box.Height / b.Height;
        using (var m = new Matrix())
        {
            m.Translate(box.Left + (box.Width - b.Width * k) / 2f, box.Top);
            m.Scale(k, k);
            m.Translate(-b.Left, -b.Top);
            path.Transform(m);
        }
        var old = g.SmoothingMode;
        var oldOffset = g.PixelOffsetMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // Pixel edges on whole coordinates, so a box from row 540 to 551 fills rows 540 to 550 exactly instead of
        // spilling half a row past each end.
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var brush = new SolidBrush(c);
        g.FillPath(brush, path);
        g.SmoothingMode = old;
        g.PixelOffsetMode = oldOffset;
    }

    static readonly Dictionary<(string, float, int), (float top, float bottom)> inkCache = [];

    /// <summary>
    /// Where the ink of capitals and digits lands (top and bottom, in pixels from the box top) when TextRenderer
    /// draws <paramref name="f"/> vertically centred in a box <paramref name="boxH"/> tall. GDI does not put the ink
    /// in the middle of the box, so an icon centred on the box sits low; centre it on this instead.
    /// </summary>
    public static (float top, float bottom) TextInk(Font f, int boxH)
    {
        var key = (f.Name, f.Size, boxH);
        if (inkCache.TryGetValue(key, out var hit)) return hit;
        using var bmp = new Bitmap(64, boxH, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Black);
            TextRenderer.DrawText(g, "H0", f, new Rectangle(0, 0, 64, boxH), Color.White, Color.Black,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
        }
        int top = -1, bottom = -1;
        for (int y = 0; y < boxH; y++)
            for (int x = 0; x < 64; x++)
                // Solid rows only: the faint antialiased fringe would make the ink look a row taller than it reads.
                if (bmp.GetPixel(x, y).R > 96) { if (top < 0) top = y; bottom = y; break; }
        // Pixel rows are inclusive: the ink spans the top of row "top" to the bottom of row "bottom".
        var ink = top < 0 ? (boxH * 0.3f, boxH * 0.7f) : (top, bottom + 1f);
        inkCache[key] = ink;
        return ink;
    }

    public static void FillRound(Graphics g, RectangleF r, float rad, Color c)
    {
        using var p = RoundRect(r, rad);
        using var b = new SolidBrush(c);
        g.FillPath(b, p);
    }

    public static GraphicsPath RoundRect(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Crescent with a small four-point star, fitted into a square.</summary>
}
