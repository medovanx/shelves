using System.Runtime.InteropServices;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Shelves.UI.Shelf;

sealed partial class ShelfWindow
{
    enum DragKind { None, TitlePending, Move, Resize, ItemPending, Marquee, TabPending, TabMove }
    int draggedTab = -1;
    [Flags] enum Edges { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

    DragKind drag;
    Edges resizeEdges;
    Point downScreen, downWindow;
    Box downOpen;
    Point marqueeFrom, marqueeTo; // open coordinates, scroll included
    bool menuOpen, dropHover, hotPortal;
    Rectangle? portalButton;

    Rectangle MarqueeRect => Rectangle.FromLTRB(Math.Min(marqueeFrom.X, marqueeTo.X), Math.Min(marqueeFrom.Y, marqueeTo.Y),
                                                Math.Max(marqueeFrom.X, marqueeTo.X), Math.Max(marqueeFrom.Y, marqueeTo.Y));

    /// <summary>The window's real size now (WinForms' Width and Height do not follow UpdateLayeredWindow).</summary>
    Size Now => new(Target().W, Target().H);

    bool InTitle(Point p) => TitleRect(Now).Contains(p);

    Edges EdgesAt(Point p)
    {
        if (!IsOpen || Model.Rolled && !peeking) return Edges.None;
        int g = (int)(6 * S);
        var e = Edges.None;
        if (p.X < g) e |= Edges.Left;
        if (p.X >= Now.Width - g) e |= Edges.Right;
        if (p.Y < g) e |= Edges.Top;
        if (p.Y >= Now.Height - g) e |= Edges.Bottom;
        return e;
    }

    static Cursor CursorFor(Edges e) => e switch
    {
        Edges.Left or Edges.Right => Cursors.SizeWE,
        Edges.Top or Edges.Bottom => Cursors.SizeNS,
        Edges.Left | Edges.Top or Edges.Right | Edges.Bottom => Cursors.SizeNWSE,
        Edges.Right | Edges.Top or Edges.Left | Edges.Bottom => Cursors.SizeNESW,
        _ => Cursors.Default,
    };

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        hot = true;
        HoverTick(); // start the hover delay now, not at the next timer tick
        if (!anim.Enabled) Apply();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hot = false;
        hoverItem = null;
        hotPortal = false;
        hoverTab = -1;
        Apply();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        downScreen = Cursor.Position;
        downWindow = e.Location;
        downOpen = open;
        if (e.Button == MouseButtons.Right) return;
        if (e.Button != MouseButtons.Left) return;

        if (portalButton is { } pb && pb.Contains(e.Location) && Model.Portal != null)
        {
            ShellOps.Open(Handle, Model.Portal);
            return;
        }
        var edges = EdgesAt(e.Location);
        if (edges != Edges.None) { drag = DragKind.Resize; resizeEdges = edges; Capture = true; return; }
        if (InTitle(e.Location)) { drag = DragKind.TitlePending; Capture = true; return; }
        int tab = TabAt(e.Location);
        if (tab == -2) { AddTab(); return; }
        if (tab >= 0)
        {
            // Shown at once; dragging it sideways moves it along the strip.
            SelectTab(tab);
            draggedTab = tab;
            drag = DragKind.TabPending;
            Capture = true;
            return;
        }

        var it = ItemAt(e.Location);
        if (it != null)
        {
            if ((ModifierKeys & Keys.Control) != 0)
            {
                if (!selected.Remove(it.Name)) selected.Add(it.Name);
                anchor = it.Name;
            }
            else if ((ModifierKeys & Keys.Shift) != 0 && anchor != null)
            {
                int a = items.FindIndex(i => string.Equals(i.Name, anchor, StringComparison.OrdinalIgnoreCase)), b = items.IndexOf(it);
                if (a < 0) a = b;
                selected.Clear();
                for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++) selected.Add(items[i].Name);
            }
            else if (!selected.Contains(it.Name))
            {
                selected.Clear();
                selected.Add(it.Name);
                anchor = it.Name;
            }
            drag = DragKind.ItemPending;
            Apply();
            return;
        }

        // Empty space in the content: a selection box.
        if ((ModifierKeys & (Keys.Control | Keys.Shift)) == 0) selected.Clear();
        var p = ToOpen(e.Location);
        p.Y += scroll;
        marqueeFrom = marqueeTo = p;
        drag = DragKind.Marquee;
        Capture = true;
        Apply();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var now = Cursor.Position;
        var ds = SystemInformation.DragSize;
        bool moved = Math.Abs(now.X - downScreen.X) > ds.Width || Math.Abs(now.Y - downScreen.Y) > ds.Height;
        switch (drag)
        {
            case DragKind.TabPending when moved:
                drag = DragKind.TabMove;
                goto case DragKind.TabMove;
            case DragKind.TabMove:
            {
                int over = TabAtStrip(e.Location);
                if (over >= 0 && over != draggedTab) { MoveTabTo(draggedTab, over); draggedTab = over; }
                return;
            }
            case DragKind.TitlePending when moved:
                drag = DragKind.Move;
                goto case DragKind.Move;
            case DragKind.Move:
                MoveTo(now);
                return;
            case DragKind.Resize:
                ResizeTo(now);
                return;
            case DragKind.ItemPending when moved:
                drag = DragKind.None;
                Capture = false;
                DragOut();
                return;
            case DragKind.Marquee:
            {
                var p = ToOpen(e.Location);
                p.Y += scroll;
                marqueeTo = p;
                var mr = MarqueeRect;
                foreach (var it in items)
                    if (it.Cell.IntersectsWith(mr)) selected.Add(it.Name);
                    else if ((ModifierKeys & (Keys.Control | Keys.Shift)) == 0) selected.Remove(it.Name);
                Apply();
                return;
            }
        }

        // Hover feedback.
        var edges = EdgesAt(e.Location);
        Cursor = edges != Edges.None ? CursorFor(edges) : Cursors.Default;
        var h = ItemAt(e.Location);
        bool hp = portalButton is { } pb && pb.Contains(e.Location);
        int ht = TabAt(e.Location);
        if (h != hoverItem || hp != hotPortal || ht != hoverTab) { hoverItem = h; hotPortal = hp; hoverTab = ht; Apply(); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Capture = false;
        var was = drag;
        drag = DragKind.None;
        switch (was)
        {
            case DragKind.Move:
                FinishMove();
                break;
            case DragKind.Resize:
                if (Model.Edge != Edge.None) open = ShelfGeometry.DockTo(open, Work, Model.Edge);
                ToModel();
                LayoutItems();
                Apply();
                break;
            case DragKind.ItemPending:
                // A plain click on a selected item (no drag) leaves just that item selected.
                if ((ModifierKeys & (Keys.Control | Keys.Shift)) == 0 && ItemAt(e.Location) is { } it)
                {
                    selected.Clear();
                    selected.Add(it.Name);
                    anchor = it.Name;
                }
                Apply();
                break;
            case DragKind.Marquee:
                Apply();
                break;
            case DragKind.TabMove:
                draggedTab = -1;
                app.SaveSoon();
                Apply();
                break;
            case DragKind.TabPending:
                draggedTab = -1;
                break;
        }
        if (e.Button == MouseButtons.Right && TabAt(e.Location) is var rt and >= 0)
        {
            ShowTabMenu(rt, Cursor.Position);
            return;
        }
        if (e.Button == MouseButtons.Right)
        {
            if (InTitle(e.Location) || ItemAt(e.Location) is not { } it)
            {
                selected.Clear();
                Apply();
                ShowShelfMenu(Cursor.Position);
            }
            else
            {
                if (!selected.Contains(it.Name)) { selected.Clear(); selected.Add(it.Name); anchor = it.Name; Apply(); }
                ShellMenu(Cursor.Position);
            }
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button != MouseButtons.Left) return;
        if (InTitle(e.Location)) { SetRolled(!Model.Rolled); return; }
        if (TabAt(e.Location) is var t and >= 0) { BeginTabRename(t); return; }
        if (ItemAt(e.Location) is { } it) OpenItem(it);
    }

    // ---- Moving and resizing -----------------------------------------------------------------------------------

    void MoveTo(Point screen)
    {
        int dx = screen.X - downScreen.X, dy = screen.Y - downScreen.Y;
        var moved = downOpen.Offset(dx, dy);
        // Pulled off its edge: a floating shelf again, which rolls to its title bar.
        if (Model.Edge != Edge.None) Model.Edge = Edge.None;
        var work = ToBox(Screen.FromPoint(screen).WorkingArea);
        var others = app.Windows.Where(w => w != this && w.Visible).Select(w => w.VisibleBox).ToList();
        var visible = ShelfGeometry.Rolled(moved, Edge.None, TitleH, TabW);
        var snapped = ShelfGeometry.Snap(IsOpen ? moved : visible, work, others, (int)(10 * S));
        open = moved.Offset(snapped.X - (IsOpen ? moved.X : visible.X), snapped.Y - (IsOpen ? moved.Y : visible.Y));
        Apply();
    }

    void FinishMove()
    {
        var work = Work;
        var visible = VisibleBox;
        var edge = ShelfGeometry.NearEdge(visible, work, (int)(12 * S));
        Model.Edge = edge;
        if (edge != Edge.None)
        {
            open = ShelfGeometry.DockTo(open, work, edge);
            var same = app.Windows.Where(w => w != this && w.Model.Edge == edge && w.Monitor == Monitor)
                .Select(w => w.OpenBox).ToList();
            open = ShelfGeometry.Unstack(open, edge, work, same);
        }
        ToModel();
        LayoutItems();
        Apply();
    }

    void ResizeTo(Point screen)
    {
        int dx = screen.X - downScreen.X, dy = screen.Y - downScreen.Y;
        int minW = (int)(140 * S), minH = TitleH + CellH / 2 + Pad * 2;
        int l = downOpen.X, t = downOpen.Y, r = downOpen.Right, b = downOpen.Bottom;
        if (resizeEdges.HasFlag(Edges.Left)) l = Math.Min(l + dx, r - minW);
        if (resizeEdges.HasFlag(Edges.Right)) r = Math.Max(r + dx, l + minW);
        if (resizeEdges.HasFlag(Edges.Top)) t = Math.Min(t + dy, b - minH);
        if (resizeEdges.HasFlag(Edges.Bottom)) b = Math.Max(b + dy, t + minH);
        open = Box.FromLTRB(l, t, r, b);
        LayoutItems();
        Apply();
    }

    public Box OpenBox => open;
    public Box VisibleBox => Target();
    public Screen Monitor => Screen.FromRectangle(ToRect(open));

    public void DockTo(Edge edge)
    {
        var work = Work;
        Model.Edge = edge;
        if (edge != Edge.None)
        {
            open = ShelfGeometry.DockTo(open, work, edge);
            var same = app.Windows.Where(w => w != this && w.Model.Edge == edge && w.Monitor == Monitor).Select(w => w.OpenBox).ToList();
            open = ShelfGeometry.Unstack(open, edge, work, same);
        }
        ToModel();
        LayoutItems();
        Apply();
    }

    // ---- Dragging icons out ------------------------------------------------------------------------------------

    void DragOut()
    {
        var names = SelectedNames();
        if (names.Count == 0) return;
        using var group = Group(names);
        if (group == null) return;
        app.Keeper.Paused = true;
        try
        {
            ShellOps.Drag(Handle, group);
            if (Model.Portal != null) return;
            // Dropped on the desktop: the icons leave the shelf and appear where they were dropped.
            var pt = Cursor.Position;
            if (!app.IsDesktopAt(pt)) return;
            var origin = app.View.Origin;
            var sp = app.View.Spacing;
            int i = 0;
            foreach (var n in names)
            {
                app.Layout.Unshelve(n);
                int col = i / 6, row = i % 6;
                app.Keeper.Unhide(n, new Pt(pt.X - origin.X - sp.Width / 2 + col * sp.Width, pt.Y - origin.Y - sp.Height / 3 + row * sp.Height));
                i++;
            }
            selected.Clear();
            app.SaveSoon();
        }
        finally
        {
            app.Keeper.Paused = false;
            app.Refresh();
        }
    }

    // ---- Dropping onto the shelf -------------------------------------------------------------------------------

    static readonly IDropTargetHelper? helper = CreateHelper();

    static IDropTargetHelper? CreateHelper()
    {
        try { return (IDropTargetHelper)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("4657278A-411B-11D2-839A-00C04FD918D0"))!)!; }
        catch { return null; }
    }

    List<string> dropNames = [];

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        dropNames = e.Data is { } d ? DropNames.From(d) : [];
        dropHover = dropNames.Count > 0;
        if (Model.Rolled) Peek(true);
        e.Effect = EffectFor(e);
        var p = new Native.POINT { X = e.X, Y = e.Y };
        if (e.Data is ComDataObject cd) helper?.DragEnter(Handle, cd, ref p, (int)e.Effect);
        Apply();
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        DragOverTabs(PointToClient(new Point(e.X, e.Y)));
        e.Effect = EffectFor(e);
        var p = new Native.POINT { X = e.X, Y = e.Y };
        helper?.DragOver(ref p, (int)e.Effect);
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        helper?.DragLeave();
        dropHover = false;
        dragTab = -1;
        Apply();
    }

    /// <summary>
    /// Desktop icons are only shelved (a link effect, so Explorer never treats it as a move and deletes anything).
    /// Other files are moved or copied onto the desktop, or into a portal's folder, like Explorer would.
    /// </summary>
    DragDropEffects EffectFor(DragEventArgs e)
    {
        if (dropNames.Count == 0) return DragDropEffects.None;
        if (Model.Portal == null && dropNames.All(app.IsDesktopItem))
            return dropNames.All(n => Model.Items.Contains(n, StringComparer.OrdinalIgnoreCase)) ? DragDropEffects.None : DragDropEffects.Link;
        string destDir = Model.Portal ?? app.DesktopDir;
        if (dropNames.Any(n => n.StartsWith("::") || !(File.Exists(n) || Directory.Exists(n)))) return DragDropEffects.None;
        if (dropNames.All(n => string.Equals(Path.GetDirectoryName(n), destDir, StringComparison.OrdinalIgnoreCase)))
            return Model.Portal != null ? DragDropEffects.None : DragDropEffects.Link;
        bool ctrl = (e.KeyState & 8) != 0, shift = (e.KeyState & 4) != 0;
        bool sameVolume = string.Equals(Path.GetPathRoot(dropNames[0]), Path.GetPathRoot(destDir), StringComparison.OrdinalIgnoreCase);
        var want = ctrl ? DragDropEffects.Copy : shift || sameVolume ? DragDropEffects.Move : DragDropEffects.Copy;
        return (e.AllowedEffect & want) != 0 ? want : (e.AllowedEffect & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        var effect = EffectFor(e);
        var p = new Native.POINT { X = e.X, Y = e.Y };
        if (e.Data is ComDataObject cd) helper?.Drop(cd, ref p, (int)effect);
        dropHover = false;
        dragTab = -1;
        var names = dropNames.ToList();
        var client = PointToClient(new Point(e.X, e.Y));
        // Dropped on a tab: into that tab, at its end.
        int onTab = TabAt(client);
        if (onTab >= 0 && onTab != Model.Active) SelectTab(onTab);
        int index = onTab >= 0 ? -1 : DropIndex(client);
        // Shelving needs no file work; moving and copying files run after the drop returns, so the source
        // (often Explorer) is not held up, and report no effect so the source never deletes anything itself.
        e.Effect = effect == DragDropEffects.Link ? DragDropEffects.Link : DragDropEffects.None;
        if (effect == DragDropEffects.None) { Apply(); return; }
        BeginInvoke(() => Receive(names, effect, index));
    }

    /// <summary>The position in the shelf's order for a drop at a client point (only kept when not sorted).</summary>
    int DropIndex(Point client)
    {
        if (Model.Sort != SortBy.None) return -1;
        var p = ToOpen(client);
        p.Y += scroll;
        for (int i = 0; i < items.Count; i++)
        {
            var c = items[i].Cell;
            if (p.Y < c.Bottom && (p.Y >= c.Top || i == 0) && (L.Ar ? p.X > c.X + c.Width / 2 : p.X < c.X + c.Width / 2))
                return Model.Items.FindIndex(n => string.Equals(n, items[i].Name, StringComparison.OrdinalIgnoreCase));
        }
        return -1;
    }

    void Receive(List<string> names, DragDropEffects effect, int index)
    {
        if (effect == DragDropEffects.Link && Model.Portal == null)
        {
            foreach (var n in names)
            {
                app.Layout.Shelve(n, Model, index);
                if (index >= 0) index++;
            }
            app.Keeper.Sync();
            app.SaveSoon();
            app.Refresh();
            return;
        }
        string destDir = Model.Portal ?? app.DesktopDir;
        foreach (var n in names)
        {
            string dest = Path.Combine(destDir, Path.GetFileName(n.TrimEnd('\\')));
            if (Model.Portal == null) app.Layout.Shelve(dest, Model, index >= 0 ? index++ : -1);
            try
            {
                var ui = Microsoft.VisualBasic.FileIO.UIOption.AllDialogs;
                if (Directory.Exists(n))
                {
                    if (effect == DragDropEffects.Move) Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(n, dest, ui);
                    else Microsoft.VisualBasic.FileIO.FileSystem.CopyDirectory(n, dest, ui);
                }
                else
                {
                    if (effect == DragDropEffects.Move) Microsoft.VisualBasic.FileIO.FileSystem.MoveFile(n, dest, ui);
                    else Microsoft.VisualBasic.FileIO.FileSystem.CopyFile(n, dest, ui);
                }
            }
            catch (OperationCanceledException) { if (Model.Portal == null) app.Layout.Unshelve(dest); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error(ex);
                if (Model.Portal == null) app.Layout.Unshelve(dest);
            }
        }
        app.SaveSoon();
        app.Keeper.Check();
        app.Refresh();
    }
}

[ComImport, Guid("4657278B-411B-11D2-839A-00C04FD918D0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IDropTargetHelper
{
    void DragEnter(IntPtr hwnd, ComDataObject data, ref Native.POINT pt, int effect);
    void DragLeave();
    void DragOver(ref Native.POINT pt, int effect);
    void Drop(ComDataObject data, ref Native.POINT pt, int effect);
    void Show(bool show);
}
