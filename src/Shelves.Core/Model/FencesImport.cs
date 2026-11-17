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

    /// <summary>
    /// The Groups and Icons values from one of Fences' own backups: a snapshot XML (Fences' daily backups and
    /// saved snapshots), or a .reg export of HKCU\Software\Stardock\Fences. Returns null for anything else.
    /// </summary>
    public static (List<string> groups, List<KeyValuePair<string, string>> icons)? ReadBackup(string text)
    {
        text = text.TrimStart('\uFEFF');
        if (text.Contains("<SnapshotInfo")) return FromSnapshot(text);
        if (text.Contains("Windows Registry Editor") || text.Contains("REGEDIT4")) return FromReg(text);
        return null;
    }

    static (List<string>, List<KeyValuePair<string, string>>)? FromSnapshot(string xml)
    {
        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(xml);
            IEnumerable<string[]> Pairs(string section) => doc.Root?.Element(section)?.Elements("ArrayOfString")
                .Select(a => a.Elements("string").Select(e => e.Value).ToArray()).Where(a => a.Length >= 2) ?? [];
            var groups = Pairs("Groups").Select(a => a[1]).ToList();
            var icons = Pairs("Icons").Select(a => new KeyValuePair<string, string>(a[0], a[1])).ToList();
            return groups.Count == 0 ? null : (groups, icons);
        }
        catch (System.Xml.XmlException) { return null; }
    }

    /// <summary>A regedit export: string values under the Fences\Groups and Fences\Icons keys.</summary>
    static (List<string>, List<KeyValuePair<string, string>>)? FromReg(string reg)
    {
        var groups = new List<string>();
        var icons = new List<KeyValuePair<string, string>>();
        string? section = null;
        foreach (var raw in reg.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith('['))
            {
                section = line.EndsWith(@"\Fences\Groups]", StringComparison.OrdinalIgnoreCase) ? "g"
                        : line.EndsWith(@"\Fences\Icons]", StringComparison.OrdinalIgnoreCase) ? "i" : null;
                continue;
            }
            if (section == null || !line.StartsWith('"')) continue;
            // "name"="value", with \\ and \" escaped in both.
            int i = 1;
            string name = ReadQuoted(line, ref i);
            if (i >= line.Length || line[i] != '=' || i + 1 >= line.Length || line[i + 1] != '"') continue;
            i += 2;
            string value = ReadQuoted(line, ref i);
            if (section == "g") groups.Add(value);
            else icons.Add(new(name, value));
        }
        return groups.Count == 0 ? null : (groups, icons);
    }

    static string ReadQuoted(string s, ref int i)
    {
        var sb = new System.Text.StringBuilder();
        while (i < s.Length)
        {
            char c = s[i++];
            if (c == '\\' && i < s.Length) { sb.Append(s[i++]); continue; }
            if (c == '"') break;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Fences writes a space as a backquote and pads fields with one.</summary>
    static string Unescape(string s) => s.Replace('`', ' ').Trim();

    static int Num(string s) => int.TryParse(s.Trim(), out int v) ? v : 0;
}
