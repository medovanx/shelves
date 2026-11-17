# Shelves: Software Requirements Specification

Version 0.1 (draft), 2026-11-17

## 1. Purpose

Shelves groups desktop icons into translucent boxes ("shelves") on the Windows desktop. A shelf can roll up to save space. Unlike Stardock Fences, a shelf can dock to any of the four screen edges, and a shelf on the left or right edge rolls up into a thin vertical tab.

## 2. Scope and platform

- Windows 10 2004+ and Windows 11, x64.
- .NET 10 WinForms, the same layout as Salaty Reminder and Data Tide:
  - `src/Shelves` is the app.
  - `src/Shelves.Core` is the logic with no UI: layout, rules, snapshots and settings.
  - `tests/Shelves.Core.Tests` holds the xUnit tests.
  - `Directory.Build.props` holds the version.
- Portable single exe, no installer. Released on GitHub Releases from the public repo `medovanx/shelves`.
- In-app update check with a progress bar, as in Salaty Reminder.
- English and Arabic, with Arabic laid out right to left.
- Settings are JSON in `%APPDATA%\Shelves`.
- Starting with Windows is optional and done through the Run key.
- Dark and light follow the Windows theme, with the app's accent colour.

## 3. Definitions

| Term | Meaning |
|---|---|
| Shelf | A box on the desktop that holds icons. It has a title bar. |
| Rolled up | A shelf collapsed to its title bar, or to a vertical tab on a side edge. |
| Dock | A shelf snapped to a screen edge (top, bottom, left or right). |
| Portal | A shelf that shows the live contents of a folder instead of desktop icons. |
| Snapshot | A saved copy of every shelf and which icon is in it. |

## 4. Functional requirements

### 4.1 Shelves on the desktop

- **FR-1:** Shelves sit on the desktop layer, behind normal windows and above the wallpaper.
  - They stay visible after Win+D and Show desktop.
  - They never appear in the taskbar or in Alt+Tab.
- **FR-2:** To create a shelf, the user right-drags a box on the empty desktop and picks "Create shelf here".
  - The new shelf takes the box's bounds.
  - Its title is ready to edit right away.
- **FR-3:** A shelf can be moved by its title bar and resized from any edge or corner.
  - It snaps to screen edges and to other shelves within 10 px.
- **FR-4:** The title bar's right-click menu offers:
  - Rename and Roll up.
  - Dock left, right, top or bottom, and Undock.
  - Sort by name, type or date.
  - Icon size (small, medium, large).
  - Make portal.
  - Delete shelf. Its icons go back to the desktop and no files are touched.

### 4.2 Icons (real desktop items)

- **FR-5:** Files stay in the Desktop folders, both the user's and Public. A shelf draws the items that belong to it, using the shell's own icon, thumbnail and display name.
- **FR-6:** Windows' copy of a shelved icon is hidden from the desktop.
  - This uses the shell's `IFolderView`, which moves the item off the visible area. Nothing is injected into Explorer.
  - Every item is put back if the app quits or crashes. The next start and a "Restore desktop" item in the tray menu both handle this.
- **FR-7:** Icons can be dragged:
  - from the desktop into a shelf
  - between shelves
  - from a shelf back to the desktop
  - from Explorer or another app into a shelf, which copies or moves the file to the Desktop and shelves it.
- **FR-8:** Inside a shelf, an icon works like it does on the desktop:
  - Double-click opens it.
  - Right-click shows the real shell context menu (`IContextMenu`).
  - F2 renames it and Delete sends it to the Recycle Bin.
  - Ctrl+click and a drag box select several icons.
  - Dragging an icon onto another app works.
- **FR-9:** The app watches the Desktop folders. A shelved file that is renamed stays in its shelf, and one that is deleted leaves it. A new file goes onto the desktop and is not shelved.

### 4.3 Roll up

- **FR-10:** Double-clicking the title bar rolls a shelf up, or opens it again.
- **FR-11:** Hovering over a rolled-up shelf opens it temporarily.
  - It opens after 300 ms of hovering.
  - It rolls up again 500 ms after the mouse leaves, unless a drag is in progress or a menu is open.
- **FR-12:** How a shelf rolls up depends on where it is docked:

  | Docked to | Rolled up as | Opens toward |
  |---|---|---|
  | Top or not docked | Title bar | Downward |
  | Bottom | Title bar | Upward |
  | Left or right | Thin vertical tab (about 28 px) with the title turned sideways | Across the screen |

- **FR-13:** Rolling up and opening animate over about 150 ms, and the animation is skipped when Windows animations are turned off.
- **FR-14:** Dragging an icon onto a rolled-up shelf opens it, so the icon can be dropped inside.

### 4.4 Docking to edges

- **FR-15:** A shelf docks to an edge when it is dragged against that edge, or from the menu.
  - A docked shelf spans its length along the edge and keeps the depth the user set.
  - It stays inside the work area, so it never covers the taskbar.
- **FR-16:** Docked shelves on the same edge stack side by side and never overlap.

### 4.5 Quick hide

- **FR-17:** Double-clicking empty desktop hides every shelf and every desktop icon, and doing it again brings them back.
  - It can be turned off.
  - It doesn't fire while a file is being renamed.

### 4.6 Folder portals

- **FR-18:** A portal shelf shows a live view of any folder, for example Downloads.
  - It has the same icon actions as FR-8 and updates when the folder changes.
  - A scrollbar appears when the contents don't fit.
  - Its title bar has an "Open in Explorer" button.

### 4.7 Multi-monitor and layout

- **FR-19:** A shelf remembers which monitor it is on and its position as a fraction of that monitor's work area. A change of resolution, DPI or taskbar puts it back in the same relative spot.
- **FR-20:** If a shelf's monitor is unplugged, the shelf moves to the main monitor. It goes back when that monitor returns.
- **FR-21:** The layout is saved after every change, at most once a second.

### 4.8 Snapshots

- **FR-22:** The user can save a named snapshot and restore it from the tray menu.
- **FR-23:** The app takes an automatic snapshot each day and before every update, and keeps the last 10.
- **FR-24:** If many shelved items look wrong at once, the app offers to restore the last good snapshot. For example, after Explorer restarts or a resolution change scrambles the desktop.

### 4.9 Tray and settings

- **FR-25:** The tray icon's menu uses the themed menu style from Salaty Reminder. It holds:
  - New shelf, Hide all, Snapshots and Restore desktop.
  - Settings, Check for updates and Exit.
  - The version line with the app icon.
- **FR-26:** Settings has these pages:
  - **General:** start with Windows, language, and double-click to hide.
  - **Appearance:**
    - acrylic opacity
    - corner radius
    - title alignment
    - icon size
    - label lines
  - **Roll up:** hover delay, and whether shelves start rolled up.
  - **Snapshots**
  - **Updates**
  - **About**

## 5. Look

- **UI-1:** A shelf has a translucent acrylic background (DWM backdrop blur) tinted to the theme, rounded corners (8 px) and a 1 px border.
  - The title is centred, or turned sideways on a vertical tab.
  - A thin accent line runs under the title while the mouse is over the shelf.
- **UI-2:** Icon labels match the desktop's: white text with a shadow, wrapped to two lines and cut off with an ellipsis.
- **UI-3:** The app is DPI aware per monitor (PerMonitorV2), so everything stays sharp when a shelf moves between monitors with different scaling.
- **UI-4:** No "·" or em dash appears anywhere in the UI text.

## 6. Non-functional requirements

- **NFR-1 Safety:** The app never deletes, moves or renames a file unless the user does it directly. Deleting a shelf only takes its icons out of it.
- **NFR-2 Recovery:**
  - After a crash or kill, the next start restores the desktop to match the saved layout.
  - Exit and uninstall leave a clean desktop with every icon visible.
- **NFR-3 Explorer restarts:** When Explorer restarts (`TaskbarCreated`), the app attaches to the desktop again within 3 s, with no restart needed.
- **NFR-4 Performance:**
  - It idles under 1% CPU and 80 MB of memory.
  - 300 desktop items load in under 1 s.
  - Rolling up and opening run at 60 fps.
- **NFR-5 Privacy:** The only network request is the update check against GitHub. There is no telemetry.
- **NFR-6 Tests:** `Shelves.Core` covers these with xUnit tests:
  - docking geometry
  - roll-up rectangles for all four edges
  - multi-monitor remapping
  - snapshot diffing
  - settings migration

## 7. Out of scope for v1

- Rules that file new items automatically
- Tabs inside a shelf
- Desktop pages you swipe between
- Per-shelf colours
- An installer

## 8. Risks

| Risk | Mitigation |
|---|---|
| Hiding Explorer's icons through `IFolderView` can break with auto-arrange or align-to-grid on. | Turn auto-arrange off while Shelves runs, and restore the user's setting on exit. |
| Explorer moves icons back after it restarts. | Re-apply the layout on `TaskbarCreated` (NFR-3), with snapshots as a fallback. |
| Acrylic blur costs GPU on old machines. | Fall back to a solid themed background when transparency effects are off. |

## 9. Spike results (2026-11-17, Windows build 26300)

Stardock Fences was running during the first tests and was reverting icon moves. These results are from after it was closed and Explorer was restarted.

- **Finding the desktop:** the shell's desktop view is reachable through `IShellWindows`, then `IFolderView2`. Item names, positions and folder flags all read correctly. Auto-arrange and align-to-grid are off on this machine.
- **Coordinates:** item positions are relative to the virtual screen's top-left corner, so the left monitor at x = -1280 is position 0.
- **Moving icons:** both `IFolderView.SelectAndPositionItems` and the list view's `LVM_SETITEMPOSITION` move an icon off-screen (9000, 5000), and the move survives F5.
- **Keeping icons hidden:** Shelves still re-checks after refreshes and Explorer restarts, in case Explorer or another desktop tool moves an icon back.
- **Running alongside Fences:** not supported. Shelves warns when it finds Fences running.
- **Shelf windows:**
  - Windows' blur (accent policy) does not work on per-pixel-alpha windows; the shelf rendered solid black. Shelves draws a blurred copy of the wallpaper itself.
  - After Show desktop (Win+D) the desktop rose above the shelf, so it stayed visible but could not be clicked. Shelves re-places its windows directly above the desktop whenever the desktop comes to the front. Re-checked without Fences: the shell raises the desktop again a moment after the focus change, so Shelves re-places at 150, 400 and 900 ms, and every second while the desktop is in front.
