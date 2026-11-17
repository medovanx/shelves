namespace Shelves;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 1 && args[0] == "--shots")
        {
            ApplicationConfiguration.Initialize();
            return Shots.Run(args[1]);
        }
        if (args.Length > 1 && args[0] == "--compose")
        {
            ApplicationConfiguration.Initialize();
            return Shots.Compose(args[1]);
        }
        if (args.Length > 1 && args[0] == "--render-icon")
            return IconMaker.Run(args[1]);
        if (args.Length > 1 && args[0] == "--fences-dump")
        {
            // Writes what an import from Fences would add, for checking.
            var lines = FencesReader.Read(IconSize.Medium).Select(m => $"{m.Title}	{m.Edge}	{m.Rel}	{m.Items.Count}");
            File.WriteAllLines(args[1], lines);
            return 0;
        }
        if (args.Contains("--import-fences"))
        {
            // Asks a running Shelves to add Stardock Fences' fences as shelves.
            Native.PostMessage((IntPtr)0xFFFF, Native.RegisterWindowMessage(ShelvesApp.ImportMessage), IntPtr.Zero, IntPtr.Zero);
            return 0;
        }
        if (args.Contains("--exit"))
        {
            // Closes a running Shelves cleanly (its icons go back on the desktop).
            Native.PostMessage((IntPtr)0xFFFF, Native.RegisterWindowMessage(ShelvesApp.ExitMessage), IntPtr.Zero, IntPtr.Zero);
            return 0;
        }
        using var mutex = new Mutex(false, @"Local\Shelves.SingleInstance");
        bool created;
        try
        {
            // After an update the old version is still exiting: wait for it to let go.
            created = mutex.WaitOne(args.Contains("--updated") || args.Contains("--restarted") ? TimeSpan.FromSeconds(20) : TimeSpan.Zero);
        }
        catch (AbandonedMutexException) { created = true; }
        if (!created)
        {
            // Already running: launching again opens the settings.
            Native.AllowSetForegroundWindow(-1);
            Native.PostMessage((IntPtr)0xFFFF, Native.RegisterWindowMessage(ShelvesApp.ShowMessage), IntPtr.Zero, IntPtr.Zero);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        ShelvesApp? app = null;
        Application.ThreadException += (_, e) => Log.Error(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error(e.ExceptionObject as Exception);
            // Dying: never leave icons parked off screen.
            app?.RestoreExplorer();
        };
        try
        {
            app = new ShelvesApp(args.Contains("--updated"));
            Application.Run(app);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            app?.RestoreExplorer();
            if (!args.Contains("--restarted"))
                try { System.Diagnostics.Process.Start(Environment.ProcessPath!, "--restarted"); } catch { }
            return 1;
        }
        return 0;
    }
}
