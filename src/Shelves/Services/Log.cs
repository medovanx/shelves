namespace Shelves.Services;

static class Log
{
    public static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shelves", "error.log");

    /// <summary>A plain line in the log, for checking behaviour.</summary>
    public static void Note(string text)
    {
        try { File.AppendAllText(Path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {text}\r\n"); } catch { }
    }

    public static void Error(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.AppendAllText(Path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
        }
        catch { }
    }
}
