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
            ? new Rectangle(Pad, Pad, open.W - Pad * 2, open.H - TitleH - Pad * 2)
            : new Rectangle(Pad, TitleH + Pad / 2, open.W - Pad * 2, open.H - TitleH - Pad - Pad / 2);

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

    void Render()
    {
        var box = Target();
        if (box.W <= 0 || box.H <= 0) return;
        using var bmp = new Bitmap(box.W, box.H, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
            PaintShelf(g, box);
        Native.PushLayered(Handle, bmp, box.X, box.Y);
    }

    void PaintShelf(Graphics g, Box box)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        float s = S;
        var size = new Size(box.W, box.H);
        float rad = Math.Min(app.Settings.CornerRadius * s, Math.Min(box.W, box.H) / 2f);
        var r = new RectangleF(0, 0, box.W, box.H);
        using var shape = Art.RoundRect(r, rad);

        // Glass: the blurred wallpaper behind the shelf, tinted with the theme. Solid when transparency is off.
        g.SetClip(shape);
        bool glass = Native.TransparencyOn();
        if (glass && app.Wallpaper.Blurred(Screen.FromRectangle(ToRect(box)).Bounds) is { } wall)
        {
            var mon = Screen.FromRectangle(ToRect(box)).Bounds;
            float k = wall.Width / (float)mon.Width;
            var src = new RectangleF((box.X - mon.X) * k, (box.Y - mon.Y) * k, box.W * k, box.H * k);
            g.DrawImage(wall, new RectangleF(0, 0, box.W, box.H), src, GraphicsUnit.Pixel);
            int a = (int)(app.Settings.Opacity / 100.0 * 230);
            using var tint = new SolidBrush(Color.FromArgb(a, Th.Window));
            g.FillRectangle(tint, r);
        }
        else
        {
            using var solid = new SolidBrush(Th.Surface);
            g.FillRectangle(solid, r);
        }

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
            using var tb = new SolidBrush(Th.Text);
            g.DrawString(text, titleFont, tb, tr, sf);
            if (portalButton is { } pb && IsOpen) Art.GlyphCentered(g, Art.IconOpen, 13 * s, pb, hotPortal ? Th.Accent : Th.Dim);

            // A line between title and icons while open, in the accent colour while the mouse is over the shelf.
            if (shown > 0.05)
            {
                int ly = Model.Edge == Edge.Bottom ? title.Top : title.Bottom - 1;
                var lc = hot ? Color.FromArgb(200, Th.Accent) : Color.FromArgb(Th.Dark ? 36 : 30, Th.Text);
                using var lp = new Pen(lc, hot ? 2 * s : 1);
                float inset = hot ? 12 * s : 0;
                g.DrawLine(lp, inset, ly, box.W - inset, ly);
            }
        }

        // Icons, clipped to the visible part of the content.
        if (shown > 0.05 && !SideTab || IsOpen)
            PaintItems(g, box, s);

        g.ResetClip();
        // Border.
        using var border = new Pen(Color.FromArgb(Th.Dark ? 48 : 40, Th.Dark ? Color.White : Color.Black), 1);
        using var inner = Art.RoundRect(new RectangleF(0.5f, 0.5f, box.W - 1, box.H - 1), Math.Max(0, rad - 0.5f));
        g.DrawPath(border, inner);

        if (dropHover)
        {
            using var dp = new Pen(Color.FromArgb(220, Th.Accent), 2 * s);
            using var dr = Art.RoundRect(new RectangleF(1, 1, box.W - 2, box.H - 2), Math.Max(0, rad - 1));
            g.DrawPath(dp, dr);
        }
    }

    void DrawSideTitle(Graphics g, string text, Font f, Rectangle tab)
    {
        // Left edge reads bottom to top, right edge top to bottom, both facing the screen's middle.
        var state = g.Save();
        bool left = Model.Edge == Edge.Left;
        g.TranslateTransform(tab.Width / 2f, tab.Height / 2f);
        g.RotateTransform(left ? -90 : 90);
        var rr = new RectangleF(-tab.Height / 2f + 10 * S, -tab.Width / 2f, tab.Height - 20 * S, tab.Width);
        using var sf = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        if (L.Ar) sf.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
        using var b = new SolidBrush(Th.Text);
        g.DrawString(text, f, b, rr, sf);
        g.Restore(state);
        if (hot)
        {
            using var p = new Pen(Color.FromArgb(200, Th.Accent), 2 * S);
            float x = left ? tab.Right - 1.5f * S : 1.5f * S;
            g.DrawLine(p, x, 10 * S, x, tab.Height - 10 * S);
        }
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
        using var textBrush = new SolidBrush(Th.Text);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisWord };
        if (L.Ar) sf.FormatFlags |= StringFormatFlags.DirectionRightToLeft;

        if (items.Count == 0)
        {
            using var eb = new SolidBrush(Th.Dim);
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
                var hc = sel ? Color.FromArgb(Th.Dark ? 70 : 50, Th.Accent) : Color.FromArgb(Th.Dark ? 28 : 20, Th.Text);
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
            Art.FillRound(g, new RectangleF(barX, barY, 3 * s, barH), 1.5f * s, Color.FromArgb(Th.Dark ? 90 : 80, Th.Text));
        }
        g.Clip = oldClip;
    }
}
