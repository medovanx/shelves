using Shelves.Core.Model;

namespace Shelves.Core.Tests;

public class ModelTests
{
    [Fact]
    public void Shelve_moves_an_item_between_shelves()
    {
        var l = new Layout();
        var a = new ShelfModel { Title = "A" };
        var b = new ShelfModel { Title = "B" };
        l.Shelves.AddRange([a, b]);
        l.Shelve(@"C:\D\x.txt", a);
        l.Shelve(@"C:\D\y.txt", a);
        l.Shelve(@"c:\d\X.TXT", b, 0); // same file, other case
        Assert.Equal([@"C:\D\y.txt"], a.Items);
        Assert.Equal([@"c:\d\X.TXT"], b.Items);
        Assert.Same(b, l.ShelfOf(@"C:\D\x.txt"));
    }

    [Fact]
    public void Rename_follows_the_file_and_its_home()
    {
        var l = new Layout();
        var a = new ShelfModel();
        l.Shelves.Add(a);
        l.Shelve("old", a);
        l.Home["old"] = new Pt(5, 6);
        Assert.True(l.Rename("old", "new"));
        Assert.Equal(["new"], a.Items);
        Assert.Equal(new Pt(5, 6), l.Home["new"]);
        Assert.False(l.Home.ContainsKey("old"));
    }

    [Fact]
    public void Portals_do_not_own_desktop_items()
    {
        var l = new Layout();
        l.Shelves.Add(new ShelfModel { Portal = @"C:\Downloads", Items = ["x"] });
        Assert.Null(l.ShelfOf("x"));
        Assert.Empty(l.AllShelved());
    }

    [Fact]
    public void Layout_round_trips_through_json()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = Path.Combine(dir, "layout.json");
            var l = new Layout();
            l.Shelves.Add(new ShelfModel { Title = "Games", Monitor = @"\\.\DISPLAY1", Rel = new(0.1, 0.2, 0.3, 0.4), Edge = Geometry.Edge.Right, Rolled = true, Items = ["a", "b"] });
            l.Home["a"] = new Pt(10, 20);
            LayoutStore.Save(path, l);
            var back = LayoutStore.Load(path);
            var s = Assert.Single(back.Shelves);
            Assert.Equal("Games", s.Title);
            Assert.Equal(Geometry.Edge.Right, s.Edge);
            Assert.True(s.Rolled);
            Assert.Equal(new Geometry.RelBox(0.1, 0.2, 0.3, 0.4), s.Rel);
            Assert.Equal(["a", "b"], s.Items);
            Assert.Equal(new Pt(10, 20), back.Home["A"]); // case-insensitive after loading
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Broken_files_load_as_empty()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = Path.Combine(dir, "layout.json");
            File.WriteAllText(path, "{ not json");
            Assert.Empty(LayoutStore.Load(path).Shelves);
            File.WriteAllText(path, """{ "Version": 0, "Shelves": null, "Home": null }""");
            var l = LayoutStore.Load(path);
            Assert.Empty(l.Shelves);
            Assert.Equal(Layout.CurrentVersion, l.Version);
            Assert.Empty(LayoutStore.Load(Path.Combine(dir, "missing.json")).Shelves);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Settings_migration_clamps_values()
    {
        var s = Settings.Migrate(new Settings { Opacity = 400, CornerRadius = -3, LabelLines = 9, Language = null!, Version = 0 });
        Assert.Equal(100, s.Opacity);
        Assert.Equal(0, s.CornerRadius);
        Assert.Equal(3, s.LabelLines);
        Assert.Equal("", s.Language);
        Assert.Equal(Settings.CurrentVersion, s.Version);
    }

    [Fact]
    public void Snapshots_rotate_only_automatic_ones()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var store = new SnapshotStore(dir);
            var t = new DateTime(2026, 11, 1, 9, 0, 0);
            store.Save(new Layout(), "Mine", auto: false, t);
            for (int i = 0; i < 14; i++) store.Save(new Layout(), "Daily", auto: true, t.AddDays(i + 1));
            var all = store.List();
            Assert.Equal(SnapshotStore.KeepAuto, all.Count(x => x.Snap.Auto));
            Assert.Contains(all, x => x.Snap.Name == "Mine");
            Assert.Equal(t.AddDays(14), all[0].Snap.Time); // newest first
            Assert.False(store.NeedsDaily(t.AddDays(14)));
            Assert.True(store.NeedsDaily(t.AddDays(15)));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Scrambled_when_most_shelved_items_are_gone()
    {
        var l = new Layout();
        l.Shelves.Add(new ShelfModel { Items = ["a", "b", "c", "d"] });
        Assert.False(Health.LooksScrambled(l, new HashSet<string> { "a", "b", "c", "d" }));
        Assert.False(Health.LooksScrambled(l, new HashSet<string> { "a", "b" })); // half: not more than half
        Assert.True(Health.LooksScrambled(l, new HashSet<string> { "a" }));
        var small = new Layout();
        small.Shelves.Add(new ShelfModel { Items = ["a", "b"] });
        Assert.False(Health.LooksScrambled(small, new HashSet<string>())); // fewer than 3
    }

    [Fact]
    public void Settings_from_version_1_get_the_faster_defaults_unless_changed()
    {
        var old = Settings.Migrate(new Settings { Version = 1, HoverDelayMs = 300, LeaveDelayMs = 500, Opacity = 55 });
        Assert.Equal(150, old.HoverDelayMs);
        Assert.Equal(0, old.LeaveDelayMs);
        Assert.Equal(60, old.Opacity);
        var mine = Settings.Migrate(new Settings { Version = 1, HoverDelayMs = 700, Opacity = 30 });
        Assert.Equal(700, mine.HoverDelayMs);
        Assert.Equal(30, mine.Opacity);
    }

    [Theory]
    [InlineData("#1a2b3c", "#1A2B3C")]
    [InlineData("1a2b3c", "#1A2B3C")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("#12345", "")]
    [InlineData("#GGGGGG", "")]
    public void Colors_normalize(string? input, string expected) => Assert.Equal(expected, Colors.Normalize(input));

    [Fact]
    public void Colors_parse_and_pick_readable_text()
    {
        Assert.Equal((26, 43, 60), Colors.Parse("#1A2B3C"));
        Assert.Null(Colors.Parse("nope"));
        Assert.True(Colors.IsLight(236, 238, 242));
        Assert.False(Colors.IsLight(46, 54, 72));
        Assert.All(Colors.Presets, p => Assert.Equal(p.Hex, Colors.Normalize(p.Hex)));
    }

    [Fact]
    public void Shelf_colour_and_opacity_survive_clone()
    {
        var m = new ShelfModel { Tint = "#5E1F32", Opacity = 80 }.Clone();
        Assert.Equal("#5E1F32", m.Tint);
        Assert.Equal(80, m.Opacity);
    }

    [Fact]
    public void Version_2_settings_roll_up_instantly_unless_the_delay_was_changed()
    {
        Assert.Equal(0, Settings.Migrate(new Settings { Version = 2, LeaveDelayMs = 300 }).LeaveDelayMs);
        Assert.Equal(800, Settings.Migrate(new Settings { Version = 2, LeaveDelayMs = 800 }).LeaveDelayMs);
        Assert.Equal(0, new Settings().LeaveDelayMs);
    }
}
