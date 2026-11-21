using System.Runtime.InteropServices;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Shelves.UI.Desk;

sealed partial class DesktopLayer
{
    enum Drag { None, ItemPending, ItemGhost, Marquee, RightPending, RightBox }
    Drag drag;
    Point downScreen;
    Rectangle? marquee, newShelfBox;
    HashSet<string> marqueeBase = new(StringComparer.OrdinalIgnoreCase);
    string? anchor;
    bool focusShown;
    DragGhost? ghost;
    List<string> dragNames = [];
    /// <summary>Set while our own icons are in a shell drag (over another app); a drop back on the desktop is a move.</summary>
    static List<string>? shellDragNames;
    static Point shellDragFrom;
    /// <summary>Set while a shelf's desktop icons are in a shell drag: dropped here, the shelf takes them out itself.</summary>
    public static bool ShelfDrag;
    Form? renameBox;
    readonly System.Windows.Forms.Timer typeTimer = new() { Interval = 1000 };
    string typed = "";

    Point ToClient(Point screen) => new(screen.X - Screen.Bounds.X, screen.Y - Screen.Bounds.Y);

    // ---- Mouse ---------------------------------------------------------------------------------------------------

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var now = Cursor.Position;
        var ds = SystemInformation.DragSize;
        bool moved = Math.Abs(now.X - downScreen.X) > ds.Width || Math.Abs(now.Y - downScreen.Y) > ds.Height;
        switch (drag)
        {
            case Drag.ItemPending when moved:
                dragNames = SelectedNames.ToList();
                if (dragNames.Count == 0) { drag = Drag.None; Capture = false; return; }
                drag = Drag.ItemGhost;
                // Every dragged icon moves at its own spot, as on Windows' desktop, not piled under the mouse.
                ghost = GhostOf(dragNames, downScreen) ?? new DragGhost(dragNames, IconPx, S);
                ghost.MoveTo(now);
                return;
            case Drag.ItemGhost:
                ghost?.MoveTo(now);
                if (app.LayerAt(now) is { } over)
                    over.ShowDropTarget(over.DropTargetAt(over.ToClient(now), dragNames));
                else
                {
                    foreach (var l in app.Layers) l.ShowDropTarget(null);
                    // Over another app (an Explorer window, a chat, a browser): hand over to the shell's own drag.
                    if (app.ShelfAt(now) == null)
                    {
                        var names = dragNames.ToList();
                        EndGhost();
                        drag = Drag.None;
                        Capture = false;
                        ShellDrag(names);
                    }
                }
                return;
            case Drag.Marquee:
            {
                var c = ToClient(now);
                var d = ToClient(downScreen);
                var old = marquee;
                marquee = Rectangle.FromLTRB(Math.Min(c.X, d.X), Math.Min(c.Y, d.Y), Math.Max(c.X, d.X) + 1, Math.Max(c.Y, d.Y) + 1);
                if (old is { } o) InvalidateRect(Rectangle.Inflate(o, 2, 2));
                InvalidateRect(Rectangle.Inflate(marquee.Value, 2, 2));
                foreach (var it in icons)
                {
                    bool inBox = HitBox(it).IntersectsWith(marquee.Value);
                    bool was = marqueeBase.Contains(it.Name);
                    // Ctrl flips what the box covers; otherwise the box adds to what was selected before (Shift) or to nothing.
                    bool want = (ModifierKeys & Keys.Control) != 0 ? inBox ^ was : inBox || was;
                    if (want ? selected.Add(it.Name) : selected.Remove(it.Name)) Invalidate(it);
                }
                Render();
                return;
            }
            case Drag.RightPending when Math.Abs(now.X - downScreen.X) > 6 || Math.Abs(now.Y - downScreen.Y) > 6:
                drag = Drag.RightBox;
                goto case Drag.RightBox;
            case Drag.RightBox:
            {
                var c = ToClient(now);
                var d = ToClient(downScreen);
                if (newShelfBox is { } o) InvalidateRect(Rectangle.Inflate(o, 4, 4));
                newShelfBox = Rectangle.FromLTRB(Math.Min(c.X, d.X), Math.Min(c.Y, d.Y), Math.Max(c.X, d.X), Math.Max(c.Y, d.Y));
                InvalidateRect(Rectangle.Inflate(newShelfBox.Value, 4, 4));
                Render();
                return;
            }
        }
        var h = IconAt(e.Location)?.Name;
        if (!string.Equals(h, hoverName, StringComparison.OrdinalIgnoreCase))
        {
            InvalidateName(hoverName);
            hoverName = h;
            InvalidateName(hoverName);
            Render();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hoverName == null) return;
        InvalidateName(hoverName);
        hoverName = null;
        Render();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        downScreen = Cursor.Position;
        app.NoteDesktopPoint(downScreen);
        Activate();
        foreach (var l in app.Layers) if (l != this) l.ClearSelection();
        var it = IconAt(e.Location);
        if (e.Button == MouseButtons.Right)
        {
            if (it == null) { drag = Drag.RightPending; Capture = true; }
            else if (!selected.Contains(it.Name)) Select(it.Name, only: true);
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        focusShown = false;
        var before = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        if (it != null)
        {
            if ((ModifierKeys & Keys.Control) != 0)
            {
                if (!selected.Remove(it.Name)) selected.Add(it.Name);
                anchor = it.Name;
                SetFocus(it.Name);
            }
            else if ((ModifierKeys & Keys.Shift) != 0 && anchor != null)
                SelectRange(anchor, it.Name);
            else if (!selected.Contains(it.Name))
                Select(it.Name, only: true);
            else SetFocus(it.Name);
            RenderChanged(before);
            drag = Drag.ItemPending;
            Capture = true;
            return;
        }
        // Empty desktop: a selection box (Ctrl and Shift add to what is selected).
        marqueeBase = (ModifierKeys & (Keys.Control | Keys.Shift)) != 0 ? new(selected, StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase);
        if (marqueeBase.Count == 0 && selected.Count > 0) { selected.Clear(); RenderChanged(before); }
        drag = Drag.Marquee;
        Capture = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Capture = false;
        var was = drag;
        drag = Drag.None;
        var at = Cursor.Position;
        switch (was)
        {
            case Drag.ItemPending:
                // A plain click on one of several selected icons leaves just that one selected.
                if ((ModifierKeys & (Keys.Control | Keys.Shift)) == 0 && IconAt(e.Location) is { } one && selected.Count > 1)
                    Select(one.Name, only: true);
                return;
            case Drag.ItemGhost:
            {
                var names = dragNames.ToList();
                EndGhost();
                foreach (var l in app.Layers) l.ShowDropTarget(null);
                if (app.ShelfAt(at) is { } shelf) { shelf.TakeShelved(names, at); app.ReloadLayers(); return; }
                if (app.LayerAt(at) is { } layer) layer.DropIcons(names, downScreen, at, ModifierKeys);
                return;
            }
            case Drag.Marquee:
                if (marquee is { } mr) InvalidateRect(Rectangle.Inflate(mr, 2, 2));
                marquee = null;
                Render();
                return;
            case Drag.RightBox:
            {
                if (newShelfBox is { } nb)
                {
                    InvalidateRect(Rectangle.Inflate(nb, 4, 4));
                    newShelfBox = null;
                    Render();
                    var r = new Rectangle(nb.X + Screen.Bounds.X, nb.Y + Screen.Bounds.Y, nb.Width, nb.Height);
                    app.OfferNewShelf(r, at);
                }
                return;
            }
        }
        if (e.Button == MouseButtons.Right)
        {
            if (IconAt(e.Location) is { } it)
            {
                if (!selected.Contains(it.Name)) Select(it.Name, only: true);
                ItemMenu(at);
            }
            else
            {
                ClearSelection();
                BackgroundMenu(at);
            }
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button != MouseButtons.Left) return;
        if (IconAt(e.Location) is { } it) { ShellOps.Open(Handle, it.Name); return; }
        if (app.Settings.DoubleClickHides) app.ToggleHideAll();
    }

    void EndGhost()
    {
        ghost?.Close();
        ghost?.Dispose();
        ghost = null;
    }

    void Select(string name, bool only)
    {
        var before = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        if (only) selected.Clear();
        selected.Add(name);
        anchor = name;
        InvalidateName(focusName);
        focusName = name;
        RenderChanged(before);
    }

    /// <summary>Redraws the icons whose selection changed since <paramref name="before"/> (and the focused one).</summary>
    void RenderChanged(HashSet<string> before)
    {
        foreach (var i in icons)
            if (before.Contains(i.Name) != selected.Contains(i.Name)) Invalidate(i);
        InvalidateName(focusName);
        // One selected icon shows its whole name: the ones that just gained or lost that need redrawing too.
        foreach (var n in before) InvalidateName(n);
        Render();
    }

    void SetFocus(string name)
    {
        InvalidateName(focusName);
        focusName = name;
        InvalidateName(name);
    }

    /// <summary>Icons between two, in reading order (by row, then column), as Shift+click on the desktop selects.</summary>
    void SelectRange(string from, string to)
    {
        var order = icons.OrderBy(i => i.Box.X).ThenBy(i => i.Box.Y).ToList(); // the desktop fills columns first
        int a = order.FindIndex(i => string.Equals(i.Name, from, StringComparison.OrdinalIgnoreCase));
        int b = order.FindIndex(i => string.Equals(i.Name, to, StringComparison.OrdinalIgnoreCase));
        if (a < 0) a = b;
        selected.Clear();
        for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++) selected.Add(order[i].Name);
        focusName = to;
    }

    // ---- Moving icons on the desktop -----------------------------------------------------------------------------

    /// <summary>A drop target icon (folder, Recycle Bin, program) under a client point that is not one of <paramref name="dragged"/>.</summary>
    string? DropTargetAt(Point client, IEnumerable<string> dragged)
    {
        var it = IconAt(client);
        if (it == null || dragged.Contains(it.Name, StringComparer.OrdinalIgnoreCase)) return null;
        return app.IsDropTarget(it.Name) ? it.Name : null;
    }

    void ShowDropTarget(string? name)
    {
        if (string.Equals(name, dropName, StringComparison.OrdinalIgnoreCase)) return;
        InvalidateName(dropName);
        dropName = name;
        InvalidateName(dropName);
        Render();
    }

    /// <summary>
    /// Our own icons dropped on this monitor: onto a folder (or the Recycle Bin, or a program) they go through the
    /// shell, as Explorer would do it; anywhere else they move by the drag's distance.
    /// </summary>
    public void DropIcons(List<string> names, Point from, Point to, Keys mods)
    {
        if (DropTargetAt(ToClient(to), names) is { } target)
        {
            app.DropOnItem(Handle, names, target, to, mods);
            return;
        }
        app.MoveIcons(names, to.X - from.X, to.Y - from.Y, to);
        foreach (var n in names) selected.Add(n);
    }

    /// <summary>Hands our icons to the shell's drag (over another app); a drop back on the desktop is still a move.</summary>
    void ShellDrag(List<string> names)
    {
        using var group = ItemGroup.FromDesktop(app.View, names);
        if (group == null) return;
        shellDragNames = names;
        shellDragFrom = downScreen;
        try { ShellOps.Drag(Handle, group); }
        finally { shellDragNames = null; app.CheckSoon(); }
    }

    // ---- Menus ---------------------------------------------------------------------------------------------------

    void ItemMenu(Point screen)
    {
        var names = SelectedNames.ToList();
        if (names.Count == 0) return;
        using var group = ItemGroup.FromDesktop(app.View, names);
        if (group == null) return;
        string? verb = ShellOps.ContextMenu(Handle, group, screen, (ModifierKeys & Keys.Shift) != 0);
        if (verb == "rename" && names.Count == 1) BeginRename(names[0]);
        if (verb is "cut") app.MarkCut(names);
        app.CheckSoon();
    }

    void BackgroundMenu(Point screen)
    {
        app.NoteDesktopPoint(screen);
        var before = app.DesktopNames();
        ShellOps.BackgroundMenu(Handle, app.View, screen, (ModifierKeys & Keys.Shift) != 0);
        // New folder, New text document, Paste: the new items go where the menu was opened, and a new one
        // Explorer wants named gets our own name box (Explorer's would be in its hidden list). The menu stays open
        // as long as the user likes, so this watches for a while.
        app.PlaceNew(before, screen, rename: true, waitMs: 30000);
    }

    // ---- Keys ----------------------------------------------------------------------------------------------------

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (renameBox != null) return;
        var names = SelectedNames.ToList();
        switch (e.KeyCode)
        {
            case Keys.Enter:
                foreach (var n in names.Take(15)) ShellOps.Open(Handle, n);
                break;
            case Keys.F2:
                if (names.Count == 1) BeginRename(names[0]);
                break;
            case Keys.F5:
                IconCache.Forget();
                app.ReloadLayers(full: true);
                break;
            case Keys.Delete:
                Verb(names, "delete", e.Shift);
                break;
            case Keys.A when e.Control:
                foreach (var l in app.Layers) l.SelectAll();
                break;
            case Keys.C when e.Control:
                Verb(names, "copy", false);
                break;
            case Keys.X when e.Control:
                Verb(names, "cut", false);
                app.MarkCut(names);
                break;
            case Keys.V when e.Control:
            {
                var before = app.DesktopNames();
                ShellOps.BackgroundVerb(Handle, app.View, "paste");
                app.PlaceNew(before, app.LastDesktopPoint ?? Cursor.Position, rename: false);
                break;
            }
            case Keys.Escape:
                ClearSelection();
                break;
            case Keys.Apps:
                if (names.Count > 0 && icons.FirstOrDefault(i => i.Name == names[0]) is { } fi)
                    ItemMenu(PointToScreen(new Point(fi.Box.X + fi.Box.Width / 2, fi.Box.Y + fi.Box.Height / 2)));
                else BackgroundMenu(Cursor.Position);
                break;
            case Keys.Left or Keys.Right or Keys.Up or Keys.Down:
                MoveFocus(e.KeyCode, e.Shift);
                e.Handled = true;
                break;
        }
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (renameBox != null || char.IsControl(e.KeyChar)) return;
        // Type the start of a name to jump to it, as on the desktop.
        typeTimer.Stop();
        typed += e.KeyChar;
        typeTimer.Tick -= TypeReset;
        typeTimer.Tick += TypeReset;
        typeTimer.Start();
        var ordered = icons.OrderBy(i => i.Box.X).ThenBy(i => i.Box.Y).ToList();
        var hit = ordered.FirstOrDefault(i => i.Label.StartsWith(typed, StringComparison.CurrentCultureIgnoreCase));
        if (hit != null) { focusShown = true; Select(hit.Name, only: true); }
    }

    void TypeReset(object? s, EventArgs e) { typeTimer.Stop(); typed = ""; }

    void SelectAll()
    {
        foreach (var i in icons) selected.Add(i.Name);
        RenderAll();
    }

    void Verb(List<string> names, string verb, bool shift)
    {
        if (names.Count == 0) return;
        using var group = ItemGroup.FromDesktop(app.View, names);
        if (group != null) ShellOps.Invoke(Handle, group, verb, shift);
        app.CheckSoon();
    }

    /// <summary>Arrow keys: to the nearest icon that way.</summary>
    void MoveFocus(Keys key, bool extend)
    {
        var from = icons.FirstOrDefault(i => string.Equals(i.Name, focusName, StringComparison.OrdinalIgnoreCase)) ?? icons.FirstOrDefault();
        if (from == null) return;
        var c = new Point(from.Box.X + from.Box.Width / 2, from.Box.Y + from.Box.Height / 2);
        LayerIcon? best = null;
        double bestD = double.MaxValue;
        foreach (var i in icons)
        {
            if (i == from) continue;
            var p = new Point(i.Box.X + i.Box.Width / 2, i.Box.Y + i.Box.Height / 2);
            int dx = p.X - c.X, dy = p.Y - c.Y;
            bool ok = key switch
            {
                Keys.Left => dx < 0 && Math.Abs(dy) <= -dx,
                Keys.Right => dx > 0 && Math.Abs(dy) <= dx,
                Keys.Up => dy < 0 && Math.Abs(dx) <= -dy,
                _ => dy > 0 && Math.Abs(dx) <= dy,
            };
            if (!ok) continue;
            double d = dx * (double)dx + dy * (double)dy;
            if (d < bestD) { bestD = d; best = i; }
        }
        if (best == null) return;
        focusShown = true;
        if (extend) { selected.Add(best.Name); focusName = best.Name; RenderAll(); }
        else Select(best.Name, only: true);
    }

    // ---- Rename --------------------------------------------------------------------------------------------------

    public void BeginRename(string name)
    {
        var it = icons.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
        if (it == null) return;
        Select(name, only: true);
        renameBox?.Close();
        float s = S;
        var lr = LabelRect(it, true);
        // The name without its extension is selected, as Explorer does, so typing keeps the type.
        string text = ShellOps.EditName(name, it.Label);
        var tb = new TextBox
        {
            Text = text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 12f * s, GraphicsUnit.Pixel),
            TextAlign = HorizontalAlignment.Center,
            RightToLeft = L.Ar ? RightToLeft.Yes : RightToLeft.No,
            Location = Point.Empty,
        };
        // Just the label's width (a little more) and one line of text, like Explorer's box.
        int w = it.Box.Width + (int)(6 * s), h = tb.PreferredHeight;
        tb.Size = new Size(w, h);
        var screen = new Rectangle(Screen.Bounds.X + it.Box.X - (w - it.Box.Width) / 2, Screen.Bounds.Y + (int)lr.Y - (int)(1 * s), w, h);
        var f = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            AutoScaleMode = AutoScaleMode.None,
            MinimumSize = new Size(1, 1),
            BackColor = Color.White,
            Owner = this,
        };
        f.Controls.Add(tb);
        bool done = false;
        void Finish(bool ok)
        {
            if (done) return;
            done = true;
            string v = tb.Text.Trim();
            renameBox = null;
            f.Close();
            if (!ok || v.Length == 0 || v == text) return;
            using var g = ItemGroup.FromDesktop(app.View, [name]);
            if (g != null && ShellOps.Rename(Handle, g, v)) { IconCache.Forget(name); app.CheckSoon(); }
        }
        tb.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Finish(true); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Finish(false); }
        };
        tb.LostFocus += (_, _) => Finish(true);
        renameBox = f;
        f.Show(this);
        f.Bounds = screen; // after Show: a form shown first takes a minimum height of its own
        f.Activate();
        tb.Focus();
        int dot = text.LastIndexOf('.');
        if (dot > 0 && !Directory.Exists(name)) tb.Select(0, dot); else tb.SelectAll();
    }

    // ---- Drops from other apps -----------------------------------------------------------------------------------

    static readonly IDropTargetHelper2? helper = CreateHelper();

    static IDropTargetHelper2? CreateHelper()
    {
        try { return (IDropTargetHelper2)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("4657278A-411B-11D2-839A-00C04FD918D0"))!)!; }
        catch { return null; }
    }

    IShellDropTarget? fwd;
    string? fwdName;
    ComDataObject? fwdData;

    /// <summary>Where a drop at a screen point goes: an icon that takes drops, or the desktop folder itself (null name).</summary>
    string? DropTargetFor(Point screen) => DropTargetAt(ToClient(screen), shellDragNames ?? []);

    void Forward(DragEventArgs e, bool enter)
    {
        var pt = new Point(e.X, e.Y);
        string? want = DropTargetFor(pt);
        ShowDropTarget(want);
        // Our own icons over plain desktop: a move, handled by us, never a file operation.
        if ((shellDragNames != null || ShelfDrag) && want == null)
        {
            if (fwd != null) { fwd.DragLeave(); fwd = null; fwdName = null; }
            e.Effect = DragDropEffects.Move;
            return;
        }
        string target = want ?? app.DesktopDir;
        var p = new Native.POINT { X = pt.X, Y = pt.Y };
        uint effect = (uint)e.AllowedEffect;
        if (fwd == null || !string.Equals(fwdName, target, StringComparison.OrdinalIgnoreCase) || enter)
        {
            fwd?.DragLeave();
            fwd = ShellOps.DropTarget(Handle, target);
            fwdName = target;
            fwdData = e.Data as ComDataObject;
            if (fwd == null || fwdData == null) { e.Effect = DragDropEffects.None; return; }
            fwd.DragEnter(fwdData, (uint)e.KeyState, p, ref effect);
        }
        else fwd.DragOver((uint)e.KeyState, p, ref effect);
        e.Effect = (DragDropEffects)effect;
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        Forward(e, enter: true);
        var p = new Native.POINT { X = e.X, Y = e.Y };
        if (e.Data is ComDataObject cd) helper?.DragEnter(Handle, cd, ref p, (int)e.Effect);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        Forward(e, enter: false);
        var p = new Native.POINT { X = e.X, Y = e.Y };
        helper?.DragOver(ref p, (int)e.Effect);
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        helper?.DragLeave();
        fwd?.DragLeave();
        fwd = null; fwdName = null; fwdData = null;
        ShowDropTarget(null);
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        var pt = new Point(e.X, e.Y);
        var p = new Native.POINT { X = pt.X, Y = pt.Y };
        if (e.Data is ComDataObject cd) helper?.Drop(cd, ref p, (int)e.Effect);
        ShowDropTarget(null);
        if (ShelfDrag && fwd == null) { e.Effect = DragDropEffects.None; return; }
        if (shellDragNames is { } own && fwd == null)
        {
            // Our icons came back to the desktop: just move them, and tell the source nothing happened to the files.
            var names = own.ToList();
            var from = shellDragFrom;
            e.Effect = DragDropEffects.None;
            BeginInvoke(() => app.MoveIcons(names, pt.X - from.X, pt.Y - from.Y, pt));
            return;
        }
        if (fwd == null || fwdData == null) { e.Effect = DragDropEffects.None; return; }
        uint effect = (uint)e.AllowedEffect;
        bool toDesktop = string.Equals(fwdName, app.DesktopDir, StringComparison.OrdinalIgnoreCase);
        var before = toDesktop ? app.DesktopNames() : null;
        fwd.Drop(fwdData, (uint)e.KeyState, p, ref effect);
        e.Effect = (DragDropEffects)effect;
        fwd = null; fwdName = null; fwdData = null;
        // Files moved or copied onto the desktop appear where they were dropped (the shell copies in the background).
        if (before != null) app.PlaceNew(before, pt, rename: false);
    }
}

[ComImport, Guid("4657278B-411B-11D2-839A-00C04FD918D0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IDropTargetHelper2
{
    void DragEnter(IntPtr hwnd, ComDataObject data, ref Native.POINT pt, int effect);
    void DragLeave();
    void DragOver(ref Native.POINT pt, int effect);
    void Drop(ComDataObject data, ref Native.POINT pt, int effect);
    void Show(bool show);
}
