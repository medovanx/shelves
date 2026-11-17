namespace Shelves.Core.Geometry;

/// <summary>Which screen edge a shelf is docked to.</summary>
public enum Edge { None, Left, Top, Right, Bottom }

/// <summary>The maths behind docking, rolling up, snapping and multi-monitor remapping.</summary>
public static class ShelfGeometry
{
    /// <summary>True for the edges where a shelf rolls into a vertical tab.</summary>
    public static bool IsSide(Edge e) => e is Edge.Left or Edge.Right;

    /// <summary>
    /// The rectangle a rolled-up shelf occupies. Top, bottom and floating shelves keep only their title bar
    /// (a bottom shelf keeps it at the bottom, so it opens upward); side shelves shrink to a tab against the edge.
    /// </summary>
    public static Box Rolled(Box open, Edge edge, int titleH, int tabW) => edge switch
    {
        Edge.Bottom => new(open.X, open.Bottom - titleH, open.W, titleH),
        Edge.Left => new(open.X, open.Y, tabW, open.H),
        Edge.Right => new(open.Right - tabW, open.Y, tabW, open.H),
        _ => new(open.X, open.Y, open.W, titleH),
    };

    /// <summary>The rectangle between <paramref name="rolled"/> and <paramref name="open"/> at progress t (0 rolled, 1 open).</summary>
    public static Box Lerp(Box rolled, Box open, double t)
    {
        t = Math.Clamp(t, 0, 1);
        int L(int a, int b) => (int)Math.Round(a + (b - a) * t);
        return Box.FromLTRB(L(rolled.X, open.X), L(rolled.Y, open.Y), L(rolled.Right, open.Right), L(rolled.Bottom, open.Bottom));
    }

    /// <summary>The edge of <paramref name="work"/> that <paramref name="b"/> touches (within <paramref name="dist"/>), or None.</summary>
    public static Edge NearEdge(Box b, Box work, int dist)
    {
        // The closest edge wins when a shelf sits in a corner.
        var cands = new (Edge e, int d)[]
        {
            (Edge.Left, Math.Abs(b.X - work.X)),
            (Edge.Top, Math.Abs(b.Y - work.Y)),
            (Edge.Right, Math.Abs(work.Right - b.Right)),
            (Edge.Bottom, Math.Abs(work.Bottom - b.Bottom)),
        };
        var best = cands.MinBy(c => c.d);
        return best.d <= dist ? best.e : Edge.None;
    }

    /// <summary>Puts the shelf flush against <paramref name="edge"/>, keeping its size and its place along the edge.</summary>
    public static Box DockTo(Box b, Box work, Edge edge)
    {
        b = b.ClampInto(work);
        return edge switch
        {
            Edge.Left => b with { X = work.X },
            Edge.Right => b with { X = work.Right - b.W },
            Edge.Top => b with { Y = work.Y },
            Edge.Bottom => b with { Y = work.Bottom - b.H },
            _ => b,
        };
    }

    /// <summary>
    /// Snaps a box being moved to the work area's edges and to other shelves' edges within <paramref name="dist"/>.
    /// Only the position changes.
    /// </summary>
    public static Box Snap(Box b, Box work, IEnumerable<Box> others, int dist)
    {
        var xs = new List<int> { work.X, work.Right - b.W };
        var ys = new List<int> { work.Y, work.Bottom - b.H };
        foreach (var o in others)
        {
            // Beside another shelf (its left or right edge), or lined up with it.
            xs.AddRange([o.Right, o.X - b.W, o.X, o.Right - b.W]);
            ys.AddRange([o.Bottom, o.Y - b.H, o.Y, o.Bottom - b.H]);
        }
        int x = Nearest(b.X, xs, dist), y = Nearest(b.Y, ys, dist);
        return b with { X = x, Y = y };
    }

    static int Nearest(int v, List<int> cands, int dist)
    {
        int best = v, bestD = dist + 1;
        foreach (var c in cands)
        {
            int d = Math.Abs(c - v);
            if (d < bestD) { best = c; bestD = d; }
        }
        return best;
    }

    /// <summary>
    /// Docked shelves on the same edge sit side by side: moves <paramref name="b"/> along its edge, to the nearest
    /// free gap, until it overlaps none of <paramref name="sameEdge"/>. Returns it unchanged if no gap fits.
    /// </summary>
    public static Box Unstack(Box b, Edge edge, Box work, IReadOnlyList<Box> sameEdge, int gap = 0)
    {
        if (edge == Edge.None || !sameEdge.Any(o => o.Intersects(b))) return b;
        bool vertical = IsSide(edge);
        int start = vertical ? work.Y : work.X, end = vertical ? work.Bottom : work.Right;
        int len = vertical ? b.H : b.W, pos = vertical ? b.Y : b.X;
        // Occupied spans along the edge, sorted.
        var spans = sameEdge.Select(o => vertical ? (o.Y - gap, o.Bottom + gap) : (o.X - gap, o.Right + gap)).OrderBy(s => s.Item1).ToList();
        int? best = null;
        int cursor = start;
        foreach (var (s, e) in spans.Append((end, end)))
        {
            // A free run from cursor to s.
            if (s - cursor >= len)
            {
                int cand = Math.Clamp(pos, cursor, s - len);
                if (best == null || Math.Abs(cand - pos) < Math.Abs(best.Value - pos)) best = cand;
            }
            cursor = Math.Max(cursor, e);
        }
        if (best == null) return b;
        return vertical ? b with { Y = best.Value } : b with { X = best.Value };
    }

    /// <summary>
    /// Moves each box (in order) down below any earlier box it overlaps, while it still fits in the work area,
    /// so imported fences never hide one another's icons.
    /// </summary>
    public static List<Box> Separate(IReadOnlyList<Box> boxes, Box work, int gap)
    {
        var res = new List<Box>();
        foreach (var b0 in boxes)
        {
            var b = b0;
            for (int guard = 0; guard < 20; guard++)
            {
                var hit = res.Where(o => o.Intersects(b)).OrderByDescending(o => o.Bottom).FirstOrDefault();
                if (hit.IsEmpty || hit.Bottom + gap + b.H > work.Bottom) break;
                b = b with { Y = hit.Bottom + gap };
            }
            res.Add(b);
        }
        return res;
    }

    public static RelBox ToRel(Box b, Box work) =>
        new((b.X - work.X) / (double)work.W, (b.Y - work.Y) / (double)work.H, b.W / (double)work.W, b.H / (double)work.H);

    /// <summary>Back to pixels on a (possibly different) work area, kept inside it.</summary>
    public static Box FromRel(RelBox r, Box work)
    {
        var b = new Box(work.X + (int)Math.Round(r.X * work.W), work.Y + (int)Math.Round(r.Y * work.H),
                        Math.Max(1, (int)Math.Round(r.W * work.W)), Math.Max(1, (int)Math.Round(r.H * work.H)));
        return b.ClampInto(work);
    }
}
