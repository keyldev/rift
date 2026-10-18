# Rift roadmap

What's done and what's next. Stages are in priority order: first everything Rift needs to replace Explorer day to day.

## Done — 0.1

- [x] Two panes side by side, each with its own tabs and history
- [x] Navigation: breadcrumbs, back / forward / up, open files with the default app
- [x] Quick access and drives in the sidebar
- [x] Dark and light themes, custom title bar, app icon
- [x] Startup arguments `--left` / `--right`
- [x] "Open in Rift" in Explorer's context menu (folders, folder background, drives)
- [x] CI: build, tests, single-file `Rift.exe` artifact

## Done — 0.2: file operations

The main feature: throw files from one pane to the other.

- [x] Copy / move to the other pane: F5 / F6, like Total Commander (refresh moved to Ctrl+R)
- [x] Drag-n-drop between panes: drop zone, onto a folder row, Ctrl copies, Shift moves
- [x] Drag-n-drop with Explorer and other apps, both ways
- [x] Clipboard Ctrl+C / Ctrl+X / Ctrl+V, compatible with Explorer (CF_HDROP + Preferred DropEffect)
- [x] Delete to Recycle Bin (Del) and permanently (Shift+Del, with confirmation)
- [x] Inline rename (F2), new folder (Ctrl+Shift+N)
- [x] Operations panel with progress, pause and cancel
- [x] Conflict dialog: replace, skip, keep both, "do this for all"
- [x] Undo (Ctrl+Z) for copy, move, rename and new folder
- [x] UI in English and Russian (follows the Windows language, `--lang en|ru` overrides)

Decided against `IFileOperation`: Rift has its own copy engine so it can show its own progress,
pause, conflicts and undo. The Recycle Bin still goes through the shell.

## 0.3 — Windows integration

- [ ] Right-click context menu: Rift's quick actions + "More…" with the native menu (`IContextMenu`: 7-Zip, Git, …)
- [ ] System file icons and thumbnails (`IShellItemImageFactory`), loaded async and cached
- [ ] File properties dialog (`SHObjectProperties`), "Open with"
- [ ] Special folders: This PC, Recycle Bin, Network, libraries
- [ ] Auto-refresh when files change on disk (`FileSystemWatcher`)
- [ ] Show hidden and system files toggle
- [ ] Single instance: "Open in Rift" opens a new tab in the running window
- [ ] Top-level "Open in Rift" in the Windows 11 menu (`IExplorerCommand` + sparse package)
- [ ] Taskbar jump list with recent folders
- [ ] Restore from the Recycle Bin with Ctrl+Z

## 0.4 — Views and search

- [ ] Sort by column, resize and pick columns
- [ ] View modes: details, tiles, thumbnails
- [ ] Preview pane: images, text, video, PDF
- [ ] Type-to-filter in the current folder
- [ ] Command palette (Ctrl+K): commands, folders, recent places
- [ ] Recursive search with results in their own tab
- [ ] Editable address bar (click the path → type), autocomplete
- [ ] Favorites and tags in the sidebar

## 0.5 — Layouts and tabs

- [ ] More than two panes, vertical and horizontal splits (like editor groups in VS Code)
- [ ] Drag tabs between panes and to an edge to create a split
- [ ] Workspaces: saved sets of tabs and layouts
- [ ] Restore the session after restart
- [ ] Pinned tabs, duplicate tab, "close others"

## 0.6 — Settings

- [ ] Settings screen; stored in `%AppData%\Rift\settings.json`
- [ ] Accent color (azure, amber, mint, iris, rose), "follow system" theme
- [ ] Language picker
- [ ] Custom hotkeys
- [ ] Mica / Acrylic background on Windows 11

## Speed

Done along the way, but with measurements.

- [ ] Benchmark: a folder with 100,000 files opens without stutter
- [ ] Cold start under 300 ms, Native AOT
- [ ] Lazy icons and sizes, never blocking the UI
- [ ] Network shares and slow drives don't freeze the window
- [ ] Faster large-file copies with `CopyFile2` and its progress callback

## Distribution

- [ ] Releases from CI on tag: zip + installer
- [ ] Installer with auto-update (Velopack or MSIX)
- [ ] winget package
- [ ] Signed binaries

## Quality

- [ ] UI tests with Avalonia.Headless
- [ ] Logs and crash reports
- [ ] Accessibility: full keyboard navigation, screen readers
