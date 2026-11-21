# Changelog

All notable changes to Shelves are listed here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Fixed
- Moving an icon on the desktop could make another icon jump to a corner, or the dragged one snap back: Shelves sometimes re-hid the wrong icon after files were added or removed.
- "Align icons to grid" can stay on: hidden icons each get their own spot, so Windows no longer spreads them back over the desktop.

## [1.1.1] - 2026-11-18

### Fixed
- Start with Windows works again on recent Windows 11 builds, which skip a startup entry unless it is also marked as enabled in Windows' startup list. Turning it on now writes that mark too.

## [1.1.0] - 2026-11-17

### Added
- "New shelf" in the desktop's own right-click menu (Settings, General; Windows asks for admin rights once).
- Import from a Fences backup file: its daily snapshots or a .reg export of its settings.
- Pin a shelf open so it never rolls up.

### Changed
- The tray menu's first line reads "Shelves v" and the version.

## [1.0.1] - 2026-11-17

### Added
- Start with Windows in the tray menu.

## [1.0.0] - 2026-11-17

The first release.

### Added
- Shelves that hold your real desktop icons, with Windows' own icons, Explorer's right-click menu, rename, delete and drag and drop.
- Draw a shelf by right-dragging on the empty desktop.
- Roll up by double-clicking the title, and hover to peek inside. Rolling up is animated and happens as soon as the mouse leaves.
- Docking to all four screen edges. Side shelves roll into a vertical tab with a sideways title facing the screen edge.
- Tabs inside a shelf, each with its own icons or folder. Drag a tab to move it; the others slide aside.
- Folder portals that show a folder's contents live.
- Colour and opacity for all shelves, and per shelf from its menu.
- Double-click the empty desktop to hide everything.
- Frosted glass from a blurred copy of the wallpaper, in Windows' dark or light theme.
- Icon labels get the desktop's shadow when Windows' "drop shadows for icon labels" is on.
- Multi-monitor layouts that survive resolution, scaling and taskbar changes.
- Daily and manual snapshots, and Restore desktop.
- Import from Stardock Fences.
- Settings in English and Arabic, start with Windows, and automatic updates from GitHub Releases.

[1.1.1]: https://github.com/medovanx/shelves/releases/tag/v1.1.1
[1.1.0]: https://github.com/medovanx/shelves/releases/tag/v1.1.0
[1.0.1]: https://github.com/medovanx/shelves/releases/tag/v1.0.1
[1.0.0]: https://github.com/medovanx/shelves/releases/tag/v1.0.0
