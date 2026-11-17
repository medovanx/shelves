using System.Drawing.Drawing2D;

namespace Shelves.UI.Styling;

/// <summary>Colours shared by shelves, menus and the settings window.</summary>
sealed record Theme(bool Dark)
{
    public static Theme Current => new(Native.SystemDark());
    public Color Window => Dark ? Color.FromArgb(30, 32, 32) : Color.FromArgb(243, 245, 244);
    public Color Surface => Dark ? Color.FromArgb(40, 43, 43) : Color.White;
    public Color Input => Dark ? Color.FromArgb(52, 56, 56) : Color.FromArgb(246, 247, 247);
    public Color Border => Dark ? Color.FromArgb(62, 66, 66) : Color.FromArgb(222, 225, 224);
    public Color Text => Dark ? Color.FromArgb(238, 238, 238) : Color.FromArgb(25, 25, 25);
    public Color Dim => Dark ? Color.FromArgb(160, 166, 166) : Color.FromArgb(105, 110, 110);
    public Color Accent => Dark ? Color.FromArgb(140, 160, 255) : Color.FromArgb(60, 78, 200);
    public Color AccentFill => Color.FromArgb(76, 96, 226);
}
