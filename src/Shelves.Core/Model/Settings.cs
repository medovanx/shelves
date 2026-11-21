namespace Shelves.Core.Model;

public enum TitleAlign { Left, Center, Right }

/// <summary>User preferences, saved as JSON beside the layout.</summary>
public sealed class Settings
{
    public const int CurrentVersion = 4;
    public int Version { get; set; } = CurrentVersion;
    /// <summary>"en" or "ar"; empty follows Windows.</summary>
    public string Language { get; set; } = "";
    public bool StartWithWindows { get; set; } = true;
    /// <summary>Adds "New shelf" to the desktop's own right-click menu while Shelves runs.</summary>
    public bool DesktopMenu { get; set; }
    public bool DoubleClickHides { get; set; } = true;
    /// <summary>Shelves draws the desktop's icons itself (Explorer's own are hidden while it runs).</summary>
    public bool DrawDesktop { get; set; }
    /// <summary>With grid alignment on, the desktop Shelves draws shows each grid cell faintly while icons are moved.</summary>
    public bool ShowGridSlots { get; set; }
    /// <summary>
    /// Snap desktop icons to the grid. Shelves does this itself while it runs, because Windows' own "Align icons to
    /// grid" will not let hidden icons stay off screen. Windows' switch is set to match when Shelves exits.
    /// </summary>
    public bool AlignToGrid { get; set; }
    /// <summary>How solid a shelf's colour is over the blurred wallpaper, 0 (clear glass) to 100 (solid).</summary>
    public int Opacity { get; set; } = 50;
    /// <summary>The colour of every shelf ("#RRGGBB"), or empty for the theme's own.</summary>
    public string Tint { get; set; } = "";
    public int CornerRadius { get; set; } = 4;
    public TitleAlign TitleAlign { get; set; } = TitleAlign.Center;
    public int LabelLines { get; set; } = 3;
    public IconSize DefaultIconSize { get; set; } = IconSize.Medium;
    public int HoverDelayMs { get; set; } = 0;
    public int LeaveDelayMs { get; set; } = 0;
    public bool HoverToPeek { get; set; } = true;
    /// <summary>Animate rolling up and opening. Shelves' own choice, not Windows' animation effects setting.</summary>
    public bool AnimateRoll { get; set; } = true;
    public bool CheckUpdates { get; set; } = true;
    public DateTime LastUpdateCheck { get; set; }

    public static Settings Load(string path) => Migrate(Json.Read<Settings>(path) ?? new Settings());

    public static Settings Migrate(Settings s)
    {
        if (s.Version < 2)
        {
            // Version 1's slower hover timings and weaker glass: move to the new defaults unless the user changed them.
            if (s.HoverDelayMs == 300) s.HoverDelayMs = 150;
            if (s.LeaveDelayMs == 500) s.LeaveDelayMs = 300;
            if (s.Opacity == 55) s.Opacity = 60;
        }
        // Version 3 rolls up the moment the mouse leaves, unless the user chose a delay of their own.
        if (s.Version < 3 && s.LeaveDelayMs is 300 or 500) s.LeaveDelayMs = 0;
        // Version 4's defaults, for values the user never changed.
        if (s.Version < 4)
        {
            if (s.CornerRadius == 8) s.CornerRadius = 4;
            if (s.Opacity is 55 or 60) s.Opacity = 50;
            if (s.LabelLines == 2) s.LabelLines = 3;
            if (s.HoverDelayMs is 150 or 300) s.HoverDelayMs = 0;
        }
        s.Tint = Colors.Normalize(s.Tint);
        s.Opacity = Math.Clamp(s.Opacity, 0, 100);
        s.CornerRadius = Math.Clamp(s.CornerRadius, 0, 16);
        s.LabelLines = Math.Clamp(s.LabelLines, 1, 3);
        s.HoverDelayMs = Math.Clamp(s.HoverDelayMs, 0, 3000);
        s.LeaveDelayMs = Math.Clamp(s.LeaveDelayMs, 0, 5000);
        s.Language ??= "";
        s.Version = CurrentVersion;
        return s;
    }

    public void Save(string path) => Json.Write(path, this);
}
