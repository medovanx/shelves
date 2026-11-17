namespace Shelves.UI.Controls;

/// <summary>
/// A flat button that paints its own text. A stock disabled button ignores ForeColor and draws
/// its text in the system grey, which looks black on the dark theme; this one fades to the theme instead.
/// </summary>
sealed class ThemedButton : Button
{
    const int SpinSize = 14, SpinGap = 8;
    readonly System.Windows.Forms.Timer spin = new() { Interval = 30 };
    bool hover, down, busy;
    int angle;

    public ThemedButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat;
        spin.Tick += (_, _) => { angle = (angle + 12) % 360; Invalidate(); };
    }

    /// <summary>Shows a spinner beside the text and blocks clicks while work runs.</summary>
    public bool Busy
    {
        get => busy;
        set
        {
            if (busy == value) return;
            busy = value;
            Enabled = !value;
            if (value) { angle = 0; spin.Start(); } else spin.Stop();
            // Extra padding makes AutoSize room for the spinner; the paint centres everything itself.
            Padding = value ? new Padding(Padding.Left + SpinSize + SpinGap, Padding.Top, Padding.Right, Padding.Bottom)
                            : new Padding(Padding.Left - SpinSize - SpinGap, Padding.Top, Padding.Right, Padding.Bottom);
            Invalidate();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) spin.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var fa = FlatAppearance;
        Color bg = !Enabled ? BackColor
            : down && !fa.MouseDownBackColor.IsEmpty ? fa.MouseDownBackColor
            : hover && !fa.MouseOverBackColor.IsEmpty ? fa.MouseOverBackColor
            : BackColor;
        g.Clear(bg);
        if (fa.BorderSize > 0)
        {
            using var pen = new Pen(fa.BorderColor.IsEmpty ? ForeColor : fa.BorderColor, fa.BorderSize);
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
        // Disabled: the text colour pulled most of the way to the background. Busy keeps full colour.
        Color fg = Enabled || busy ? ForeColor : Blend(ForeColor, bg, 0.55f);
        bool rtl = RightToLeft == RightToLeft.Yes;
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                    | TextFormatFlags.EndEllipsis | (rtl ? TextFormatFlags.RightToLeft : 0);
        var textRect = ClientRectangle;
        if (busy)
        {
            int textW = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 2;
            int total = SpinSize + SpinGap + textW;
            int left = (Width - total) / 2;
            int spinX = rtl ? left + textW + SpinGap : left;
            textRect = new Rectangle(rtl ? left : left + SpinSize + SpinGap, 0, textW, Height);
            flags = (flags & ~TextFormatFlags.EndEllipsis) | TextFormatFlags.NoPadding;
            var old = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var arc = new Rectangle(spinX, (Height - SpinSize) / 2, SpinSize, SpinSize);
            using (var track = new Pen(Blend(fg, bg, 0.75f), 2f)) g.DrawEllipse(track, arc);
            using (var pen = new Pen(fg, 2f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                g.DrawArc(pen, arc, angle, 90);
            g.SmoothingMode = old;
        }
        TextRenderer.DrawText(g, Text, Font, textRect, fg, flags);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -3, -3), fg, bg);
    }

    static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
}
