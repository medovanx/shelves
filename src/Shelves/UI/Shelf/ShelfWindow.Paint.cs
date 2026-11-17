using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace Shelves.UI.Shelf;

sealed partial class ShelfWindow
{
    bool hot; // the mouse is over the shelf

    Theme Th => app.Theme;

    /// <summary>The title bar in client coordinates: at the bottom for a bottom shelf (it rolls down to it).</summary>
    Rectangle TitleRect(Size size) =>
        SideTab ? new Rectangle(0, 0, size.Width, size.Height)
        : Model.Edge == Edge.Bottom ? new Rectangle(0, size.Height - TitleH, size.Width, TitleH)
        : new Rectangle(0, 0, size.Width, TitleH);

    /// <summary>Where icons go, in client coordinates of the open shelf.</summary>
    Rectangle ContentRect =>
        Model.Edge == Edge.Bottom
            ? new Rectangle(Pad, Pad, open.W - Pad * 2, open.H - TitleH - TabH - Pad * 2)
            : new Rectangle(Pad, TitleH + TabH + Pad / 2, open.W - Pad * 2, open.H - TitleH - TabH - Pad - Pad / 2);

    /// <summary>
    /// Offset from the open shelf's client coordinates to the window's while it is part rolled: content keeps its
    /// place on screen while the window around it grows or shrinks.
    /// </summary>
    Point ContentShift(Box now) => new(open.X - now.X, open.Y - now.Y);

    /// <summary>For --shots: the shelf as it would look on screen, at <paramref name="box"/>, without showing a window.</summary>
    public Bitmap Snapshot(Box box, double roll = 1)
    {
        open = box;
        shown = roll;
        LayoutItems();
        var b = Target();
        var bmp = new Bitmap(b.W, b.H, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp)) PaintShelf(g, b);
        return bmp;
    }

    public void SetHover(bool on) => hot = on;

    /// <summary>The open shelf, drawn once when a roll starts; each frame of the roll is a crop of it.</summary>
    Bitmap? openFrame;

    void Render()
    {
        var box = Target();
        if (box.W <= 0 || box.H <= 0) return;
        using var bmp = new Bitmap(box.W, box.H, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            if (anim.Enabled && openFrame != null) PaintFrame(g, box);
            else PaintShelf(g, box);
        }
        Native.PushLayered(Handle, bmp, box.X, box.Y);
    }

    /// <summary>Draws the open shelf once, for the frames of a roll.</summary>
    void CacheOpenFrame()
    {
        openFrame?.Dispose();
        double saved = shown;
        shown = 1;
        openFrame = new Bitmap(open.W, open.H, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(openFrame)) PaintShelf(g, open);
        shown = saved;
    }

    /// <summary>
    /// One frame of a roll: the part of the open shelf the window shows now, with its corners and border.
    /// A plain copy, so every frame is fast however many icons the shelf holds.
    /// </summary>
    void PaintFrame(Graphics g, Box box)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var shape = ShapePath(box, 0);
        g.SetClip(shape);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(openFrame!, new Rectangle(0, 0, box.W, box.H), box.X - open.X, box.Y - open.Y, box.W, box.H, GraphicsUnit.Pixel);
        g.CompositingMode = CompositingMode.SourceOver;
        g.ResetClip();
        DrawBorder(g, box);
    }

    /// <summary>Corners against the screen edge a shelf is docked to stay square, so it sits flush.</summary>
    (bool tl, bool tr, bool br, bool bl) Corners => Model.Edge switch
    {
        Edge.Top => (false, false, true, true),
        Edge.Bottom => (true, true, false, false),
        Edge.Left => (false, true, true, false),
        Edge.Right => (true, false, false, true),
        _ => (true, true, true, true),
    };

    float Radius(Box box) => Math.Min(app.Settings.CornerRadius * S, Math.Min(box.W, box.H) / 2f);

    GraphicsPath ShapePath(Box box, float inset)
    {
        var (tl, tr, br, bl) = Corners;
        return Art.RoundRect(new RectangleF(inset, inset, box.W - inset * 2, box.H - inset * 2), Math.Max(0, Radius(box) - inset), tl, tr, br, bl);
    }

    /// <summary>The 1 px border, left off the side that touches the screen edge.</summary>
    void DrawBorder(Graphics g, Box box)
    {
        using var border = new Pen(Color.FromArgb(Th.Dark ? 30 : 28, Th.Dark ? Color.White : Color.Black), 1);
        using var path = ShapePath(box, 0.5f);
        var state = g.Save();
        // Pixel centres at half coordinates: a 1 px pen on a path inset by half a pixel covers exactly one pixel
        // row, instead of smearing over two (which read as a soft rim, like a shadow).
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var keep = Model.Edge switch
        {
            Edge.Top => new Rectangle(0, 1, box.W, box.H),
            Edge.Bottom => new Rectangle(0, 0, box.W, box.H - 1),
            Edge.Left => new Rectangle(1, 0, box.W, box.H),
            Edge.Right => new Rectangle(0, 0, box.W - 1, box.H),
            _ => new Rectangle(0, 0, box.W, box.H),
        };
        g.SetClip(keep);
        g.DrawPath(border, path);
        g.Restore(state);
    }

    /// <summary>The shelf's colour: its own, the setting for all shelves, or the theme's.</summary>
    Color TintColor => Colors.Parse(Model.Tint) is { } c ? Color.FromArgb(c.R, c.G, c.B)
                     : Colors.Parse(app.Settings.Tint) is { } d ? Color.FromArgb(d.R, d.G, d.B)
                     : Th.Window;

    int OpacityPercent => Model.Opacity ?? app.Settings.Opacity;

    /// <summary>Text that reads on the shelf: dark on a light colour, light on a dark one (the theme's when see-through).</summary>
    Color TextColor
    {
        get
        {
            var t = TintColor;
            if (OpacityPercent < 35 || t == Th.Window) return Th.Text;
            return Colors.IsLight(t.R, t.G, t.B) ? Color.FromArgb(25, 25, 25) : Color.FromArgb(240, 240, 240);
        }
    }

    Color DimColor => Color.FromArgb(170, TextColor);

    void PaintShelf(Graphics g, Box box)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        // Pixel edges on whole coordinates: a shape from 0 fills row 0 fully (no half-lit row at the screen edge).
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float s = S;
        var size = new Size(box.W, box.H);
        var r = new RectangleF(0, 0, box.W, box.H);
        using var shape = ShapePath(box, 0);

        // Glass: the blurred wallpaper behind the shelf under the shelf's colour, as solid as its opacity says.
        g.SetClip(shape);
        int alpha = (int)Math.Round(OpacityPercent / 100.0 * 255);
        if (alpha < 255 && app.Wallpaper.Blurred(Screen.FromRectangle(ToRect(box)).Bounds) is { } wall)
        {
            var mon = Screen.FromRectangle(ToRect(box)).Bounds;
            float k = wall.Width / (float)mon.Width;
            var src = new RectangleF((box.X - mon.X) * k, (box.Y - mon.Y) * k, box.W * k, box.H * k);
            g.DrawImage(wall, new RectangleF(0, 0, box.W, box.H), src, GraphicsUnit.Pixel);
        }
        else alpha = 255;
        using (var tint = new SolidBrush(Color.FromArgb(alpha, TintColor)))
            g.FillRectangle(tint, r);
        g.PixelOffsetMode = PixelOffsetMode.Default;

        // Title.
        var title = TitleRect(size);
        int btn = (int)(24 * s);
        portalButton = Model.Portal != null && !SideTab
            ? new Rectangle(L.Ar ? (int)(6 * s) : box.W - btn - (int)(6 * s), title.Y + (title.Height - btn) / 2, btn, btn)
            : null;
        string text = Model.Title.Length > 0 ? Model.Title : L.T("DefaultTitle");
        using var tf = new Font("Segoe UI Semibold", 10f * s, GraphicsUnit.Pixel);
        using var titleFont = new Font("Segoe UI Semibold", 13f * s, GraphicsUnit.Pixel);
        if (SideTab)
            DrawSideTitle(g, text, titleFont, title);
        else
        {
            var tr = Rectangle.Inflate(title, -(int)(12 * s), 0);
            var align = app.Settings.TitleAlign switch
            {
                TitleAlign.Left => L.Ar ? StringAlignment.Far : StringAlignment.Near,
                TitleAlign.Right => L.Ar ? StringAlignment.Near : StringAlignment.Far,
                _ => StringAlignment.Center,
            };
            using var sf = new StringFormat(StringFormatFlags.NoWrap) { Alignment = align, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
            if (L.Ar) sf.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
            using var tb = new SolidBrush(TextColor);
            g.DrawString(text, titleFont, tb, tr, sf);
            if (portalButton is { } pb && IsOpen) Art.GlyphCentered(g, Art.IconOpen, 13 * s, pb, hotPortal ? Th.Accent : DimColor);

            // A faint line between title and icons while open.
            if (shown > 0.05)
            {
                int ly = Model.Edge == Edge.Bottom ? title.Top : title.Bottom - 1;
                using var lp = new Pen(Color.FromArgb(Th.Dark ? 36 : 30, TextColor), 1);
                g.DrawLine(lp, 0, ly, box.W, ly);
            }
        }

        // Tabs, then icons, clipped to the visible part of the shelf.
        if (shown > 0.05 && !SideTab || IsOpen)
        {
            PaintTabs(g, box);
            PaintItems(g, box, s);
        }

        g.ResetClip();
        DrawBorder(g, box);

        if (dropHover)
        {
            using var dp = new Pen(Color.FromArgb(220, Th.Accent), 2 * s);
            using var dr = ShapePath(box, 1);
            g.DrawPath(dp, dr);
        }
    }

    void DrawSideTitle(Graphics g, string text, Font f, Rectangle tab)
    {
        // Right edge reads bottom to top, left edge top to bottom: the tops of the letters face the screen's edge.
        var state = g.Save();
        bool left = Model.Edge == Edge.Left;
        g.TranslateTransform(tab.Width / 2f, tab.Height / 2f);
        g.RotateTransform(left ? 90 : -90);
        var rr = new RectangleF(-tab.Height / 2f + 10 * S, -tab.Width / 2f, tab.Height - 20 * S, tab.Width);
        using var sf = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        if (L.Ar) sf.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
        using var b = new SolidBrush(TextColor);
        g.DrawString(text, f, b, rr, sf);
        g.Restore(state);
    }

    void PaintItems(Graphics g, Box box, float s)
    {
        var shift = ContentShift(box);
        var content = ContentRect;
        content.Offset(shift.X, shift.Y);
        var title = TitleRect(new Size(box.W, box.H));
        var clip = Rectangle.Intersect(content, new Rectangle(0, 0, box.W, box.H));
        clip = Model.Edge == Edge.Bottom
            ? Rectangle.FromLTRB(clip.Left, clip.Top, clip.Right, Math.Min(clip.Bottom, title.Top))
            : Rectangle.FromLTRB(clip.Left, Math.Max(clip.Top, title.Bottom), clip.Right, clip.Bottom);
        if (clip.Width <= 0 || clip.Height <= 0) return;
        var oldClip = g.Clip;
        g.SetClip(clip, CombineMode.Intersect);

        int px = IconPx;
        using var lf = new Font("Segoe UI", 12f * s, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(TextColor);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisWord };
        if (L.Ar) sf.FormatFlags |= StringFormatFlags.DirectionRightToLeft;

        if (items.Count == 0)
        {
            using var eb = new SolidBrush(DimColor);
            using var ef = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            string msg = Model.Portal == null ? L.T("Empty") : Directory.Exists(Model.Portal) ? L.T("EmptyPortal") : L.T("PortalMissing");
            g.DrawString(msg, lf, eb, content, ef);
        }

        foreach (var it in items)
        {
            var cell = it.Cell;
            cell.Offset(shift.X, shift.Y - scroll);
            if (!cell.IntersectsWith(clip)) continue;
            bool sel = selected.Contains(it.Name), over = hoverItem == it;
            if (sel || over)
            {
                var hc = sel ? Color.FromArgb(Th.Dark ? 70 : 50, Th.Accent) : Color.FromArgb(Th.Dark ? 28 : 20, TextColor);
                Art.FillRound(g, Rectangle.Inflate(cell, -(int)(2 * s), -(int)(1 * s)), 5 * s, hc);
            }
            var icon = IconCache.Get(it.Name, px);
            var ir = new Rectangle(cell.X + (cell.Width - px) / 2, cell.Y + (int)(6 * s), px, px);
            if (icon != null)
            {
                // Thumbnails keep their aspect ratio inside the icon square.
                float k = Math.Min(px / (float)icon.Width, px / (float)icon.Height);
                var dr = new RectangleF(ir.X + (px - icon.Width * k) / 2, ir.Y + (px - icon.Height * k) / 2, icon.Width * k, icon.Height * k);
                g.DrawImage(icon, dr);
            }
            var lr = new RectangleF(cell.X + 2 * s, ir.Bottom + 4 * s, cell.Width - 4 * s, LabelH);
            g.DrawString(it.Label, lf, textBrush, lr, sf);
        }

        if (drag == DragKind.Marquee)
        {
            var mr = MarqueeRect;
            mr.Offset(shift.X, shift.Y - scroll);
            using var mb = new SolidBrush(Color.FromArgb(40, Th.Accent));
            using var mp = new Pen(Color.FromArgb(160, Th.Accent), 1);
            g.FillRectangle(mb, mr);
            g.DrawRectangle(mp, mr);
        }

        // Scrollbar when the icons do not fit.
        int contentH = ContentHeight;
        if (contentH > ContentRect.Height && IsOpen)
        {
            float visible = ContentRect.Height, total = contentH;
            float barH = Math.Max(24 * s, visible * visible / total);
            float barY = content.Top + (visible - barH) * (scroll / Math.Max(1f, total - visible));
            float barX = L.Ar ? content.Left - Pad / 2f : content.Right + Pad / 2f - 3 * s;
            Art.FillRound(g, new RectangleF(barX, barY, 3 * s, barH), 1.5f * s, Color.FromArgb(Th.Dark ? 90 : 80, TextColor));
        }
        g.Clip = oldClip;
    }
}
