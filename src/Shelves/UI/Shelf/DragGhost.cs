using System.Drawing.Imaging;

namespace Shelves.UI.Shelf;

/// <summary>
/// The icon that follows the mouse while icons are dragged out of a shelf by Shelves itself (to the desktop or
/// another shelf). Click-through, so the window under the mouse is always the real target.
/// </summary>
sealed class DragGhost : Form
{
    readonly Bitmap image;
    readonly Point hotspot;

    /// <summary>A ready picture, with the point of it that sits under the mouse.</summary>
    public DragGhost(Bitmap picture, Point hotspot)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        image = picture;
        this.hotspot = hotspot;
    }

    public DragGhost(IReadOnlyList<string> names, int px, float s)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        int pad = (int)(6 * s);
        int extra = Math.Min(names.Count - 1, 2) * (int)(6 * s);
        image = new Bitmap(px + pad * 2 + extra, px + pad * 2 + extra, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(image))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            // Up to three icons fanned out, the first on top, about two thirds opaque.
            using var fade = new ImageAttributes();
            fade.SetColorMatrix(new ColorMatrix { Matrix33 = 0.7f });
            for (int i = Math.Min(names.Count, 3) - 1; i >= 0; i--)
            {
                var icon = IconCache.Get(names[i], px);
                if (icon == null) continue;
                int off = i * (int)(6 * s);
                g.DrawImage(icon, new Rectangle(pad + off, pad + off, px, px), 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, fade);
            }
        }
        hotspot = new Point(px / 2 + pad, px / 2 + pad);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // Tool window, layered, click-through (so hit tests see what is under it), never activated.
            cp.ExStyle |= 0x80 | 0x80000 | 0x20 | 0x08000000;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    public void MoveTo(Point screen)
    {
        if (!Visible) Show();
        Native.PushLayered(Handle, image, screen.X - hotspot.X, screen.Y - hotspot.Y);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) image.Dispose();
        base.Dispose(disposing);
    }
}
