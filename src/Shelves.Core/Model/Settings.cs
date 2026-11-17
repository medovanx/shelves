namespace Shelves.Core.Model;

public enum TitleAlign { Left, Center, Right }

/// <summary>User preferences, saved as JSON beside the layout.</summary>
public sealed class Settings
{
    public const int CurrentVersion = 1;
    public int Version { get; set; } = CurrentVersion;
    /// <summary>"en" or "ar"; empty follows Windows.</summary>
    public string Language { get; set; } = "";
    public bool StartWithWindows { get; set; } = true;
    public bool DoubleClickHides { get; set; } = true;
    /// <summary>How solid the acrylic tint is, 0 to 100.</summary>
    public int Opacity { get; set; } = 55;
    public int CornerRadius { get; set; } = 8;
    public TitleAlign TitleAlign { get; set; } = TitleAlign.Center;
    public int LabelLines { get; set; } = 2;
    public IconSize DefaultIconSize { get; set; } = IconSize.Medium;
    public int HoverDelayMs { get; set; } = 300;
    public int LeaveDelayMs { get; set; } = 500;
    public bool HoverToPeek { get; set; } = true;
    public bool CheckUpdates { get; set; } = true;
    public DateTime LastUpdateCheck { get; set; }

    public static Settings Load(string path) => Migrate(Json.Read<Settings>(path) ?? new Settings());

    public static Settings Migrate(Settings s)
    {
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
