using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Shelves.Desktop;

/// <summary>A set of shell items that share one parent folder, as the shell's own menus and drags need them.</summary>
sealed class ItemGroup : IDisposable
{
    public IShellFolder Folder { get; }
    public IntPtr[] Pidls { get; }
    readonly IntPtr[] owned;
    readonly bool releaseFolder;

    ItemGroup(IShellFolder folder, IntPtr[] pidls, IntPtr[] owned, bool releaseFolder)
    {
        Folder = folder; Pidls = pidls; this.owned = owned; this.releaseFolder = releaseFolder;
    }

    /// <summary>Desktop items: children of the merged desktop folder, so user and public desktop items mix.</summary>
    public static ItemGroup? FromDesktop(DesktopView view, IEnumerable<string> names)
    {
        if (view.Folder == null) return null;
        var p = view.Pidls(names);
        return p.Length == 0 ? null : new ItemGroup(view.Folder, p, p, false);
    }

    /// <summary>Files in one folder (a portal), by full path.</summary>
    public static ItemGroup? FromPaths(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return null;
        var abs = new List<IntPtr>();
        IShellFolder? parent = null;
        var children = new List<IntPtr>();
        foreach (var path in paths)
        {
            if (Shell32.SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _) != 0) continue;
            abs.Add(pidl);
            Guid iid = Shell32.IID_IShellFolder;
            if (Shell32.SHBindToParent(pidl, ref iid, out var f, out var child) != 0) continue;
            parent ??= (IShellFolder)f;
            children.Add(child); // points inside pidl, freed with it
        }
        if (parent == null || children.Count == 0) { foreach (var a in abs) Shell32.CoTaskMemFree(a); return null; }
        return new ItemGroup(parent, [.. children], [.. abs], true);
    }

    public void Dispose()
    {
        foreach (var p in owned) Shell32.CoTaskMemFree(p);
        if (releaseFolder) try { Marshal.ReleaseComObject(Folder); } catch { }
    }
}

/// <summary>Opening, menus, drags and icons for shell items, all through the shell's own objects.</summary>
static class ShellOps
{
    /// <summary>Opens items the way a double-click on the desktop does (the default verb).</summary>
    public static void Open(IntPtr hwnd, string parsingName)
    {
        if (Shell32.SHParseDisplayName(parsingName, IntPtr.Zero, out var pidl, 0, out _) != 0) return;
        try
        {
            var info = new Shell32.SHELLEXECUTEINFO
            {
                cbSize = Marshal.SizeOf<Shell32.SHELLEXECUTEINFO>(),
                fMask = 0x4 /* SEE_MASK_IDLIST */ | 0x400 /* SEE_MASK_FLAG_NO_UI off? keep errors */ & 0x4,
                hwnd = hwnd,
                lpIDList = pidl,
                nShow = 1,
            };
            Shell32.ShellExecuteEx(ref info);
        }
        finally { Shell32.CoTaskMemFree(pidl); }
    }

    static IContextMenu2? menu2;
    static IContextMenu3? menu3;

    /// <summary>Forward menu messages while a shell context menu is open, so "Send to" and "Open with" fill in.</summary>
    public static bool HandleMenuMessage(ref Message m)
    {
        if (m.Msg is not (0x117 /* WM_INITMENUPOPUP */ or 0x2B /* WM_DRAWITEM */ or 0x2C /* WM_MEASUREITEM */ or 0x120 /* WM_MENUCHAR */))
            return false;
        if (m.Msg == 0x117 && (menu2 != null || menu3 != null))
        {
            // A submenu (New, Send to) fills in now: let the handler fill it, then fix its icons too.
            bool handled = menu3 != null ? menu3.HandleMenuMsg2((uint)m.Msg, m.WParam, m.LParam, out _) == 0
                                         : menu2!.HandleMenuMsg((uint)m.Msg, m.WParam, m.LParam) == 0;
            FixMenuBitmaps(m.WParam, deep: false);
            m.Result = IntPtr.Zero;
            return handled;
        }
        if (menu3 != null)
        {
            if (menu3.HandleMenuMsg2((uint)m.Msg, m.WParam, m.LParam, out var r) == 0) { m.Result = r; return true; }
        }
        else if (menu2 != null && menu2.HandleMenuMsg((uint)m.Msg, m.WParam, m.LParam) == 0)
        {
            m.Result = IntPtr.Zero;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Shows the shell's context menu for the items at a screen point and runs the choice. Returns the verb when the
    /// caller should handle it itself ("rename"), otherwise null.
    /// </summary>
    public static string? ContextMenu(IntPtr hwnd, ItemGroup items, Point screen, bool extended)
    {
        Guid iid = Shell32.IID_IContextMenu;
        items.Folder.GetUIObjectOf(hwnd, (uint)items.Pidls.Length, items.Pidls, ref iid, IntPtr.Zero, out var obj);
        return RunMenu(hwnd, obj, screen, 0x10 /* CMF_CANRENAME */ | (extended ? 0x100u /* CMF_EXTENDEDVERBS */ : 0), out _, out _);
    }

    /// <summary>
    /// The desktop's own background menu (View, Sort by, Refresh, Paste, New, Display settings, Personalize and
    /// any entries added to it), taken from Explorer's desktop view and shown at a screen point. Returns the verb run.
    /// </summary>
    public static string? BackgroundMenu(IntPtr hwnd, DesktopView view, Point screen, bool extended, out bool topLevel)
    {
        topLevel = false;
        if (view.ShellView is not { } sv) return null;
        Guid iid = Shell32.IID_IContextMenu;
        try
        {
            sv.GetItemObject(0 /* SVGIO_BACKGROUND */, ref iid, out var obj);
            var verb = RunMenu(hwnd, obj, screen, extended ? 0x100u : 0, out int cmd, out var top);
            topLevel = cmd > 0 && top.Contains(cmd);
            return verb;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { Log.Error(ex); return null; }
    }

    /// <summary>Runs a verb from the desktop's background menu ("paste", "refresh") without showing it.</summary>
    public static void BackgroundVerb(IntPtr hwnd, DesktopView view, string verb)
    {
        if (view.ShellView is not { } sv) return;
        Guid iid = Shell32.IID_IContextMenu;
        try
        {
            sv.GetItemObject(0, ref iid, out var obj);
            InvokeVerb(hwnd, (IContextMenu)obj, verb, 0);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { Log.Error(ex); }
    }

    /// <summary>Shows a shell menu and runs the choice; <paramref name="cmd"/> is the chosen id, <paramref name="topLevel"/> the ids not in a submenu.</summary>
    static string? RunMenu(IntPtr hwnd, object obj, Point screen, uint flags, out int cmd, out HashSet<int> topLevel)
    {
        cmd = 0;
        topLevel = [];
        var cm = (IContextMenu)obj;
        menu2 = obj as IContextMenu2;
        menu3 = obj as IContextMenu3;
        var hmenu = Shell32.CreatePopupMenu();
        try
        {
            const uint first = 1, last = 0x7FFF;
            if (cm.QueryContextMenu(hmenu, 0, first, last, flags) < 0) return null;
            FixMenuBitmaps(hmenu, deep: true);
            for (int i = 0; i < GetMenuItemCount(hmenu); i++) topLevel.Add((int)GetMenuItemID(hmenu, i));
            cmd = Shell32.TrackPopupMenuEx(hmenu, 0x100 /* TPM_RETURNCMD */ | 0x2 /* TPM_RIGHTBUTTON */, screen.X, screen.Y, hwnd, IntPtr.Zero);
            if (cmd <= 0) return null;
            string? verb = Verb(cm, (uint)(cmd - first));
            if (verb is "rename") return verb;
            var info = new CMINVOKECOMMANDINFOEX
            {
                cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                fMask = 0x4000 /* CMIC_MASK_UNICODE */ | 0x20000000 /* CMIC_MASK_PTINVOKE */,
                hwnd = hwnd,
                lpVerb = (IntPtr)(cmd - first),
                lpVerbW = (IntPtr)(cmd - first),
                nShow = 1,
                ptInvoke = new Native.POINT { X = screen.X, Y = screen.Y },
            };
            cm.InvokeCommand(ref info);
            return verb;
        }
        finally
        {
            Shell32.DestroyMenu(hmenu);
            foreach (var b in menuBitmaps) Native.DeleteObject(b);
            menuBitmaps.Clear();
            menu2 = null; menu3 = null;
            Marshal.ReleaseComObject(cm);
        }
    }

    static readonly List<IntPtr> menuBitmaps = [];

    /// <summary>For checking (--menu-icons): builds the desktop's background menu, fixes its icons, saves each as a PNG.</summary>
    public static unsafe void DumpBackgroundMenuIcons(DesktopView view, string dir)
    {
        Guid iid = Shell32.IID_IContextMenu;
        view.ShellView!.GetItemObject(0, ref iid, out var obj);
        var cm = (IContextMenu)obj;
        var hmenu = Shell32.CreatePopupMenu();
        cm.QueryContextMenu(hmenu, 0, 1, 0x7FFF, 0);
        FixMenuBitmaps(hmenu, deep: true);
        Directory.CreateDirectory(dir);
        Dump(hmenu, "item");
        // Submenus fill in when opened (New): ask the menu to fill each, then fix and save those too.
        for (int i = 0; i < GetMenuItemCount(hmenu); i++)
        {
            var sm = new MENUITEMINFO { cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(), fMask = 0x4 };
            GetMenuItemInfo(hmenu, (uint)i, true, ref sm);
            if (sm.hSubMenu == IntPtr.Zero) continue;
            if (obj is IContextMenu3 c3) c3.HandleMenuMsg2(0x117, sm.hSubMenu, (IntPtr)i, out _);
            else if (obj is IContextMenu2 c2) c2.HandleMenuMsg(0x117, sm.hSubMenu, (IntPtr)i);
            FixMenuBitmaps(sm.hSubMenu, deep: false);
            Dump(sm.hSubMenu, $"sub{i}-");
        }
        Shell32.DestroyMenu(hmenu);

        void Dump(IntPtr menu, string prefix)
        {
        for (int i = 0; i < GetMenuItemCount(menu); i++)
        {
            var mii = new MENUITEMINFO { cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(), fMask = 0x80 | 0x100 /* FTYPE */ | 0x20 /* DATA */ };
            GetMenuItemInfo(menu, (uint)i, true, ref mii);
            if ((long)mii.hbmpItem is >= -1 and <= 11) continue;
            if (Shell32.GetObject(mii.hbmpItem, Marshal.SizeOf<Shell32.DIBSECTION>(), out var ds) == 0 || ds.dsBm.bmBits == IntPtr.Zero) continue;
            int w = ds.dsBm.bmWidth, h = ds.dsBm.bmHeight;
            using var bmp = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, ds.dsBm.bmBits);
            // Scaled up on a mid grey so the edges are easy to judge.
            using var big = new Bitmap(w * 8, h * 8);
            using (var g = Graphics.FromImage(big))
            {
                g.Clear(Color.FromArgb(200, 200, 200));
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(bmp, 0, 0, w * 8, h * 8);
            }
            big.Save(Path.Combine(dir, $"{prefix}{i}.png"));
        }
        }
    }

    /// <summary>
    /// Menu icons made in Explorer (the desktop's background menu comes from there) lose their transparency when
    /// the menu is shown by another program, and draw on black. Each is copied into a bitmap of our own with its
    /// alpha kept, which menus draw properly.
    /// </summary>
    static unsafe void FixMenuBitmaps(IntPtr hmenu, bool deep)
    {
        int n = GetMenuItemCount(hmenu);
        for (int i = 0; i < n; i++)
        {
            var text = new char[260];
            var mii = new MENUITEMINFO { cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(), fMask = 0x80 /* MIIM_BITMAP */ | 0x4 /* MIIM_SUBMENU */ | 0x40 /* MIIM_STRING */ };
            var h = GCHandle.Alloc(text, GCHandleType.Pinned);
            try
            {
                mii.dwTypeData = h.AddrOfPinnedObject();
                mii.cch = (uint)text.Length - 1;
                if (!GetMenuItemInfo(hmenu, (uint)i, true, ref mii)) continue;
            }
            finally { h.Free(); }
            if (deep && mii.hSubMenu != IntPtr.Zero) FixMenuBitmaps(mii.hSubMenu, true);
            string label = new string(text, 0, (int)Math.Min(mii.cch, (uint)text.Length)).Replace("&", "");
            IntPtr copy;
            if (mii.hbmpItem == (IntPtr)(-1) /* HBMMENU_CALLBACK */)
            {
                // Drawn by Explorer on request (the New submenu), which cannot reach across to this program's
                // menu: the entry's file type gives the icon instead; the first entry is Folder.
                copy = NewItemIcon(label, first: i == 0);
            }
            else
            {
                // Small values are the system's own glyphs (HBMMENU_*), not bitmaps.
                if ((long)mii.hbmpItem is >= -1 and <= 11) continue;
                if (menuBitmaps.Contains(mii.hbmpItem)) continue;
                copy = VerbIconBitmap(label, mii.hbmpItem) is var v && v != IntPtr.Zero ? v : AlphaCopy(mii.hbmpItem);
                // A bitmap this program cannot even read (the New submenu's, made inside Explorer): by file type.
                if (copy == IntPtr.Zero && Shell32.GetObject(mii.hbmpItem, Marshal.SizeOf<Shell32.DIBSECTION>(), out _) == 0)
                    copy = NewItemIcon(label, first: i == 0 && IsNewMenu(hmenu));
            }
            if (copy == IntPtr.Zero) continue;
            menuBitmaps.Add(copy);
            var set = new MENUITEMINFO { cbSize = mii.cbSize, fMask = 0x80, hbmpItem = copy };
            SetMenuItemInfo(hmenu, (uint)i, true, ref set);
        }
    }

    /// <summary>A premultiplied 32-bit copy of a bitmap, or zero when it has no alpha to keep.</summary>
    static unsafe IntPtr AlphaCopy(IntPtr hbmp)
    {
        int got = Shell32.GetObject(hbmp, Marshal.SizeOf<Shell32.DIBSECTION>(), out var ds);
        if (got == 0) return IntPtr.Zero;
        int w = ds.dsBm.bmWidth, h = ds.dsBm.bmHeight;
        if (w <= 0 || h <= 0 || w > 256 || h > 256) return IntPtr.Zero;
        var pixels = new uint[w * h];
        var bi = new Shell32.BITMAPINFOHEADER { biSize = (uint)Marshal.SizeOf<Shell32.BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var screen = Native.GetDC(IntPtr.Zero);
        try
        {
            fixed (uint* p = pixels)
                if (GetDIBits(screen, hbmp, 0, (uint)h, (IntPtr)p, ref bi, 0) == 0) return IntPtr.Zero;
        }
        finally { Native.ReleaseDC(IntPtr.Zero, screen); }
        bool alpha = false, straight = false;
        foreach (var px in pixels)
        {
            uint a = px >> 24;
            if (a != 0) alpha = true;
            if (((px >> 16) & 255) > a || ((px >> 8) & 255) > a || (px & 255) > a) straight = true;
        }
        // No alpha at all (an old-style bitmap, as the New submenu's are when they reach us): opaque, and the same
        // flattened-onto-black check below applies.
        if (!alpha)
        {
            if (pixels.All(px => (px & 0xFFFFFF) == 0)) return IntPtr.Zero; // nothing readable
            for (int k = 0; k < pixels.Length; k++) pixels[k] |= 0xFF000000;
        }
        // Every pixel opaque and the corners black: the icon was flattened onto black on its way here (menus from
        // Explorer). Black reached from the edges becomes transparent, and the dark rim next to it fades by its
        // brightness, which undoes the blend for anti-aliased edges.
        if (pixels.All(px => px >> 24 == 255) && (pixels[0] & 0xFFFFFF) == 0 && (pixels[^1] & 0xFFFFFF) == 0)
        {
            ClearEdgeBlack(pixels, w, h);
            return ToDib(pixels, w, h);
        }
        if (straight)
            for (int k = 0; k < pixels.Length; k++)
            {
                uint px = pixels[k], a = px >> 24;
                uint r = ((px >> 16) & 255) * a / 255, g = ((px >> 8) & 255) * a / 255, b = (px & 255) * a / 255;
                pixels[k] = (a << 24) | (r << 16) | (g << 8) | b;
            }
        return ToDib(pixels, w, h);
    }

    /// <summary>A top-down 32-bit DIB section holding premultiplied pixels.</summary>
    static unsafe IntPtr ToDib(uint[] pixels, int w, int h)
    {
        var bi = new Shell32.BITMAPINFOHEADER { biSize = (uint)Marshal.SizeOf<Shell32.BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var dib = CreateDIBSection(IntPtr.Zero, ref bi, 0, out var bits, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero) return IntPtr.Zero;
        fixed (uint* src = pixels) Buffer.MemoryCopy(src, (void*)bits, pixels.Length * 4L, pixels.Length * 4L);
        return dib;
    }

    static void ClearEdgeBlack(uint[] px, int w, int h)
    {
        static int Max(uint p) => (int)Math.Max((p >> 16) & 255, Math.Max((p >> 8) & 255, p & 255));
        var clear = new bool[px.Length];
        var stack = new Stack<int>();
        for (int x = 0; x < w; x++) { stack.Push(x); stack.Push((h - 1) * w + x); }
        for (int y = 0; y < h; y++) { stack.Push(y * w); stack.Push(y * w + w - 1); }
        while (stack.Count > 0)
        {
            int k = stack.Pop();
            if (clear[k] || Max(px[k]) > 8) continue;
            clear[k] = true;
            int x = k % w, y = k / w;
            if (x > 0) stack.Push(k - 1);
            if (x < w - 1) stack.Push(k + 1);
            if (y > 0) stack.Push(k - w);
            if (y < h - 1) stack.Push(k + w);
        }
        for (int k = 0; k < px.Length; k++)
        {
            if (clear[k]) { px[k] = 0; continue; }
            int x = k % w, y = k / w;
            bool rim = (x > 0 && clear[k - 1]) || (x < w - 1 && clear[k + 1]) || (y > 0 && clear[k - w]) || (y < h - 1 && clear[k + w]);
            // Blended over black, a rim pixel's colour is already its premultiplied value; its alpha is its brightness.
            if (rim) px[k] = ((uint)Max(px[k]) << 24) | (px[k] & 0xFFFFFF);
        }
    }

    static Dictionary<string, string>? verbIcons;

    /// <summary>
    /// The desktop menu's own entries (Display settings, Personalize, New shelf, entries apps add) are registry
    /// verbs with an Icon value: their real icon, at the menu bitmap's size, beats one rebuilt from a flattened copy.
    /// </summary>
    static IntPtr VerbIconBitmap(string label, IntPtr like)
    {
        verbIcons ??= ReadVerbIcons();
        if (!verbIcons.TryGetValue(label.Trim(), out var loc)) return IntPtr.Zero;
        int size = Shell32.GetObject(like, Marshal.SizeOf<Shell32.DIBSECTION>(), out var ds) != 0 ? ds.dsBm.bmWidth : 16;
        string file = Environment.ExpandEnvironmentVariables(loc.Trim('"'));
        int index = 0;
        int comma = file.LastIndexOf(',');
        if (comma > 1 && int.TryParse(file[(comma + 1)..].Trim(), out var ix)) { index = ix; file = file[..comma].Trim('"', ' '); }
        if (SHDefExtractIcon(file, index, 0, out var big, out _, (uint)size) != 0 || big == IntPtr.Zero) return IntPtr.Zero;
        try { return IconDib(big, size); }
        finally { DestroyIcon(big); }
    }

    /// <summary>An icon drawn into a premultiplied DIB of <paramref name="size"/> pixels, for a menu.</summary>
    static IntPtr IconDib(IntPtr hicon, int size)
    {
        {
            using var ic = System.Drawing.Icon.FromHandle(hicon);
            using var bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawIcon(ic, new Rectangle(0, 0, size, size));
            }
            var data = bmp.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                var pixels = new uint[size * size];
                unsafe { fixed (uint* d = pixels) for (int y = 0; y < size; y++) Buffer.MemoryCopy((byte*)data.Scan0 + y * data.Stride, d + y * size, size * 4L, size * 4L); }
                return ToDib(pixels, size, size);
            }
            finally { bmp.UnlockBits(data); }
        }
    }

    static Dictionary<string, string>? newTypes;

    /// <summary>True for the New submenu: it holds the Shortcut entry (by its type name), which always comes second.</summary>
    static bool IsNewMenu(IntPtr hmenu)
    {
        var fi = new SHFILEINFO();
        if (SHGetFileInfo("x.lnk", 0x80, ref fi, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x400 | 0x10) == IntPtr.Zero) return false;
        var sb = new System.Text.StringBuilder(260);
        return GetMenuString(hmenu, 1, sb, sb.Capacity, 0x400 /* MF_BYPOSITION */) > 0
               && string.Equals(sb.ToString().Replace("&", "").Trim(), fi.szTypeName, StringComparison.CurrentCultureIgnoreCase);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetMenuString(IntPtr m, uint id, System.Text.StringBuilder s, int n, uint flags);

    /// <summary>The icon for an entry of the New submenu: its file type's icon, by the type's name.</summary>
    static IntPtr NewItemIcon(string label, bool first)
    {
        int size = Native.GetSystemMetrics(49 /* SM_CXSMICON */);
        newTypes ??= ReadNewTypes();
        IntPtr hicon = IntPtr.Zero;
        if (newTypes.TryGetValue(label.Trim(), out var ext))
        {
            var fi = new SHFILEINFO();
            if (SHGetFileInfo("x" + ext, 0x80 /* NORMAL */, ref fi, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x100 | 0x1 | 0x10 /* ICON | SMALLICON | USEFILEATTRIBUTES */) != IntPtr.Zero)
                hicon = fi.hIcon;
        }
        else if (first)
        {
            var info = new SHSTOCKICONINFO { cbSize = (uint)Marshal.SizeOf<SHSTOCKICONINFO>() };
            if (SHGetStockIconInfo(3 /* SIID_FOLDER */, 0x100 | 0x1 /* ICON | SMALLICON */, ref info) == 0) hicon = info.hIcon;
        }
        if (hicon == IntPtr.Zero) return IntPtr.Zero;
        try { return IconDib(hicon, size); }
        finally { DestroyIcon(hicon); }
    }

    /// <summary>Type name to extension for every type that offers a New entry (a ShellNew key), read once.</summary>
    static Dictionary<string, string> ReadNewTypes()
    {
        var res = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
        var later = new List<string>();
        try
        {
            var root = Microsoft.Win32.Registry.ClassesRoot;
            foreach (var ext in root.GetSubKeyNames())
            {
                if (ext.Length < 2 || ext[0] != '.') continue;
                using var k = root.OpenSubKey(ext);
                if (k == null) continue;
                bool hasNew = k.OpenSubKey("ShellNew") is { } sn && Close(sn);
                if (!hasNew && k.GetValue(null) is string progId && progId.Length > 0)
                    hasNew = k.OpenSubKey(progId + @"\ShellNew") is { } psn && Close(psn);
                // Types without a ShellNew key can still be in New: packaged apps (Notepad, Paint) add theirs
                // their own way. Their names are added after the rest, so a ShellNew type wins a shared name.
                if (!hasNew) { later.Add(ext); continue; }
                var fi = new SHFILEINFO();
                if (SHGetFileInfo("x" + ext, 0x80, ref fi, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x400 | 0x10 /* TYPENAME | USEFILEATTRIBUTES */) != IntPtr.Zero
                    && fi.szTypeName.Length > 0)
                    res.TryAdd(fi.szTypeName, ext);
                // The New menu names a type by its class's own name, which can differ from the name above when
                // another app now opens the type: add the class's names too.
                foreach (var cls in new[] { k.GetValue(null) as string, (k.OpenSubKey("ShellNew") is { } sn2 ? Take(sn2, "ItemName") : null) })
                {
                    if (string.IsNullOrEmpty(cls)) continue;
                    if (cls.StartsWith('@')) { if (Indirect(cls) is { } t) res.TryAdd(t, ext); continue; }
                    using var ck = root.OpenSubKey(cls);
                    if (ck == null) continue;
                    if (ck.GetValue("FriendlyTypeName") is string ftn && Indirect(ftn) is { } f) res.TryAdd(f, ext);
                    if (ck.GetValue(null) is string dn && dn.Length > 0) res.TryAdd(dn, ext);
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        foreach (var ext in later)
        {
            var fi = new SHFILEINFO();
            if (SHGetFileInfo("x" + ext, 0x80, ref fi, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x400 | 0x10) != IntPtr.Zero && fi.szTypeName.Length > 0)
                res.TryAdd(fi.szTypeName, ext);
        }
        // Paint's own New entry, whose name no file type carries.
        res.TryAdd("Bitmap image", ".bmp");
        return res;

        static bool Close(Microsoft.Win32.RegistryKey key) { key.Dispose(); return true; }
        static string? Take(Microsoft.Win32.RegistryKey key, string name) { using (key) return key.GetValue(name) as string; }
        static string? Indirect(string v)
        {
            if (!v.StartsWith('@')) return v;
            var sb = new System.Text.StringBuilder(260);
            return SHLoadIndirectString(v, sb, sb.Capacity, IntPtr.Zero) == 0 ? sb.ToString() : null;
        }
    }

    /// <summary>Reads the New types in the background at startup, so the first New menu opens at once.</summary>
    public static void WarmNewTypes() => Task.Run(() => { try { newTypes ??= ReadNewTypes(); } catch { } });

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO
    {
        public IntPtr hIcon; public int iIcon; public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path, uint attrs, ref SHFILEINFO fi, uint size, uint flags);

    static Dictionary<string, string> ReadVerbIcons()
    {
        var res = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var path in new[] { @"DesktopBackground\Shell", @"Directory\Background\shell" })
        {
            try
            {
                using var root = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(path);
                if (root == null) continue;
                foreach (var name in root.GetSubKeyNames())
                {
                    using var k = root.OpenSubKey(name);
                    if (k?.GetValue("Icon") is not string icon || icon.Length == 0) continue;
                    var text = (k.GetValue("MUIVerb") as string) ?? (k.GetValue(null) as string);
                    if (string.IsNullOrEmpty(text)) continue;
                    if (text.StartsWith('@'))
                    {
                        var sb = new System.Text.StringBuilder(260);
                        if (SHLoadIndirectString(text, sb, sb.Capacity, IntPtr.Zero) != 0) continue;
                        text = sb.ToString();
                    }
                    res.TryAdd(text.Replace("&", "").Trim(), icon);
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return res;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] static extern int SHLoadIndirectString(string src, System.Text.StringBuilder outBuf, int cch, IntPtr reserved);

    [StructLayout(LayoutKind.Sequential)]
    struct MENUITEMINFO
    {
        public uint cbSize, fMask, fType, fState, wID;
        public IntPtr hSubMenu, hbmpChecked, hbmpUnchecked, dwItemData, dwTypeData;
        public uint cch;
        public IntPtr hbmpItem;
    }

    [DllImport("user32.dll")] static extern int GetMenuItemCount(IntPtr m);
    [DllImport("user32.dll")] static extern uint GetMenuItemID(IntPtr m, int pos);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMenuItemInfo(IntPtr m, uint item, bool byPos, ref MENUITEMINFO mii);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetMenuItemInfo(IntPtr m, uint item, bool byPos, ref MENUITEMINFO mii);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, IntPtr bits, ref Shell32.BITMAPINFOHEADER bi, uint usage);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref Shell32.BITMAPINFOHEADER bi, uint usage, out IntPtr bits, IntPtr section, uint offset);

    /// <summary>Runs one verb ("delete", "properties", ...) on the items without showing a menu.</summary>
    public static void Invoke(IntPtr hwnd, ItemGroup items, string verb, bool shift = false)
    {
        Guid iid = Shell32.IID_IContextMenu;
        items.Folder.GetUIObjectOf(hwnd, (uint)items.Pidls.Length, items.Pidls, ref iid, IntPtr.Zero, out var obj);
        InvokeVerb(hwnd, (IContextMenu)obj, verb, shift ? 0x10000000u /* CMIC_MASK_SHIFT_DOWN */ : 0);
    }

    static void InvokeVerb(IntPtr hwnd, IContextMenu cm, string verb, uint mask)
    {
        var hmenu = Shell32.CreatePopupMenu();
        var ansi = Marshal.StringToHGlobalAnsi(verb);
        var wide = Marshal.StringToHGlobalUni(verb);
        try
        {
            cm.QueryContextMenu(hmenu, 0, 1, 0x7FFF, 0);
            var info = new CMINVOKECOMMANDINFOEX
            {
                cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                fMask = 0x4000 | mask,
                hwnd = hwnd,
                lpVerb = ansi,
                lpVerbW = wide,
                nShow = 1,
            };
            cm.InvokeCommand(ref info);
        }
        finally
        {
            Marshal.FreeHGlobal(ansi);
            Marshal.FreeHGlobal(wide);
            Shell32.DestroyMenu(hmenu);
            Marshal.ReleaseComObject(cm);
        }
    }

    static string? Verb(IContextMenu cm, uint id)
    {
        var buf = Marshal.AllocHGlobal(512);
        try
        {
            return cm.GetCommandString((UIntPtr)id, 4 /* GCS_VERBW */, IntPtr.Zero, buf, 256) == 0 ? Marshal.PtrToStringUni(buf) : null;
        }
        catch { return null; }
        finally { Marshal.FreeHGlobal(buf); }
    }

    /// <summary>
    /// The shell's drop target for an item (a folder, the Recycle Bin, a program) or, for a folder path, the
    /// folder itself: files dropped through it are moved, copied, linked or opened exactly as Explorer would.
    /// </summary>
    public static IShellDropTarget? DropTarget(IntPtr hwnd, string parsingName)
    {
        if (Shell32.SHParseDisplayName(parsingName, IntPtr.Zero, out var pidl, 0, out _) != 0) return null;
        try
        {
            Guid iidf = Shell32.IID_IShellFolder;
            if (Shell32.SHBindToParent(pidl, ref iidf, out var f, out var child) != 0) return null;
            var folder = (IShellFolder)f;
            try
            {
                Guid iid = Shell32.IID_IDropTarget;
                folder.GetUIObjectOf(hwnd, 1, [child], ref iid, IntPtr.Zero, out var dt);
                return dt as IShellDropTarget;
            }
            catch (COMException) { return null; }
            finally { Marshal.ReleaseComObject(folder); }
        }
        finally { Shell32.CoTaskMemFree(pidl); }
    }

    /// <summary>The shell's data object for desktop items, as a drag of them carries.</summary>
    public static ComDataObject? DataObject(IntPtr hwnd, ItemGroup items)
    {
        Guid iid = Shell32.IID_IDataObject;
        try
        {
            items.Folder.GetUIObjectOf(hwnd, (uint)items.Pidls.Length, items.Pidls, ref iid, IntPtr.Zero, out var obj);
            return obj as ComDataObject;
        }
        catch (COMException) { return null; }
    }

    /// <summary>True when the item takes drops (folders, drives, the Recycle Bin, programs), as SFGAO_DROPTARGET says.</summary>
    public static bool IsDropTarget(DesktopView view, string name)
    {
        if (view.Folder == null) return false;
        var p = view.Pidls([name]);
        if (p.Length == 0) return false;
        try
        {
            uint attrs = 0x100 /* SFGAO_DROPTARGET */;
            view.Folder.GetAttributesOf(1, p, ref attrs);
            return (attrs & 0x100) != 0;
        }
        catch (COMException) { return false; }
        finally { DesktopView.Free(p); }
    }

    /// <summary>True for shortcuts, which get the arrow overlay (SFGAO_LINK).</summary>
    public static bool IsLink(string parsingName)
    {
        if (parsingName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || parsingName.EndsWith(".url", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>The shortcut-arrow overlay at <paramref name="px"/> pixels, as Windows draws it over a shortcut's icon.</summary>
    public static Bitmap? LinkOverlay(int px)
    {
        var info = new SHSTOCKICONINFO { cbSize = (uint)Marshal.SizeOf<SHSTOCKICONINFO>() };
        if (SHGetStockIconInfo(29 /* SIID_LINK */, 0x100 /* SHGSI_ICON */ | 0x0 /* large */ , ref info) != 0 || info.hIcon == IntPtr.Zero)
            return null;
        try
        {
            // The overlay icon at the size asked for, from its own resource (it scales badly from 32 px).
            if (SHDefExtractIcon(info.szPath, info.iIcon, 0, out var big, out _, (uint)px) == 0 && big != IntPtr.Zero)
            {
                try { using var ic = System.Drawing.Icon.FromHandle(big); return ic.ToBitmap(); }
                finally { DestroyIcon(big); }
            }
            using var small = System.Drawing.Icon.FromHandle(info.hIcon);
            return small.ToBitmap();
        }
        finally { DestroyIcon(info.hIcon); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHSTOCKICONINFO { public uint cbSize; public IntPtr hIcon; public int iSysImageIndex, iIcon; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szPath; }
    [DllImport("shell32.dll")] static extern int SHGetStockIconInfo(uint id, uint flags, ref SHSTOCKICONINFO info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHDefExtractIcon(string file, int index, uint flags, out IntPtr large, out IntPtr small, uint size);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

    /// <summary>Renames through the shell (it keeps extensions hidden the way Explorer does, and handles conflicts).</summary>
    public static bool Rename(IntPtr hwnd, ItemGroup item, string newName)
    {
        try
        {
            item.Folder.SetNameOf(hwnd, item.Pidls[0], newName, 0 /* SHGDN_NORMAL */, out var np);
            if (np != IntPtr.Zero) Shell32.CoTaskMemFree(np);
            return true;
        }
        catch (COMException) { return false; }
    }

    /// <summary>Drags the items with the shell's own data object, drag image and drop source.</summary>
    public static DragDropEffects Drag(IntPtr hwnd, ItemGroup items)
    {
        Guid iid = Shell32.IID_IDataObject;
        items.Folder.GetUIObjectOf(hwnd, (uint)items.Pidls.Length, items.Pidls, ref iid, IntPtr.Zero, out var obj);
        try
        {
            Shell32.SHDoDragDrop(hwnd, (ComDataObject)obj, IntPtr.Zero, 1 | 2 | 4 /* copy, move, link */, out int effect);
            return (DragDropEffects)effect;
        }
        finally { Marshal.ReleaseComObject(obj); }
    }

    /// <summary>The name as Explorer puts it in its rename box (with the extension when it shows extensions).</summary>
    public static string EditName(string parsingName, string fallback)
    {
        try
        {
            Guid iid = Shell32.IID_IShellItem;
            Shell32.SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var o);
            var si = (IShellItem)o;
            try { si.GetDisplayName(0x31001 /* SIGDN_PARENTRELATIVEEDITING */, out var n); return n; }
            finally { Marshal.ReleaseComObject(si); }
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or FileNotFoundException) { return fallback; }
    }

    /// <summary>The item's name as Explorer shows it (extensions hidden or not, as the user set).</summary>
    public static string DisplayName(string parsingName)
    {
        try
        {
            Guid iid = Shell32.IID_IShellItem;
            Shell32.SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var o);
            var si = (IShellItem)o;
            try { si.GetDisplayName(0 /* SIGDN_NORMALDISPLAY */, out var n); return n; }
            finally { Marshal.ReleaseComObject(si); }
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or FileNotFoundException)
        {
            return Path.GetFileNameWithoutExtension(parsingName);
        }
    }

    /// <summary>The item's icon or thumbnail at <paramref name="px"/> pixels, as Explorer would draw it, or null.</summary>
    public static Bitmap? Icon(string parsingName, int px)
    {
        try
        {
            Guid iid = Shell32.IID_IShellItemImageFactory;
            Shell32.SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var o);
            var f = (IShellItemImageFactory)o;
            try
            {
                // SIIGBF_BIGGERSIZEOK: a sharper bigger image is fine, it is scaled down when drawn.
                if (f.GetImage(new Native.SIZE { CX = px, CY = px }, 0x1, out var hbmp) != 0 || hbmp == IntPtr.Zero) return null;
                try { return FromHBitmap(hbmp); }
                finally { Native.DeleteObject(hbmp); }
            }
            finally { Marshal.ReleaseComObject(f); }
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or FileNotFoundException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Copies a 32-bit DIB section into a bitmap. The shell returns icons with straight (not premultiplied) alpha,
    /// so soft edges keep their full colour; reading them as premultiplied made those edges hard and bright.
    /// A bitmap whose colours never exceed their alpha may be premultiplied (some thumbnails), and is kept as is.
    /// </summary>
    static unsafe Bitmap? FromHBitmap(IntPtr hbmp)
    {
        if (Shell32.GetObject(hbmp, Marshal.SizeOf<Shell32.DIBSECTION>(), out var ds) == 0 || ds.dsBm.bmBits == IntPtr.Zero || ds.dsBm.bmBitsPixel != 32)
            return Image.FromHbitmap(hbmp);
        int w = ds.dsBm.bmWidth, h = ds.dsBm.bmHeight, stride = ds.dsBm.bmWidthBytes;
        bool bottomUp = ds.dsBmih.biHeight > 0;
        bool anyAlpha = false, straight = false;
        for (int y = 0; y < h; y++)
        {
            var src = (uint*)((byte*)ds.dsBm.bmBits + y * stride);
            for (int x = 0; x < w; x++)
            {
                uint p = src[x];
                uint a = p >> 24;
                if (a != 0) anyAlpha = true;
                if (((p >> 16) & 255) > a || ((p >> 8) & 255) > a || (p & 255) > a) straight = true;
            }
        }
        var format = straight || !anyAlpha ? PixelFormat.Format32bppArgb : PixelFormat.Format32bppPArgb;
        var bmp = new Bitmap(w, h, format);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, format);
        try
        {
            for (int y = 0; y < h; y++)
            {
                var src = (uint*)((byte*)ds.dsBm.bmBits + (bottomUp ? h - 1 - y : y) * stride);
                var dst = (uint*)((byte*)data.Scan0 + y * data.Stride);
                // No alpha at all: an old icon drawn opaque.
                for (int x = 0; x < w; x++) dst[x] = anyAlpha ? src[x] : src[x] | 0xFF000000;
            }
        }
        finally { bmp.UnlockBits(data); }
        return bmp;
    }
}
