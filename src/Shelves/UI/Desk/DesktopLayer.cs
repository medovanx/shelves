using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Shelves.UI.Desk;

/// <summary>One desktop icon as the layer draws it.</summary>
sealed class LayerIcon(string name, int index, Pt pos, string label, Rectangle box)
{
    /// <summary>Parsing name: a full path, or ::{GUID} for items such as the Recycle Bin.</summary>
    public string Name { get; } = name;
    /// <summary>Index in Explorer's (hidden) desktop list, which keeps the item's position.</summary>
    public int Index { get; } = index;
    /// <summary>Position in Explorer's list-view coordinates (what Explorer saves).</summary>
    public Pt Pos { get; } = pos;
    public string Label { get; } = label;
    /// <summary>The icon and its label, in layer client coordinates.</summary>
    public Rectangle Box { get; } = box;
}

/// <summary>
/// The desktop's icons, drawn by Shelves over one monitor. Explorer's own desktop list is hidden while this runs:
/// it still holds the items and their saved positions, but it never draws, takes a click or handles a drop, so
/// nothing Windows does with dropped icons can shuffle them. The window sits right above the desktop and below
/// the shelves; every pixel is at least faintly drawn, so it takes every click on the desktop.
/// This part holds the window and the drawing; the mouse, keys and drops are in DesktopLayer.Input.cs.
/// </summary>
sealed partial class DesktopLayer : Form
{
    readonly ShelvesApp app;
    public Screen Screen { get; private set; }
    LayerSurface? surface;
    List<LayerIcon> icons = [];
    readonly HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);
    string? hoverName, focusName, dropName;

    public DesktopLayer(ShelvesApp app, Screen screen)
    {
        this.app = app;
        Screen = screen;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = screen.Bounds;
        AllowDrop = true;
        KeyPreview = true;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80 /* WS_EX_TOOLWINDOW */ | 0x80000 /* WS_EX_LAYERED */;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int disabled = 1;
        Native.DwmSetWindowAttribute(Handle, 2 /* DWMWA_NCRENDERING_POLICY */, ref disabled, sizeof(int));
        PlaceAboveDesktop();
        RenderAll();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { surface?.Dispose(); typeTimer.Dispose(); EndGhost(); }
        base.Dispose(disposing);
    }

    float S => IsHandleCreated ? Native.GetDpiForWindow(Handle) / 96f : DeviceDpi / 96f;

    /// <summary>The monitor moved or changed size.</summary>
    public void SetScreen(Screen s)
    {
        Screen = s;
        surface?.Dispose();
        surface = null;
        RenderAll();
    }

    // ---- Z-order: right above the desktop, below the shelves -----------------------------------------------------

    public void PlaceAboveDesktop()
    {
        if (!IsHandleCreated) return;
        var after = AboveDesktop();
        if (after == Handle) return;
        Native.SetWindowPos(Handle, after, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    /// <summary>The window to insert after so this one lands directly above Progman.</summary>
    IntPtr AboveDesktop()
    {
        var progman = Native.FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return Native.HWND_BOTTOM;
        var prev = GetWindow(progman, 3 /* GW_HWNDPREV */);
        return prev == IntPtr.Zero ? (IntPtr)0 : prev;
    }

    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_WINDOWPOSCHANGING)
        {
            var wp = Marshal.PtrToStructure<Native.WINDOWPOS>(m.LParam);
            // Keep the monitor's bounds, and never come forward over other windows when clicked.
            wp.x = Screen.Bounds.X; wp.y = Screen.Bounds.Y; wp.cx = Screen.Bounds.Width; wp.cy = Screen.Bounds.Height;
            if ((wp.flags & Native.SWP_NOZORDER) == 0)
            {
                var after = AboveDesktop();
                if (after == Handle) wp.flags |= Native.SWP_NOZORDER;
                else wp.hwndInsertAfter = after;
            }
            Marshal.StructureToPtr(wp, m.LParam, false);
        }
        else if (m.Msg == Native.WM_DPICHANGED)
        {
            IconCache.Forget();
            app.ReloadLayers();
            m.Result = IntPtr.Zero;
            return;
        }
        if (ShellOps.HandleMenuMessage(ref m)) return;
        base.WndProc(ref m);
    }

    // ---- Icons ---------------------------------------------------------------------------------------------------

    public IReadOnlyList<LayerIcon> Icons => icons;

    /// <summary>The icons on this monitor, from the app's last read of Explorer's list. Redraws what changed.</summary>
    public void SetIcons(List<LayerIcon> list)
    {
        var old = icons.ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
        icons = list;
        selected.IntersectWith(list.Select(i => i.Name));
        // Redraw only where an icon came, went, moved or was renamed.
        var now = list.ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var (n, o) in old)
            if (!now.TryGetValue(n, out var i) || i.Box != o.Box || i.Label != o.Label) { Invalidate(o); if (i != null) Invalidate(i); }
        foreach (var (n, i) in now)
            if (!old.ContainsKey(n)) Invalidate(i);
        Render();
    }

    LayerIcon? IconAt(Point client) =>
        // Last drawn is on top.
        icons.LastOrDefault(i => HitBox(i).Contains(client));

    /// <summary>The part of an icon's box that takes clicks: its icon and its label as drawn, like Explorer's.</summary>
    Rectangle HitBox(LayerIcon i)
    {
        var lr = LabelRect(i, Expanded(i));
        var ir = IconRect(i);
        return Rectangle.Union(new Rectangle(i.Box.X, ir.Y - (int)(2 * S), i.Box.Width, ir.Height + (int)(4 * S)), Rectangle.Round(lr));
    }

    public IEnumerable<string> SelectedNames => icons.Where(i => selected.Contains(i.Name)).Select(i => i.Name);

    public void ClearSelection()
    {
        if (selected.Count == 0) return;
        foreach (var n in selected) InvalidateName(n);
        selected.Clear();
        Render();
    }

    // ---- Drawing -------------------------------------------------------------------------------------------------

    int IconPx => app.DesktopIconPx;

    Rectangle IconRect(LayerIcon i)
    {
        int px = IconPx;
        int areaH = app.IconAreaHeight;
        return new Rectangle(i.Box.X + (i.Box.Width - px) / 2, i.Box.Y + (areaH - px) / 2, px, px);
    }

    Font LabelFont() => new("Segoe UI", 12f * S, GraphicsUnit.Pixel);

    /// <summary>The selected icon with focus shows its whole name, as on the desktop; the others two lines at most.</summary>
    bool Expanded(LayerIcon i) => selected.Contains(i.Name) && (focusName == null || string.Equals(focusName, i.Name, StringComparison.OrdinalIgnoreCase)) && selected.Count == 1;

    static readonly Bitmap measureBmp = new(1, 1);

    static readonly Graphics measure = Graphics.FromImage(measureBmp);
    readonly Dictionary<(string, int, float), (SizeF size, float line)> measured = [];

    RectangleF LabelRect(LayerIcon i, bool expanded)
    {
        float w = i.Box.Width - 4 * S;
        var key = (i.Label, (int)w, S);
        if (!measured.TryGetValue(key, out var m))
        {
            using var f = LabelFont();
            using var sf = LabelFormat();
            m = (measure.MeasureString(i.Label, f, (int)w, sf), f.GetHeight(measure));
            measured[key] = m;
        }
        var (size, line) = m;
        float h = expanded ? Math.Min(size.Height, line * 8) : Math.Min(size.Height, line * 2);
        float tw = Math.Min(w, size.Width + 2 * S);
        return new RectangleF(i.Box.X + (i.Box.Width - tw) / 2, i.Box.Y + app.IconAreaHeight + 1 * S, tw, h);
    }

    static StringFormat LabelFormat()
    {
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter };
        if (L.Ar) sf.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
        return sf;
    }

    /// <summary>Everything that shows for an icon (its highlight, icon and label), for redrawing just it.</summary>
    Rectangle DrawnArea(LayerIcon i)
    {
        var r = Rectangle.Union(i.Box, Rectangle.Ceiling(LabelRect(i, true)));
        r.Inflate((int)(4 * S), (int)(4 * S));
        return r;
    }

    Rectangle? dirty;

    void Invalidate(LayerIcon? i)
    {
        if (i == null) return;
        var r = DrawnArea(i);
        dirty = dirty is { } d ? Rectangle.Union(d, r) : r;
    }

    void InvalidateName(string? name)
    {
        if (name != null) Invalidate(icons.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)));
    }

    void InvalidateRect(Rectangle r) => dirty = dirty is { } d ? Rectangle.Union(d, r) : r;

    public void RenderAll()
    {
        dirty = new Rectangle(0, 0, Screen.Bounds.Width, Screen.Bounds.Height);
        Render();
    }

    /// <summary>Draws the changed part of the picture and shows it.</summary>
    void Render()
    {
        if (!IsHandleCreated || dirty is not { } area) return;
        dirty = null;
        renderWatch.Restart();
        var b = Screen.Bounds;
        if (surface == null || surface.Width != b.Width || surface.Height != b.Height)
        {
            surface?.Dispose();
            surface = new LayerSurface(b.Width, b.Height);
            area = new Rectangle(0, 0, b.Width, b.Height);
        }
        area.Intersect(new Rectangle(0, 0, b.Width, b.Height));
        if (area.Width <= 0 || area.Height <= 0) return;
        using (var g = Graphics.FromImage(surface.Bitmap))
        {
            g.SetClip(area);
            g.CompositingMode = CompositingMode.SourceCopy;
            // Faintly drawn everywhere (alpha 1 of 255, invisible): a layered window takes clicks only where it draws.
            using (var bg = new SolidBrush(Color.FromArgb(1, 0, 0, 0))) g.FillRectangle(bg, area);
            g.CompositingMode = CompositingMode.SourceOver;
            Paint(g, area);
        }
        surface.Push(Handle, b.Location, area);
        // SHELVES_DUMP_LAYER=folder: saves each monitor's picture after a full redraw, for checking the drawing.
        if (Environment.GetEnvironmentVariable("SHELVES_DUMP_LAYER") is { Length: > 0 } dump)
        {
            if (area.Width == b.Width && area.Height == b.Height)
                try { surface.Bitmap.Save(Path.Combine(dump, $"layer-{Screen.DeviceName.Trim('\\', '.')}.png")); } catch { }
            Log.Note($"[layer] {Screen.DeviceName} {icons.Count} icons, {area.Width}x{area.Height} drawn in {renderWatch.ElapsedMilliseconds} ms");
        }
    }

    readonly System.Diagnostics.Stopwatch renderWatch = new();

    void Paint(Graphics g, Rectangle area)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        float s = S;
        int px = IconPx;
        using var font = LabelFont();
        using var sf = LabelFormat();
        using var white = new SolidBrush(Color.White);
        using var shadowHard = new SolidBrush(Color.FromArgb(190, 0, 0, 0));
        using var shadowSoft = new SolidBrush(Color.FromArgb(80, 0, 0, 0));
        bool shadows = app.LabelShadows;
        var link = app.LinkOverlay(px);
        var cut = app.CutNames;

        foreach (var it in icons)
        {
            if (!DrawnArea(it).IntersectsWith(area)) continue;
            bool sel = selected.Contains(it.Name);
            bool over = string.Equals(hoverName, it.Name, StringComparison.OrdinalIgnoreCase);
            bool drop = string.Equals(dropName, it.Name, StringComparison.OrdinalIgnoreCase);
            bool expanded = Expanded(it);
            var lr = LabelRect(it, expanded);
            var ir = IconRect(it);
            if (sel || over || drop)
            {
                // Windows' desktop highlight: a light wash, stronger and outlined when selected.
                var hl = new Rectangle(it.Box.X, it.Box.Y, it.Box.Width, (int)Math.Ceiling(lr.Bottom - it.Box.Y + 3 * s));
                int fill = sel ? (over ? 110 : 90) : drop ? 90 : 45;
                using (var fb = new SolidBrush(Color.FromArgb(fill, 200, 225, 255))) g.FillRectangle(fb, hl);
                if (sel || drop)
                {
                    using var pen = new Pen(Color.FromArgb(140, 225, 240, 255), 1);
                    g.DrawRectangle(pen, hl.X, hl.Y, hl.Width - 1, hl.Height - 1);
                }
            }
            if (string.Equals(focusName, it.Name, StringComparison.OrdinalIgnoreCase) && !sel && focusShown)
            {
                using var pen = new Pen(Color.FromArgb(160, 255, 255, 255), 1) { DashStyle = DashStyle.Dot };
                g.DrawRectangle(pen, it.Box.X, it.Box.Y, it.Box.Width - 1, (int)(lr.Bottom - it.Box.Y + 2 * s));
            }

            var icon = IconCache.Get(it.Name, px);
            bool faded = cut.Contains(it.Name);
            if (icon != null)
            {
                float k = Math.Min(px / (float)icon.Width, px / (float)icon.Height);
                var dr = new RectangleF(ir.X + (px - icon.Width * k) / 2, ir.Y + (px - icon.Height * k) / 2, icon.Width * k, icon.Height * k);
                if (faded) DrawFaded(g, icon, dr);
                else g.DrawImage(icon, dr);
            }
            if (link != null && ShellOps.IsLink(it.Name)) g.DrawImage(link, ir);

            if (shadows)
            {
                g.DrawString(it.Label, font, shadowSoft, new RectangleF(lr.X + 1, lr.Y + 2, lr.Width, lr.Height), sf);
                g.DrawString(it.Label, font, shadowSoft, new RectangleF(lr.X + 2, lr.Y + 1, lr.Width, lr.Height), sf);
                g.DrawString(it.Label, font, shadowHard, new RectangleF(lr.X + 1, lr.Y + 1, lr.Width, lr.Height), sf);
            }
            g.DrawString(it.Label, font, white, lr, sf);
        }

        if (marquee is { } mr && mr.IntersectsWith(area))
        {
            using var mb = new SolidBrush(Color.FromArgb(70, 0, 120, 215));
            using var mp = new Pen(Color.FromArgb(200, 0, 120, 215), 1);
            g.FillRectangle(mb, mr);
            g.DrawRectangle(mp, mr.X, mr.Y, mr.Width - 1, mr.Height - 1);
        }
        if (newShelfBox is { } nb && nb.IntersectsWith(area))
        {
            var t = app.Theme;
            using var fill = new SolidBrush(Color.FromArgb(60, t.Accent));
            using var pen = new Pen(Color.FromArgb(220, t.Accent), 2 * s) { DashStyle = DashStyle.Dash };
            using var path = Art.RoundRect(new RectangleF(nb.X + 1, nb.Y + 1, nb.Width - 2, nb.Height - 2), 8 * s);
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }
    }

    /// <summary>
    /// The picture that follows the mouse while icons are dragged: each dragged icon on this monitor with its label,
    /// faded, laid out as they sit now, with the press point as the spot under the mouse.
    /// </summary>
    DragGhost? GhostOf(List<string> names, Point pressScreen)
    {
        var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var list = icons.Where(i => set.Contains(i.Name)).ToList();
        if (list.Count == 0) return null;
        var bounds = list.Select(DrawnArea).Aggregate(Rectangle.Union);
        bounds.Intersect(new Rectangle(0, 0, Screen.Bounds.Width, Screen.Bounds.Height));
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;
        var bmp = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var layer = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
        {
            using (var g = Graphics.FromImage(layer))
            {
                g.TranslateTransform(-bounds.X, -bounds.Y);
                var saved = icons;
                var savedHover = hoverName;
                icons = list;
                hoverName = null;
                try { Paint(g, new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height)); }
                finally { icons = saved; hoverName = savedHover; }
            }
            using var g2 = Graphics.FromImage(bmp);
            using var ia = new System.Drawing.Imaging.ImageAttributes();
            ia.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.75f });
            g2.DrawImage(layer, new Rectangle(0, 0, bounds.Width, bounds.Height), 0, 0, bounds.Width, bounds.Height, GraphicsUnit.Pixel, ia);
        }
        var press = ToClient(pressScreen);
        return new DragGhost(bmp, new Point(press.X - bounds.X, press.Y - bounds.Y));
    }

    static void DrawFaded(Graphics g, Bitmap icon, RectangleF dr)
    {
        using var ia = new System.Drawing.Imaging.ImageAttributes();
        ia.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.5f });
        g.DrawImage(icon, Rectangle.Round(dr), 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, ia);
    }
}
