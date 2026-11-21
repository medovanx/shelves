using System.Runtime.InteropServices;

namespace Shelves.Desktop;

// The shell interfaces Shelves needs, declared in vtable order. Methods Shelves never calls are placeholders
// that only keep the slots right.

[ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
interface IShellWindows
{
    int Count { get; }
    [return: MarshalAs(UnmanagedType.IDispatch)] object Item(object index);
    [return: MarshalAs(UnmanagedType.IUnknown)] object _NewEnum();
    void Register(); void RegisterPending(); void Revoke(); void OnNavigate(); void OnActivated();
    [return: MarshalAs(UnmanagedType.IDispatch)] object FindWindowSW(ref object pvarloc, ref object pvarlocRoot, int swClass, out int phwnd, int swfwOptions);
}

[ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IComServiceProvider { void QueryService(ref Guid sid, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object o); }

[ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellBrowser
{
    void GetWindow(); void ContextSensitiveHelp(); void InsertMenusSB(); void SetMenuSB(); void RemoveMenusSB(); void SetStatusTextSB();
    void EnableModelessSB(); void TranslateAcceleratorSB(); void BrowseObject(); void GetViewStateStream(); void GetControlWindow(); void SendControlMsg();
    void QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object view);
}

[ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellFolder
{
    void ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string name, out uint eaten, out IntPtr pidl, ref uint attrs);
    void EnumObjects(); void BindToObject(); void BindToStorage(); void CompareIDs(); void CreateViewObject();
    void GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint inOut);
    void GetUIObjectOf(IntPtr hwnd, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref Guid riid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    void GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr strret);
    void SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IntPtr newPidl);
}

[ComImport, Guid("1af3a467-214f-4298-908e-06b03e0b39f9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IFolderView2
{
    void GetCurrentViewMode(out uint mode);
    void SetCurrentViewMode(uint mode);
    void GetFolder(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    void Item(int i, out IntPtr pidl);
    void ItemCount(uint flags, out int count);
    void Items(); void GetSelectionMarkedItem(); void GetFocusedItem();
    void GetItemPosition(IntPtr pidl, out Native.POINT pt);
    void GetSpacing(out Native.POINT pt);
    void GetDefaultSpacing(out Native.POINT pt);
    [PreserveSig] int GetAutoArrange();
    void SelectItem(int i, uint flags);
    void SelectAndPositionItems(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, [MarshalAs(UnmanagedType.LPArray)] Native.POINT[] apt, uint flags);
    void SetGroupBy(); void GetGroupBy(); void SetViewProperty(); void GetViewProperty(); void SetTileViewProperties(); void SetExtendedTileViewProperties(); void SetText();
    void SetCurrentFolderFlags(uint mask, uint flags);
    void GetCurrentFolderFlags(out uint flags);
    void GetSortColumnCount(); void SetSortColumns(); void GetSortColumns(); void GetItem(); void GetVisibleItem();
    void GetSelectedItem(); void GetSelection(); void GetSelectionState(); void InvokeVerbOnSelection(); void SetViewModeAndIconSize();
    void GetViewModeAndIconSize(out uint mode, out int size);
}

/// <summary>The desktop's view, for its background menu (the one a right-click on empty desktop shows).</summary>
[ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellView
{
    void GetWindow(); void ContextSensitiveHelp(); void TranslateAccelerator(); void EnableModeless(); void UIActivate(); void Refresh();
    void CreateViewWindow(); void DestroyViewWindow(); void GetCurrentInfo(); void AddPropertySheetPages(); void SaveViewState(); void SelectItem();
    void GetItemObject(uint item, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
}

/// <summary>A shell drop target: a folder, the Recycle Bin, a program (files dropped on it open with it).</summary>
[ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellDropTarget
{
    [PreserveSig] int DragEnter(System.Runtime.InteropServices.ComTypes.IDataObject data, uint keys, Native.POINT pt, ref uint effect);
    [PreserveSig] int DragOver(uint keys, Native.POINT pt, ref uint effect);
    [PreserveSig] int DragLeave();
    [PreserveSig] int Drop(System.Runtime.InteropServices.ComTypes.IDataObject data, uint keys, Native.POINT pt, ref uint effect);
}

[ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellItem
{
    void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    void GetParent(out IShellItem parent);
    void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
    void GetAttributes(uint mask, out uint attrs);
    void Compare(IShellItem other, uint hint, out int order);
}

[ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellItemImageFactory
{
    [PreserveSig] int GetImage(Native.SIZE size, int flags, out IntPtr hbitmap);
}

[ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IContextMenu
{
    [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint index, uint first, uint last, uint flags);
    [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
    [PreserveSig] int GetCommandString(UIntPtr cmd, uint type, IntPtr reserved, IntPtr name, uint cch);
}

[ComImport, Guid("000214f4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IContextMenu2
{
    [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint index, uint first, uint last, uint flags);
    [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
    [PreserveSig] int GetCommandString(UIntPtr cmd, uint type, IntPtr reserved, IntPtr name, uint cch);
    [PreserveSig] int HandleMenuMsg(uint msg, IntPtr w, IntPtr l);
}

[ComImport, Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IContextMenu3
{
    [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint index, uint first, uint last, uint flags);
    [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
    [PreserveSig] int GetCommandString(UIntPtr cmd, uint type, IntPtr reserved, IntPtr name, uint cch);
    [PreserveSig] int HandleMenuMsg(uint msg, IntPtr w, IntPtr l);
    [PreserveSig] int HandleMenuMsg2(uint msg, IntPtr w, IntPtr l, out IntPtr result);
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct CMINVOKECOMMANDINFOEX
{
    public int cbSize;
    public uint fMask;
    public IntPtr hwnd;
    public IntPtr lpVerb;
    [MarshalAs(UnmanagedType.LPStr)] public string? lpParameters;
    [MarshalAs(UnmanagedType.LPStr)] public string? lpDirectory;
    public int nShow;
    public uint dwHotKey;
    public IntPtr hIcon;
    [MarshalAs(UnmanagedType.LPStr)] public string? lpTitle;
    public IntPtr lpVerbW;
    public string? lpParametersW;
    public string? lpDirectoryW;
    public string? lpTitleW;
    public Native.POINT ptInvoke;
}

static class Shell32
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    public static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHParseDisplayName(string name, IntPtr pbc, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
    [DllImport("shell32.dll")]
    public static extern int SHBindToParent(IntPtr pidl, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv, out IntPtr childPidl);
    [DllImport("shell32.dll")]
    public static extern int SHDoDragDrop(IntPtr hwnd, System.Runtime.InteropServices.ComTypes.IDataObject data, IntPtr dropSource, int okEffects, out int effect);
    [DllImport("shell32.dll")]
    public static extern IntPtr ILFindLastID(IntPtr pidl);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    public static extern int StrRetToBufW(IntPtr pstr, IntPtr pidl, System.Text.StringBuilder buf, int cch);
    [DllImport("ole32.dll")] public static extern void CoTaskMemFree(IntPtr p);
    [DllImport("user32.dll")] public static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] public static extern bool DestroyMenu(IntPtr m);
    [DllImport("user32.dll")] public static extern int TrackPopupMenuEx(IntPtr m, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
    [DllImport("gdi32.dll")] public static extern int GetObject(IntPtr h, int size, out DIBSECTION ds);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHELLEXECUTEINFO
    {
        public int cbSize; public uint fMask; public IntPtr hwnd;
        public string? lpVerb, lpFile, lpParameters, lpDirectory;
        public int nShow; public IntPtr hInstApp, lpIDList; public string? lpClass;
        public IntPtr hkeyClass; public uint dwHotKey; public IntPtr hIcon, hProcess;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }
    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }
    [StructLayout(LayoutKind.Sequential)]
    public struct DIBSECTION { public BITMAP dsBm; public BITMAPINFOHEADER dsBmih; public uint f0, f1, f2; public IntPtr dshSection; public uint dsOffset; }

    public static readonly Guid IID_IDropTarget = new("00000122-0000-0000-C000-000000000046");
    public static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    public static readonly Guid IID_IContextMenu = new("000214e4-0000-0000-c000-000000000046");
    public static readonly Guid IID_IDataObject = new("0000010e-0000-0000-C000-000000000046");
    public static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    public static readonly Guid IID_IShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");
}
