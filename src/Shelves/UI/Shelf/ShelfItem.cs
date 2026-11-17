using System.Runtime.InteropServices;

namespace Shelves.UI.Shelf;

/// <summary>One icon shown in a shelf: a desktop item, or a file in a portal's folder.</summary>
sealed class ShelfItem(string name, string label)
{
    /// <summary>Parsing name: a full path, or ::{GUID} for virtual items such as the Recycle Bin.</summary>
    public string Name { get; } = name;
    public string Label { get; set; } = label;
    /// <summary>Where it is drawn, in shelf client coordinates (before scrolling).</summary>
    public Rectangle Cell { get; set; }
}

/// <summary>Shell icons by name and size, loaded once.</summary>
static class IconCache
{
    static readonly Dictionary<(string, int), Bitmap?> cache = new();
    static readonly Dictionary<string, string> labels = new(StringComparer.OrdinalIgnoreCase);

    public static Bitmap? Get(string name, int px)
    {
        var key = (name.ToLowerInvariant(), px);
        if (!cache.TryGetValue(key, out var bmp))
        {
            bmp = ShellOps.Icon(name, px);
            cache[key] = bmp;
        }
        return bmp;
    }

    public static string Label(string name)
    {
        if (!labels.TryGetValue(name, out var l))
            labels[name] = l = ShellOps.DisplayName(name);
        return l;
    }

    /// <summary>Forgets one item (it changed on disk), or everything.</summary>
    public static void Forget(string? name = null)
    {
        if (name == null)
        {
            foreach (var b in cache.Values) b?.Dispose();
            cache.Clear();
            labels.Clear();
            return;
        }
        foreach (var k in cache.Keys.Where(k => string.Equals(k.Item1, name, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            cache[k]?.Dispose();
            cache.Remove(k);
        }
        labels.Remove(name);
    }
}

/// <summary>Reads the names of the items in a drag: real paths, and shell items such as the Recycle Bin.</summary>
static class DropNames
{
    public static List<string> From(IDataObject data)
    {
        var res = new List<string>();
        if (data.GetDataPresent("Shell IDList Array") && data.GetData("Shell IDList Array") is MemoryStream ms)
            res.AddRange(FromIdList(ms.ToArray()));
        if (res.Count == 0 && data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
            res.AddRange(files);
        return res;
    }

    /// <summary>A CIDA: a count, offsets, the parent folder's pidl, then each item's pidl relative to it.</summary>
    static unsafe List<string> FromIdList(byte[] cida)
    {
        var res = new List<string>();
        if (cida.Length < 8) return res;
        fixed (byte* p = cida)
        {
            uint n = *(uint*)p;
            var offs = (uint*)(p + 4);
            if (8 + n * 4 > cida.Length) return res;
            var parent = (IntPtr)(p + offs[0]);
            for (int i = 1; i <= n; i++)
            {
                var child = (IntPtr)(p + offs[i]);
                var abs = ILCombine(parent, child);
                if (abs == IntPtr.Zero) continue;
                try
                {
                    if (SHGetNameFromIDList(abs, 0x80028000 /* SIGDN_DESKTOPABSOLUTEPARSING */, out var name) == 0 && name != IntPtr.Zero)
                    {
                        res.Add(Marshal.PtrToStringUni(name)!);
                        Marshal.FreeCoTaskMem(name);
                    }
                }
                finally { ILFree(abs); }
            }
        }
        return res;
    }

    [DllImport("shell32.dll")] static extern IntPtr ILCombine(IntPtr a, IntPtr b);
    [DllImport("shell32.dll")] static extern void ILFree(IntPtr p);
    [DllImport("shell32.dll")] static extern int SHGetNameFromIDList(IntPtr pidl, uint sigdn, out IntPtr name);
}
