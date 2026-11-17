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
        if (args.Length > 1 && args[0] == "--menu-shot")
        {
            // A themed menu at a fixed spot, captured with a margin around it to check its edges (local check only).
            ApplicationConfiguration.Initialize();
            var m = new ContextMenuStrip { AutoClose = false };
            foreach (var t in new[] { "Rename", "Roll up", "Dock", "Sort by" }) m.Items.Add(t);
            ThemedMenuRenderer.Style(m, Theme.Current);
            var host = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(220, 220), Size = new Size(400, 300), TopMost = true, FormBorderStyle = FormBorderStyle.None, BackColor = Color.White };
            host.Shown += async (_, _) =>
            {
                m.Show(new Point(300, 300));
                await Task.Delay(800);
                using var bmp = new Bitmap(m.Width + 80, m.Height + 80);
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(260, 260, 0, 0, bmp.Size);
                bmp.Save(args[1]);
                m.Close();
                host.Close();
            };
            Application.Run(host);
            return 0;
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
            var lines = (args.Length > 2 ? FencesReader.ReadFile(args[2], IconSize.Medium) : FencesReader.Read(IconSize.Medium)).Select(m => $"{m.Title}	{m.Edge}	{m.Rel}	{m.Items.Count}");
            File.WriteAllLines(args[1], lines);
            return 0;
        }
        if (args.Contains("--install-menu") || args.Contains("--remove-menu"))
        {
            // The elevated step for the desktop menu entry (started by Settings with an admin prompt).
            return DesktopMenu.Write(args.Contains("--install-menu")) ? 0 : 1;
        }
        if (args.Contains("--new-shelf") && Mutex.TryOpenExisting(@"Local\Shelves.SingleInstance", out var running))
        {
            // From the desktop's right-click menu while Shelves runs: ask it for a new shelf where the menu was.
            running.Dispose();
            Native.AllowSetForegroundWindow(-1);
            Native.PostMessage((IntPtr)0xFFFF, Native.RegisterWindowMessage(ShelvesApp.NewShelfMessage), IntPtr.Zero, IntPtr.Zero);
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
            // Started from the desktop menu while it was not running: make the shelf once it is up.
            if (args.Contains("--new-shelf")) app.NewShelfAtMenu();
            int fi = Array.IndexOf(args, "--import-fences-file");
            if (fi >= 0 && fi + 1 < args.Length) app.ImportFencesFile(args[fi + 1]);
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
