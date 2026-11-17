using Shelves.Core.Geometry;

namespace Shelves.Core.Tests;

public class GeometryTests
{
    static readonly Box Work = new(0, 0, 1920, 1040);
    static readonly Box Open = new(100, 200, 300, 400);

    [Theory]
    [InlineData(Edge.None, 100, 200, 300, 32)]
    [InlineData(Edge.Top, 100, 200, 300, 32)]
    [InlineData(Edge.Bottom, 100, 568, 300, 32)]
    [InlineData(Edge.Left, 100, 200, 28, 400)]
    [InlineData(Edge.Right, 372, 200, 28, 400)]
    public void Rolled_keeps_the_side_against_the_edge(Edge e, int x, int y, int w, int h) =>
        Assert.Equal(new Box(x, y, w, h), ShelfGeometry.Rolled(Open, e, 32, 28));

    [Fact]
    public void Lerp_runs_from_rolled_to_open()
    {
        var rolled = ShelfGeometry.Rolled(Open, Edge.Right, 32, 28);
        Assert.Equal(rolled, ShelfGeometry.Lerp(rolled, Open, 0));
        Assert.Equal(Open, ShelfGeometry.Lerp(rolled, Open, 1));
        var mid = ShelfGeometry.Lerp(rolled, Open, 0.5);
        Assert.Equal(Open.Right, mid.Right); // a right-edge tab grows leftward
    }

    [Theory]
    [InlineData(5, 300, Edge.Left)]
    [InlineData(1615, 300, Edge.Right)]
    [InlineData(800, 3, Edge.Top)]
    [InlineData(800, 636, Edge.Bottom)]
    [InlineData(800, 300, Edge.None)]
    public void NearEdge_finds_the_touching_edge(int x, int y, Edge expected) =>
        Assert.Equal(expected, ShelfGeometry.NearEdge(new Box(x, y, 300, 400), Work, 10));

    [Fact]
    public void DockTo_is_flush_and_keeps_position_along_the_edge()
    {
        Assert.Equal(new Box(1620, 200, 300, 400), ShelfGeometry.DockTo(Open, Work, Edge.Right));
        Assert.Equal(new Box(100, 640, 300, 400), ShelfGeometry.DockTo(Open, Work, Edge.Bottom));
        // Off the work area: pulled back in first.
        Assert.Equal(new Box(0, 0, 300, 400), ShelfGeometry.DockTo(new Box(-50, -80, 300, 400), Work, Edge.Left));
    }

    [Fact]
    public void Snap_pulls_to_work_area_and_neighbours()
    {
        Assert.Equal(new Box(0, 0, 300, 400), ShelfGeometry.Snap(new Box(7, 6, 300, 400), Work, [], 10));
        var other = new Box(500, 100, 200, 200);
        // Dropped just right of the other shelf: sits flush beside it, top aligned.
        Assert.Equal(new Box(700, 100, 300, 400), ShelfGeometry.Snap(new Box(706, 95, 300, 400), Work, [other], 10));
        Assert.Equal(new Box(900, 500, 300, 400), ShelfGeometry.Snap(new Box(900, 500, 300, 400), Work, [other], 10));
    }

    [Fact]
    public void Unstack_slides_along_the_edge_to_a_free_gap()
    {
        var a = new Box(0, 0, 300, 400);
        var moving = new Box(0, 300, 300, 300); // overlaps a on the left edge
        var r = ShelfGeometry.Unstack(moving, Edge.Left, Work, [a]);
        Assert.Equal(400, r.Y);
        Assert.False(r.Intersects(a));
        // Top edge: slides horizontally.
        var t = ShelfGeometry.Unstack(new Box(250, 0, 300, 200), Edge.Top, Work, [new Box(0, 0, 400, 200)]);
        Assert.Equal(400, t.X);
    }

    [Fact]
    public void Unstack_leaves_it_when_nothing_fits()
    {
        var full = new Box(0, 0, 300, 1040);
        var b = new Box(0, 100, 300, 300);
        Assert.Equal(b, ShelfGeometry.Unstack(b, Edge.Left, Work, [full]));
    }

    [Fact]
    public void Relative_boxes_survive_a_resolution_change()
    {
        var rel = ShelfGeometry.ToRel(new Box(960, 520, 480, 260), Work);
        var big = new Box(0, 0, 3840, 2080);
        Assert.Equal(new Box(1920, 1040, 960, 520), ShelfGeometry.FromRel(rel, big));
        // A monitor to the left of the primary (negative origin).
        var left = new Box(-1280, 0, 1280, 1024);
        var b = ShelfGeometry.FromRel(rel, left);
        Assert.True(b.X >= -1280 && b.Right <= 0);
    }

    [Fact]
    public void FromRel_keeps_the_shelf_on_screen() =>
        Assert.Equal(new Box(1620, 0, 300, 400), ShelfGeometry.FromRel(new RelBox(0.95, -0.1, 300 / 1920.0, 400 / 1040.0), Work));

    [Fact]
    public void Separate_moves_an_overlapping_box_below()
    {
        var games = new Box(1238, 0, 672, 580);
        var lower = new Box(1622, 483, 288, 290);
        var r = ShelfGeometry.Separate([games, lower], Work, 6);
        Assert.Equal(games, r[0]);
        Assert.Equal(new Box(1622, 586, 288, 290), r[1]);
        // No room below: left where it was.
        var tall = new Box(1622, 483, 288, 500);
        Assert.Equal(tall, ShelfGeometry.Separate([games, tall], Work, 6)[1]);
    }
}
