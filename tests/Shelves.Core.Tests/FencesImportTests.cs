using Shelves.Core.Geometry;
using Shelves.Core.Model;

namespace Shelves.Core.Tests;

public class FencesImportTests
{
    static readonly string[] Groups =
    [
        "6|2518|0|672|580|0|Games` |\\\\.\\DISPLAY1` |1280|0|1920|1080|0|0|255|0|` |-1|1|1|1|0|{CF5E5C43-F6DF-4FAD-8F3B-57AD26FC5959} |0|0|0|",
        "11|2902|483|288|290|0|Two`Words` |\\\\.\\DISPLAY1` |1280|0|1920|1080|0|0|255|0|` |-1|0|0|0|0|{9FD3D1B4-3B0A-466E-B3F4-0A522E8416AF} |0|0|0|",
        "broken",
    ];

    static readonly Dictionary<string, string> Icons = new()
    {
        [@"C:\D\b.lnk"] = @"6|2635|2|117|2|0|1280|0|\\.\DISPLAY1|1|0|0||0|1|1",
        [@"C:\D\a.lnk"] = @"6|2539|2|21|2|0|1280|0|\\.\DISPLAY1|1|0|0||0|1|1",
        [@"C:\D\c.lnk"] = @"6|2539|98|21|98|0|1280|0|\\.\DISPLAY1|1|0|0||0|1|1",
        [@"C:\D\loose.lnk"] = @"-1|1291|2|1291|2|0|1280|0|\\.\DISPLAY1|1|0|0||0|1|1",
        [@"C:\D\x.lnk"] = @"11|2923|485|21|2|0|1280|0|\\.\DISPLAY1|1|0|0||0|1|1",
    };

    [Fact]
    public void Reads_fences_with_titles_monitors_and_bounds()
    {
        var f = FencesImport.Parse(Groups, Icons);
        Assert.Equal(2, f.Count);
        Assert.Equal("Games", f[0].Title);
        Assert.Equal(@"\\.\DISPLAY1", f[0].Monitor);
        Assert.Equal(new Box(2518, 0, 672, 580), f[0].Box);
        Assert.Equal(new Pt(1280, 0), f[0].MonitorOrigin);
        Assert.Equal("Two Words", f[1].Title);
    }

    [Fact]
    public void Icons_come_in_reading_order_and_loose_ones_are_left_out()
    {
        var f = FencesImport.Parse(Groups, Icons);
        Assert.Equal([@"C:\D\a.lnk", @"C:\D\b.lnk", @"C:\D\c.lnk"], f[0].Items);
        Assert.Equal([@"C:\D\x.lnk"], f[1].Items);
        Assert.DoesNotContain(f.SelectMany(x => x.Items), n => n.Contains("loose"));
    }
}
