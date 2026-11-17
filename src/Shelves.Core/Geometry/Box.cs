namespace Shelves.Core.Geometry;

/// <summary>A rectangle in physical screen pixels (or any integer space), free of WinForms.</summary>
public readonly record struct Box(int X, int Y, int W, int H)
{
    public int Right => X + W;
    public int Bottom => Y + H;
    public int CenterX => X + W / 2;
    public int CenterY => Y + H / 2;
    public bool IsEmpty => W <= 0 || H <= 0;

    public static Box FromLTRB(int l, int t, int r, int b) => new(l, t, r - l, b - t);

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;
    public bool Intersects(Box o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;
    public Box Offset(int dx, int dy) => this with { X = X + dx, Y = Y + dy };

    /// <summary>Moves (never resizes, unless it is larger) the box so it lies inside <paramref name="area"/>.</summary>
    public Box ClampInto(Box area)
    {
        int w = Math.Min(W, area.W), h = Math.Min(H, area.H);
        int x = Math.Clamp(X, area.X, area.Right - w), y = Math.Clamp(Y, area.Y, area.Bottom - h);
        return new(x, y, w, h);
    }

    public long Overlap(Box o)
    {
        int w = Math.Min(Right, o.Right) - Math.Max(X, o.X), h = Math.Min(Bottom, o.Bottom) - Math.Max(Y, o.Y);
        return w > 0 && h > 0 ? (long)w * h : 0;
    }
}

/// <summary>A rectangle as fractions (0..1) of a monitor's work area, so it survives resolution and DPI changes.</summary>
public readonly record struct RelBox(double X, double Y, double W, double H);
