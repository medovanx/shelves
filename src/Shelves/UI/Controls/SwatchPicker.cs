using System.Drawing.Drawing2D;

namespace Shelves.UI.Controls;

/// <summary>
/// A row of colour circles: the theme's own colour first, then the presets, then a "+" that opens a colour dialog.
/// <see cref="Value"/> is "" for the theme's colour or "#RRGGBB".
/// </summary>
sealed class SwatchPicker : Control
{
    string value = "";
    int hover = -1;
    public Theme Theme { get; set; } = Theme.Current;
    public event EventHandler? ValueChanged;

    public SwatchPicker()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Size = new Size(Count * 26, 26);
    }

    public string Value
    {
        get => value;
        set { this.value = Colors.Normalize(value); Invalidate(); }
    }

    static int Count => Colors.Presets.Length + 2;
    float S => DeviceDpi / 96f;

    Rectangle Cell(int i)
    {
        int d = (int)(20 * S), step = (int)(26 * S);
        int x = RightToLeft == RightToLeft.Yes ? Width - (i + 1) * step + (step - d) / 2 : i * step + (step - d) / 2;
        return new Rectangle(x, (Height - d) / 2, d, d);
    }

    /// <summary>The colour of cell <paramref name="i"/>: null for the theme's own and for the "+" cell.</summary>
    static string? HexAt(int i) => i == 0 || i == Count - 1 ? null : Colors.Presets[i - 1].Hex;

    int Selected => value.Length == 0 ? 0 : Array.FindIndex(Colors.Presets, p => p.Hex == value) is var k && k >= 0 ? k + 1 : Count - 1;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = Enumerable.Range(0, Count).FirstOrDefault(i => Cell(i).Contains(e.Location), -1);
        if (h != hover) { hover = h; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        for (int i = 0; i < Count; i++)
        {
            if (!Cell(i).Contains(e.Location)) continue;
            if (i == Count - 1)
            {
                using var dlg = new ColorDialog { FullOpen = true, AnyColor = true };
                if (Colors.Parse(value) is { } c) dlg.Color = Color.FromArgb(c.R, c.G, c.B);
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                value = Colors.ToHex(dlg.Color.R, dlg.Color.G, dlg.Color.B);
            }
            else value = HexAt(i) ?? "";
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int sel = Selected;
        for (int i = 0; i < Count; i++)
        {
            var r = Cell(i);
            Color fill = i == 0 ? Theme.Window
                       : i == Count - 1 ? (Colors.Parse(value) is { } c && sel == Count - 1 ? Color.FromArgb(c.R, c.G, c.B) : Theme.Input)
                       : Colors.Parse(HexAt(i)) is { } p ? Color.FromArgb(p.R, p.G, p.B) : Theme.Window;
            using (var b = new SolidBrush(fill)) g.FillEllipse(b, r);
            using (var pen = new Pen(Color.FromArgb(90, Theme.Text), 1)) g.DrawEllipse(pen, r);
            if (i == 0)
            {
                // The theme's own colour: half dark, half light.
                using var half = new SolidBrush(Theme.Dark ? Color.FromArgb(243, 245, 244) : Color.FromArgb(30, 32, 32));
                g.FillPie(half, r, -45, 180);
            }
            if (i == Count - 1 && sel != Count - 1)
                Art.GlyphCentered(g, Art.IconAdd, 10 * S, r, Theme.Dim);
            if (i == sel || i == hover)
            {
                var ring = Rectangle.Inflate(r, (int)(3 * S), (int)(3 * S));
                using var pen = new Pen(i == sel ? Theme.Accent : Color.FromArgb(120, Theme.Text), i == sel ? 2 * S : 1);
                g.DrawEllipse(pen, ring);
            }
        }
    }
}
