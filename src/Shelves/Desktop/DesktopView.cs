using System.Runtime.InteropServices;
using System.Text;

namespace Shelves.Desktop;

/// <summary>One icon on Explorer's desktop.</summary>
sealed record DesktopItem(string Name, int Index, Pt Pos);

/// <summary>
/// Explorer's desktop icons, read through the shell's IFolderView2 and moved through the desktop list view's own
/// messages (IFolderView's SelectAndPositionItems does not stick on current Windows). Hiding an icon parks it far
/// off screen; Explorer's F5 or a restart brings it back, and <see cref="IconKeeper"/> parks it again.
/// </summary>
sealed class DesktopView : IDisposable
{
    /// <summary>Where hidden icons wait: far beyond any screen, inside the 16-bit range the list view accepts.</summary>
    public static readonly Pt Parked = new(30000, 30000);

    /// <summary>
    /// The off-screen spot for the n-th hidden icon. Each gets its own, a grid cell apart, because with "Align icons
    /// to grid" on Explorer spreads icons that share a spot back onto the screen. Stays inside the 16-bit range the
    /// list view accepts, and at or past 20000 so <see cref="IsParked"/> knows it.
    /// </summary>
    public static Pt ParkSlot(int n, Size spacing)
    {
        int w = Math.Max(spacing.Width, 40), h = Math.Max(spacing.Height, 40);
        int perRow = Math.Max(1, (32000 - 20000) / w);
        return new Pt(20000 + n % perRow * w, 20000 + n / perRow * h);
    }
    public static bool IsParked(Pt p) => p.X >= 20000 || p.Y >= 20000;

    IFolderView2? view;
    IShellFolder? folder;
    IntPtr process, buffer, textBuffer;
    public IntPtr ListView { get; private set; }
    public IntPtr DefView { get; private set; }
    public Size Spacing { get; private set; } = new(76, 103);

    public bool Attached => view != null && Native.IsWindow(ListView);

    /// <summary>Finds the desktop view. False while Explorer is not running (or still starting).</summary>
    public bool Attach()
    {
        Detach();
        try
        {
            var sw = (IShellWindows)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"))!)!;
            object loc = 0 /* CSIDL_DESKTOP */, root = null!;
            var disp = sw.FindWindowSW(ref loc, ref root, 8 /* SWC_DESKTOP */, out _, 1 /* SWFO_NEEDDISPATCH */);
            Guid sid = new("4C96BE40-915C-11CF-99D3-00AA004AE837"), iid = new("000214E2-0000-0000-C000-000000000046");
            ((IComServiceProvider)disp).QueryService(ref sid, ref iid, out var browser);
            ((IShellBrowser)browser).QueryActiveShellView(out var sv);
            view = (IFolderView2)sv;
            Guid iidf = Shell32.IID_IShellFolder;
            view.GetFolder(ref iidf, out var f);
            folder = (IShellFolder)f;
            view.GetSpacing(out var sp);
            if (sp.X > 0 && sp.Y > 0) Spacing = new Size(sp.X, sp.Y);

            ListView = FindListView();
            if (ListView == IntPtr.Zero) { Detach(); return false; }
            DefView = Native.GetParent(ListView);
            Native.GetWindowThreadProcessId(ListView, out uint pid);
            process = OpenProcess(0x0008 | 0x0010 | 0x0020 /* VM_OPERATION | VM_READ | VM_WRITE */, false, pid);
            buffer = VirtualAllocEx(process, IntPtr.Zero, 64, 0x3000, 4);
            // An LVITEM (first 128 bytes) and its text (the rest), for reading icon labels.
            textBuffer = VirtualAllocEx(process, IntPtr.Zero, 128 + 1024, 0x3000, 4);
            return buffer != IntPtr.Zero;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or NullReferenceException)
        {
            Log.Error(ex);
            Detach();
            return false;
        }
    }

    /// <summary>The desktop's SysListView32: under Progman, or under a WorkerW on older builds.</summary>
    static IntPtr FindListView()
    {
        var progman = Native.FindWindow("Progman", null);
        var dv = Native.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (dv == IntPtr.Zero)
        {
            Native.EnumWindows((h, _) =>
            {
                if (Native.ClassOf(h) != "WorkerW") return true;
                dv = Native.FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
                return dv == IntPtr.Zero;
            }, IntPtr.Zero);
        }
        return dv == IntPtr.Zero ? IntPtr.Zero : Native.FindWindowEx(dv, IntPtr.Zero, "SysListView32", null);
    }

    public void Detach()
    {
        if (buffer != IntPtr.Zero) { VirtualFreeEx(process, buffer, 0, 0x8000); buffer = IntPtr.Zero; }
        if (textBuffer != IntPtr.Zero) { VirtualFreeEx(process, textBuffer, 0, 0x8000); textBuffer = IntPtr.Zero; }
        if (process != IntPtr.Zero) { CloseHandle(process); process = IntPtr.Zero; }
        if (view != null) { try { Marshal.ReleaseComObject(view); } catch { } view = null; }
        if (folder != null) { try { Marshal.ReleaseComObject(folder); } catch { } folder = null; }
        ListView = DefView = IntPtr.Zero;
    }

    public void Dispose() => Detach();

    /// <summary>The desktop's view as an IShellView, for its background menu.</summary>
    public IShellView? ShellView => view as IShellView;

    /// <summary>The size Explorer draws desktop icons at (View, Large/Medium/Small, or Ctrl+wheel), in pixels.</summary>
    public int IconSize
    {
        get
        {
            try { if (view == null) return 0; view.GetViewModeAndIconSize(out _, out int size); return size; }
            catch (Exception ex) when (ex is COMException or InvalidCastException) { return 0; }
        }
    }

    /// <summary>
    /// Has Explorer save the icon positions it holds now. Moves made through list-view messages are only in its
    /// list until it saves; a Refresh (or a restart) reloads what was saved, undoing them.
    /// </summary>
    public void SaveState()
    {
        try { ShellView?.SaveViewState(); }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { Log.Error(ex); }
    }

    /// <summary>Ends an in-place rename Explorer started (after "New folder" it waits for a name in its own list).</summary>
    public void CancelEdit() { if (ListView != IntPtr.Zero) Send(0x10B3 /* LVM_CANCELEDITLABEL */, 0, 0); }

    /// <summary>The desktop's shell folder (user and public desktops merged), for context menus and drags.</summary>
    public IShellFolder? Folder => folder;

    /// <summary>Every desktop icon with its parsing name and list-view position.</summary>
    public List<DesktopItem> Items()
    {
        var list = new List<DesktopItem>();
        if (view == null || folder == null) return list;
        try
        {
            view.ItemCount(2 /* SVGIO_ALLVIEW */, out int n);
            for (int i = 0; i < n; i++)
            {
                view.Item(i, out var pidl);
                try
                {
                    view.GetItemPosition(pidl, out var pt);
                    list.Add(new DesktopItem(NameOf(pidl), i, new Pt(pt.X, pt.Y)));
                }
                finally { Shell32.CoTaskMemFree(pidl); }
            }
        }
        catch (COMException ex) { Log.Error(ex); }
        return list;
    }

    /// <summary>Child pidls (relative to <see cref="Folder"/>) for the named items. Free them with <see cref="Free"/>.</summary>
    public IntPtr[] Pidls(IEnumerable<string> names)
    {
        var want = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var res = new List<IntPtr>();
        if (view == null) return [];
        view.ItemCount(2, out int n);
        for (int i = 0; i < n && want.Count > 0; i++)
        {
            view.Item(i, out var pidl);
            if (want.Remove(NameOf(pidl))) res.Add(pidl);
            else Shell32.CoTaskMemFree(pidl);
        }
        return [.. res];
    }

    public static void Free(IntPtr[] pidls) { foreach (var p in pidls) Shell32.CoTaskMemFree(p); }

    string NameOf(IntPtr pidl)
    {
        var buf = Marshal.AllocCoTaskMem(272);
        try
        {
            folder!.GetDisplayNameOf(pidl, 0x8000 /* SHGDN_FORPARSING */, buf);
            var sb = new StringBuilder(1024);
            Shell32.StrRetToBufW(buf, pidl, sb, sb.Capacity);
            return sb.ToString();
        }
        finally { Marshal.FreeCoTaskMem(buf); }
    }

    public int Count => ListView == IntPtr.Zero ? 0 : (int)Send(0x1004 /* LVM_GETITEMCOUNT */, 0, 0);

    /// <summary>Positions of every list-view item, fast (no COM), for spotting icons Explorer put back.</summary>
    public Pt[] Positions()
    {
        int n = Count;
        var res = new Pt[n];
        for (int i = 0; i < n; i++) res[i] = PositionOf(i);
        return res;
    }

    public Pt PositionOf(int index)
    {
        if (buffer == IntPtr.Zero) return default;
        Send(0x1010 /* LVM_GETITEMPOSITION */, index, buffer);
        ReadProcessMemory(process, buffer, out Native.POINT p, 8, out _);
        return new Pt(p.X, p.Y);
    }

    /// <summary>
    /// The label Explorer shows for the icon at a list-view index, read with plain list-view messages (no COM), or
    /// null if it cannot be read. Used to check an index still holds the icon it held at the last sync.
    /// </summary>
    public string? Label(int index)
    {
        if (textBuffer == IntPtr.Zero) return null;
        var item = new LVITEMW { iSubItem = 0, pszText = textBuffer + 128, cchTextMax = 511 };
        if (!WriteProcessMemory(process, textBuffer, ref item, Marshal.SizeOf<LVITEMW>(), out _)) return null;
        int len = (int)Send(0x1073 /* LVM_GETITEMTEXTW */, index, textBuffer, 200);
        if (len < 0) return null;
        var bytes = new byte[Math.Min(len, 511) * 2];
        if (bytes.Length > 0 && !ReadProcessMemory(process, textBuffer + 128, bytes, bytes.Length, out _)) return null;
        return System.Text.Encoding.Unicode.GetString(bytes);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct LVITEMW
    {
        public uint mask; public int iItem, iSubItem; public uint state, stateMask;
        public IntPtr pszText; public int cchTextMax, iImage; public IntPtr lParam;
        public int iIndent, iGroupId; public uint cColumns; public IntPtr puColumns, piColFmt; public int iGroup;
    }

    [DllImport("kernel32.dll")] static extern bool WriteProcessMemory(IntPtr p, IntPtr a, ref LVITEMW b, int size, out IntPtr written);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr p, IntPtr a, byte[] b, int size, out IntPtr read);

    /// <summary>List-view indices of the selected icons (the ones a drag carries).</summary>
    public HashSet<int> SelectedIndices()
    {
        var res = new HashSet<int>();
        if (ListView == IntPtr.Zero) return res;
        int i = -1;
        for (int guard = 0; guard < 10000; guard++)
        {
            i = (int)Send(0x100C /* LVM_GETNEXTITEM */, i, (IntPtr)2 /* LVNI_SELECTED */);
            if (i < 0) break;
            res.Add(i);
        }
        return res;
    }

    /// <summary>
    /// Like <see cref="Move"/> but does not wait for Explorer: the move is queued and applied as soon as Explorer's
    /// thread is free (while it handles a drop it answers nothing, so waiting on each move costs hundreds of ms).
    /// </summary>
    public void PostMove(int index, Pt p)
    {
        if (ListView != IntPtr.Zero)
            Native.PostMessage(ListView, 0x100F /* LVM_SETITEMPOSITION */, (IntPtr)index, (IntPtr)(((p.Y & 0xFFFF) << 16) | (p.X & 0xFFFF)));
    }

    public void Move(int index, Pt p) =>
        Send(0x100F /* LVM_SETITEMPOSITION */, index, (IntPtr)(((p.Y & 0xFFFF) << 16) | (p.X & 0xFFFF)));

    /// <summary>The item's bounds (icon and label) in list-view client coordinates, or null.</summary>
    public Rectangle? Bounds(int index)
    {
        if (buffer == IntPtr.Zero) return null;
        var r = new Native.RECT { Left = 0 /* LVIR_BOUNDS */ };
        WriteProcessMemory(process, buffer, ref r, 16, out _);
        if (Send(0x100E /* LVM_GETITEMRECT */, index, buffer) == IntPtr.Zero) return null;
        ReadProcessMemory(process, buffer, out r, 16, out _);
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    /// <summary>One of the item's rectangles (0 bounds, 1 icon, 2 label) in list-view client coordinates, or null.</summary>
    public Rectangle? ItemRect(int index, int code)
    {
        if (buffer == IntPtr.Zero) return null;
        var r = new Native.RECT { Left = code };
        WriteProcessMemory(process, buffer, ref r, 16, out _);
        if (Send(0x100E /* LVM_GETITEMRECT */, index, buffer) == IntPtr.Zero) return null;
        ReadProcessMemory(process, buffer, out r, 16, out _);
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    /// <summary>The item under a screen point, or -1 (empty desktop), using the list view's own hit test.</summary>
    public int HitTest(Point screen)
    {
        if (buffer == IntPtr.Zero) return -1;
        var p = new Native.POINT { X = screen.X, Y = screen.Y };
        Native.ScreenToClient(ListView, ref p);
        var info = new LVHITTESTINFO { pt = p, iItem = -1 };
        WriteProcessMemory(process, buffer, ref info, Marshal.SizeOf<LVHITTESTINFO>(), out _);
        var r = Send(0x1012 /* LVM_HITTEST */, 0, buffer, 100);
        return r == (IntPtr)(-2) ? -1 : (int)r; // -2: the call timed out
    }

    /// <summary>True while an icon's name is being edited on the desktop.</summary>
    public bool Renaming => ListView != IntPtr.Zero && Native.FindWindowEx(ListView, IntPtr.Zero, "Edit", null) is var e && e != IntPtr.Zero && Native.IsWindowVisible(e);

    /// <summary>The list view's top-left on screen; item positions are relative to it.</summary>
    public Point Origin
    {
        get
        {
            var p = new Native.POINT();
            ClientToScreen(ListView, ref p);
            return new Point(p.X, p.Y);
        }
    }

    public const uint FWF_AUTOARRANGE = 0x1, FWF_SNAPTOGRID = 0x4;

    public uint FolderFlags
    {
        get { uint f = 0; try { view?.GetCurrentFolderFlags(out f); } catch (COMException) { } return f; }
    }

    public void SetFolderFlags(uint mask, uint flags)
    {
        try { view?.SetCurrentFolderFlags(mask, flags); } catch (COMException ex) { Log.Error(ex); }
    }

    IntPtr Send(uint msg, int w, IntPtr l, uint timeout = 1000) =>
        Native.SendMessageTimeout(ListView, msg, (IntPtr)w, l, 0x2 /* SMTO_ABORTIFHUNG */, timeout, out var r) ? r : (IntPtr)(-2);
    IntPtr Send(uint msg, int w, int l) => Send(msg, w, (IntPtr)l);

    [StructLayout(LayoutKind.Sequential)] struct LVHITTESTINFO { public Native.POINT pt; public uint flags; public int iItem, iSubItem, iGroup; }

    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(int access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern IntPtr VirtualAllocEx(IntPtr p, IntPtr a, int size, int type, int protect);
    [DllImport("kernel32.dll")] static extern bool VirtualFreeEx(IntPtr p, IntPtr a, int size, int type);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr p, IntPtr a, out Native.POINT b, int size, out IntPtr read);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr p, IntPtr a, out Native.RECT b, int size, out IntPtr read);
    [DllImport("kernel32.dll")] static extern bool WriteProcessMemory(IntPtr p, IntPtr a, ref Native.RECT b, int size, out IntPtr written);
    [DllImport("kernel32.dll")] static extern bool WriteProcessMemory(IntPtr p, IntPtr a, ref LVHITTESTINFO b, int size, out IntPtr written);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd, ref Native.POINT p);
}
