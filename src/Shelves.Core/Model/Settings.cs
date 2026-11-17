namespace Shelves.Core.Model;

public enum TitleAlign { Left, Center, Right }

/// <summary>User preferences, saved as JSON beside the layout.</summary>
public sealed class Settings
{
    public const int CurrentVersion = 2;
    public int Version { get; set; } = CurrentVersion;
    /// <summary>"en" or "ar"; empty follows Windows.</summary>
    public string Language { get; set; } = "";
    public bool StartWithWindows { get; set; } = true;
    public bool DoubleClickHides { get; set; } = true;
    /// <summary>How solid a shelf's colour is over the blurred wallpaper, 0 (clear glass) to 100 (solid).</summary>
    public int Opacity { get; set; } = 60;
    /// <summary>The colour of every shelf ("#RRGGBB"), or empty for the theme's own.</summary>
    public string Tint { get; set; } = "";
    public int CornerRadius { get; set; } = 8;
    public TitleAlign TitleAlign { get; set; } = TitleAlign.Center;
    public int LabelLines { get; set; } = 2;
    public IconSize DefaultIconSize { get; set; } = IconSize.Medium;
    public int HoverDelayMs { get; set; } = 150;
    public int LeaveDelayMs { get; set; } = 300;
    public bool HoverToPeek { get; set; } = true;
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
