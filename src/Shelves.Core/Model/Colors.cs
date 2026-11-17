namespace Shelves.Core.Model;

/// <summary>Shelf colours as "#RRGGBB" strings.</summary>
public static class Colors
{
    /// <summary>The colours offered in menus and settings, by name (empty is the theme's own).</summary>
    public static readonly (string Key, string Hex)[] Presets =
    [
        ("Slate", "#2E3648"), ("Indigo", "#3A3F8C"), ("Teal", "#145A5A"), ("Forest", "#22502F"),
        ("Wine", "#5E1F32"), ("Amber", "#7A5214"), ("Rose", "#8E4466"), ("Snow", "#ECEEF2"), ("Black", "#0B0B0D"),
    ];

    /// <summary>"#RRGGBB" in upper case, or "" when it is not a colour.</summary>
    public static string Normalize(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return "";
        var h = hex.Trim().TrimStart('#');
        return h.Length == 6 && int.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out _) ? "#" + h.ToUpperInvariant() : "";
    }

    public static (int R, int G, int B)? Parse(string? hex)
    {
        var n = Normalize(hex);
        if (n.Length == 0) return null;
        int v = int.Parse(n[1..], System.Globalization.NumberStyles.HexNumber);
        return ((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
    }

    public static string ToHex(int r, int g, int b) => $"#{r:X2}{g:X2}{b:X2}";

    /// <summary>True when dark text reads better than light text on this colour.</summary>
    public static bool IsLight(int r, int g, int b) => 0.299 * r + 0.587 * g + 0.114 * b > 150;
}
