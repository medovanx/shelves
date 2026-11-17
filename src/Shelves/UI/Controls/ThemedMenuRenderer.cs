using System.Drawing.Drawing2D;

namespace Shelves.UI.Controls;

/// <summary>
/// Menus in the app's own style, like the hover card and the flyout's popups: the theme surface, rounded
/// hover rows inset from the edges, an accent check, dim separators and submenu arrows, and an icon-font
/// glyph beside an item whose <see cref="ToolStripItem.Tag"/> is a <see cref="MenuGlyph"/>.
/// Call <see cref="Style"/> on a menu to also round its corners and give it roomier rows.
/// </summary>
sealed class ThemedMenuRenderer(Theme t) : ToolStripRenderer
{
    /// <summary>Put in an item's Tag to draw this icon-font glyph in the image margin.</summary>
    public sealed record MenuGlyph(string Glyph, Color? Color = null);

    static readonly Color Danger = Color.FromArgb(232, 72, 85);
    readonly Dictionary<ToolStrip, Rectangle> margins = [];

    Color Hover => Color.FromArgb(t.Dark ? 40 : 24, t.Accent);

    /// <summary>Rounds the menu and every submenu, gives the rows room, and themes submenus too.</summary>
    public static void Style(ToolStripDropDown menu, Theme theme)
    {
        var r = new ThemedMenuRenderer(theme);
        float s = menu.DeviceDpi / 96f;
        void Apply(ToolStripDropDown d)
        {
            d.Renderer = r;
            d.Padding = new Padding(0, (int)(4 * s), 0, (int)(4 * s));
            d.Font = new Font("Segoe UI", 9.5f);
            d.BackColor = theme.Surface;
            if (d is ToolStripDropDownMenu dm) dm.ImageScalingSize = new Size((int)(16 * s), (int)(16 * s));
            d.Opened += (_, _) => Native.RoundCorners(d.Handle, theme.Dark);
            foreach (ToolStripItem it in d.Items)
            {
                if (it is ToolStripSeparator) continue;
                it.Padding = new Padding(0, (int)(4 * s), 0, (int)(4 * s));
                if (it is ToolStripMenuItem { HasDropDownItems: true } mi) Apply(mi.DropDown);
            }
        }
        Apply(menu);
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var b = new SolidBrush(t.Surface);
        e.Graphics.FillRectangle(b, e.AffectedBounds);
    }

    // Windows draws the rounded border (RoundCorners); a square one would poke out of the corners.
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) => margins[e.ToolStrip] = e.AffectedBounds;

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var it = e.Item;
        float s = it.Owner?.DeviceDpi / 96f ?? 1f;
        if (it.Selected && it.Enabled)
        {
            var r = new RectangleF(4 * s, 1 * s, it.Width - 8 * s, it.Height - 2 * s);
            bool danger = it.ForeColor.ToArgb() == Danger.ToArgb();
            Art.FillRound(g, r, 5 * s, Color.FromArgb(t.Dark ? 40 : 24, danger ? Danger : t.Accent));
        }
    }

    /// <summary>Item images (the app icon in the header) in full colour, even on a disabled item.</summary>
    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Image == null) return;
        var r = e.ImageRectangle;
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        // Same 2px inset as the icon-font glyphs, so the column lines up.
        int inset = (int)Math.Round(2 * e.Item.Owner!.DeviceDpi / 96f) * (e.Item.Owner.RightToLeft == RightToLeft.Yes ? -1 : 1);
        g.DrawImage(e.Image, new Rectangle(r.X + inset, (e.Item.Height - r.Height) / 2, r.Width, r.Height));
    }

    // The check is drawn with the text (DrawMarginGlyph), so it lines up with it.
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }

    /// <summary>
    /// The icon or check in the image margin, centred on the text's own rectangle rather than the row:
    /// Windows places menu text above the row's middle, so a row-centred icon sat low beside it.
    /// </summary>
    void DrawMarginGlyph(ToolStripItemTextRenderEventArgs e)
    {
        var it = e.Item;
        if (it.Owner is not { } owner || !margins.TryGetValue(owner, out var m) || m.Width <= 0) return;
        bool icon = it.Tag is MenuGlyph;
        string? glyph = it.Tag is MenuGlyph mg ? mg.Glyph : it is ToolStripMenuItem { Checked: true } ? "\uE73E" : null;
        if (glyph == null) return;
        float s = owner.DeviceDpi / 96f;
        bool rtl = owner.RightToLeft == RightToLeft.Yes;
        var line = e.TextRectangle;
        var box = new RectangleF(rtl ? it.Width - m.Width : 0, line.Top, m.Width, line.Height);
        box.Offset(rtl ? -2 * s : 2 * s, 0);
        Color c = it.Tag is MenuGlyph g ? (!it.Enabled ? Color.FromArgb(120, t.Dim) : g.Color ?? t.Dim) : t.Accent;
        Art.GlyphCentered(e.Graphics, glyph, (icon ? 14 : 11) * s, box, c);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var it = e.Item;
        // Windows lays the text out about 3.5px above the row's middle, which leaves the separators closer
        // to the row below than the one above and the hover highlight off-centre. Centre it in the row.
        var tr = e.TextRectangle;
        e.TextRectangle = new Rectangle(tr.X, (it.Height - tr.Height) / 2, tr.Width, tr.Height);
        DrawMarginGlyph(e);
        bool danger = it.ForeColor.ToArgb() == Danger.ToArgb();
        e.TextColor = !it.Enabled ? t.Dim : danger ? Danger : it is ToolStripMenuItem { Checked: true } ? t.Accent : t.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        float s = e.Item.Owner?.DeviceDpi / 96f ?? 1f;
        int y = e.Item.Height / 2;
        using var pen = new Pen(t.Border, Math.Max(1, s));
        e.Graphics.DrawLine(pen, 12 * s, y, e.Item.Width - 12 * s, y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = t.Dim;
        base.OnRenderArrow(e);
    }
}
