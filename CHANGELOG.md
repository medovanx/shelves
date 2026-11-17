# Changelog

All notable changes to Shelves are listed here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Tabs inside a shelf, each with its own icons or folder.
- Colour and opacity for all shelves, and per shelf from its menu.
- Icon labels get the desktop's shadow when Windows' "drop shadows for icon labels" is on.
- "Animate rolling up", on whatever Windows' animation setting is.

### Changed
- Rolling up and opening are smooth and quick, and a shelf rolls up as soon as the mouse leaves.
- New defaults: 4 px corners, 50% opacity, 3 label lines, open on hover at once.
- Titles on side edges face the screen edge.
- Docked shelves sit flush against the screen edge.

### Fixed
- Shelved icons no longer flash on the desktop when an icon is dropped there.
- Icons kept their old spots when Explorer moved them; they now always go back to where they came from.
- Icons had hard, bright edges.
- Shelves and menus no longer have a shadow, and the shelf border is a crisp 1 px.
- A peeking shelf no longer rolls up while the mouse is still inside it, and can be resized.

## [0.1.0] - 2026-11-17

### Added
- Shelves that hold your real desktop icons, with Windows' own icons, Explorer's right-click menu, rename, delete and drag and drop.
- Draw a shelf by right-dragging on the empty desktop.
- Roll up by double-clicking the title, and hover to peek inside.
- Docking to all four screen edges. Side shelves roll into a vertical tab with a sideways title.
- Folder portals that show a folder's contents live.
- Double-click the empty desktop to hide everything.
- Frosted glass from a blurred copy of the wallpaper, in Windows' dark or light theme.
- Multi-monitor layouts that survive resolution, scaling and taskbar changes.
- Daily and manual snapshots, and Restore desktop.
- Import from Stardock Fences.
- Settings in English and Arabic, start with Windows, and automatic updates from GitHub Releases.

[0.1.0]: https://github.com/medovanx/shelves/releases/tag/v0.1.0
