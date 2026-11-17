namespace Shelves.Core.Model;

/// <summary>One fence read from Stardock Fences' settings.</summary>
public sealed record ImportedFence(int Id, string Title, string Monitor, Geometry.Box Box, Pt MonitorOrigin, IReadOnlyList<string> Items);

/// <summary>
/// Reads Stardock Fences' layout (its registry values under HKCU\Software\Stardock\Fences, or the same values in
/// its snapshot XML). Positions there are in the desktop list view's coordinates, like Explorer's own icon positions.
/// </summary>
public static class FencesImport
{
    /// <summary>
    /// <paramref name="groups"/>: the Groups values ("id|x|y|w|h|?|title` |monitor` |monX|monY|monW|monH|...").
    /// <paramref name="icons"/>: the Icons values, keyed by parsing name ("fenceId|x|y|xInFence|yInFence|...").
    /// </summary>
    public static List<ImportedFence> Parse(IEnumerable<string> groups, IEnumerable<KeyValuePair<string, string>> icons)
    {
        // Icons by fence id, in reading order inside the fence (row by row, left to right).
        var byFence = icons
            .Select(kv => (name: kv.Key, f: kv.Value.Split('|')))
            .Where(x => x.f.Length > 4 && int.TryParse(x.f[0], out int id) && id >= 0)
            .GroupBy(x => int.Parse(x.f[0]))
            .ToDictionary(g => g.Key, g => g
                .OrderBy(x => Num(x.f[4])).ThenBy(x => Num(x.f[3]))
                .Select(x => x.name).ToList());

        var res = new List<ImportedFence>();
        foreach (var raw in groups)
        {
            var f = raw.Split('|');
            if (f.Length < 12 || !int.TryParse(f[0], out int id)) continue;
            var box = new Geometry.Box(Num(f[1]), Num(f[2]), Num(f[3]), Num(f[4]));
            if (box.W <= 0 || box.H <= 0) continue;
            string title = Unescape(f[6]);
            string monitor = Unescape(f[7]);
            var origin = new Pt(Num(f[8]), Num(f[9]));
            res.Add(new ImportedFence(id, title, monitor, box, origin, byFence.TryGetValue(id, out var items) ? items : []));
        }
        return res;
    }

    /// <summary>Fences writes a space as a backquote and pads fields with one.</summary>
    static string Unescape(string s) => s.Replace('`', ' ').Trim();

    static int Num(string s) => int.TryParse(s.Trim(), out int v) ? v : 0;
}
