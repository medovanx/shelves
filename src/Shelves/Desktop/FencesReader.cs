using Microsoft.Win32;

namespace Shelves.Desktop;

/// <summary>Turns Stardock Fences' saved fences (in the registry) into shelves.</summary>
static class FencesReader
{
    const string Key = @"Software\Stardock\Fences";

    public static bool Available()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key + @"\Groups");
        return k != null && k.GetValueNames().Any(n => n.Length > 0);
    }

    static Box Work(Screen s) => new(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height);

    /// <summary>Fences' layout as shelves, placed where the fences were on the same monitors.</summary>
    public static List<ShelfModel> Read(IconSize size)
    {
        using var g = Registry.CurrentUser.OpenSubKey(Key + @"\Groups");
        using var i = Registry.CurrentUser.OpenSubKey(Key + @"\Icons");
        if (g == null || i == null) return [];
        var groups = g.GetValueNames().Where(n => n.Length > 0).Select(n => g.GetValue(n) as string ?? "");
        var icons = i.GetValueNames().Where(n => n.Length > 0).Select(n => new KeyValuePair<string, string>(n, i.GetValue(n) as string ?? ""));
        var res = new List<ShelfModel>();
        var placed = new List<(ImportedFence f, Screen scr, Box b)>();
        foreach (var f in FencesImport.Parse(groups, icons))
        {
            var scr = Screen.AllScreens.FirstOrDefault(s => string.Equals(s.DeviceName, f.Monitor, StringComparison.OrdinalIgnoreCase)) ?? Screen.PrimaryScreen!;
            // Fences stores list-view coordinates; its monitor origin turns them into a place on that monitor.
            var b = new Box(f.Box.X - f.MonitorOrigin.X + scr.Bounds.X, f.Box.Y - f.MonitorOrigin.Y + scr.Bounds.Y, f.Box.W, f.Box.H);
            placed.Add((f, scr, b.ClampInto(Work(scr))));
        }
        foreach (var grp in placed.GroupBy(p => p.scr.DeviceName))
        {
            var list = grp.ToList();
            var work = Work(list[0].scr);
            var boxes = ShelfGeometry.Separate(list.Select(p => p.b).ToList(), work, 6);
            for (int k = 0; k < list.Count; k++)
            {
                var b = boxes[k];
                res.Add(new ShelfModel
                {
                    Title = list[k].f.Title,
                    Monitor = list[k].scr.DeviceName,
                    Rel = ShelfGeometry.ToRel(b, work),
                    Edge = ShelfGeometry.NearEdge(b, work, 2),
                    IconSize = size,
                    Items = [.. list[k].f.Items],
                });
            }
        }
        return res;
    }
}
