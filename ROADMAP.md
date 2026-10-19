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

## Done — 0.3: Windows integration

- [x] Right-click menu: Rift's actions + "More options" with Explorer's own menu (`IContextMenu`: 7-Zip, Git, "Send to", …);
      Shift+right-click opens Explorer's menu directly, with extended verbs
- [x] System file icons from the system image list, loaded lazily off the UI thread and cached per file type
- [x] Properties (`SHObjectProperties`, `SHMultiFileProperties`) and "Open with" (`SHOpenWithDialog`)
- [x] This PC (drives with free space) and the Recycle Bin (restore, delete permanently, empty)
- [x] Auto-refresh when files change on disk (`FileSystemWatcher`), edited in place so scroll and selection stay
- [x] Show hidden and system files (Ctrl+H), starting from Explorer's own setting
- [x] Single instance: "Open in Rift" and jump list items open new tabs in the running window (`--new-window` to opt out)
- [x] Taskbar jump list with recent folders and a "New window" task
- [x] Undo a delete to the Recycle Bin with Ctrl+Z

The Recycle Bin is read straight from `$Recycle.Bin` (`$I` metadata + `$R` data files), the way Explorer stores it:
it's fast, needs no shell UI and lets Ctrl+Z find exactly what Del just sent there.

Moved to later stages:

- Thumbnails (`IShellItemImageFactory`) → 0.4, together with the thumbnail view
- Network and libraries in the sidebar → 0.4, with favorites
- Top-level "Open in Rift" in the Windows 11 menu (`IExplorerCommand` + sparse package) → Distribution: it needs a signed package

## Done — 0.4: views and search

- [x] Sort by column (folders stay first), resize columns by dragging, pick columns (right-click the header);
      a narrow pane drops Created, Type, Modified in that order
- [x] View modes: details, tiles, thumbnails (`IShellItemImageFactory`), Ctrl+Shift+1/2/3 or Ctrl+wheel;
      tiles and thumbnails sit in Rift's own virtualizing grid, so big folders stay fast
- [x] Preview pane (Alt+P): images with their size, text and code, the shell's thumbnail for video, PDF and documents
- [x] Type-to-filter: start typing in the list; `*` and `?` work; Backspace edits, Esc clears
- [x] Command palette (Ctrl+K): commands with their shortcuts, recent folders, open tabs, places, typed paths
- [x] Recursive search (Ctrl+F) with results streaming into their own tab; file operations work on the results
- [x] Editable address bar (click the path, Ctrl+L, Alt+D, F4) with folder autocomplete, `%VARS%`, `..` and file paths
- [x] Pinned folders in the sidebar; Network (computers, a server's shares) and Libraries

Moved to later stages:

- Tags → Settings (0.6): they need Rift's own storage
- Live previews through shell preview handlers (`IPreviewHandler`) → later; for now video and PDF show the shell thumbnail

## Done — 0.5: layouts and tabs

- [x] Any number of panes in vertical and horizontal splits, like editor groups in VS Code (Ctrl+\\, Ctrl+Shift+\\);
      show just one pane with Ctrl+Shift+Enter; "the other pane" for F5/F6 is the one you were in before
- [x] Drag tabs: reorder them, drop them on another pane, or on a pane's edge to split it
- [x] Workspaces: saved sets of panes and tabs, opened from the command palette
- [x] The session comes back after a restart: layout, tabs, views, sort, window size and place
- [x] Pinned tabs, duplicate, close others / to the right, reopen closed tab (Ctrl+Shift+T), Ctrl+Tab between tabs

## Done — 0.6: settings

- [x] Settings screen (Ctrl+,), applied as you change them; stored in `%AppData%\Rift\settings.json`
- [x] Accent color (azure, ember, mint, iris, rose) and theme: dark, light or as in Windows
- [x] Language picker; Rift restarts itself and the tabs come back
- [x] Tags on files and folders: dots in the list, chips in the sidebar, a tab with everything tagged;
      tags follow renames and moves made in Rift
- [x] Column layout remembered; view and sort remembered per folder
- [x] Custom shortcuts for every command (record a key, reset one or all); the palette shows the same keys
- [x] Mica background on Windows 11 (not verified on Windows 11 yet; Windows 10 keeps a solid window)

Fixed along the way: closing the window could leave the process running without a window (Avalonia's
WinUI composition deadlocked while icons were still rendering). Rift now uses DirectComposition.
Acrylic was dropped for the same reason.

Screenshots in the README were retaken on the demo files (`scripts/demo-fixture.ps1`, now with real pictures).

## Done — 0.8: speed

Measured with `scripts/bench-fixture.ps1` (100,500 items) and `RIFT_PERF=1`, which logs folder loads and every
UI-thread stall over 50 ms to `%TEMP%\rift-perf.log`. Numbers are the longest stall, before → after.

- [x] A folder with 100,000 files opens without stutter: reading and sorting happen in the background,
      the UI thread only puts the list on screen (opening: 275 → 80 ms, ~35 ms of it Rift's own work)
- [x] Type-to-filter in that folder: 770 → 70 ms for the first letter. A change of thousands of rows builds
      a new list instead of thousands of single edits; sorted listings are kept, so a keystroke doesn't sort again
- [x] Ctrl+A and then a click or an arrow key: 6.5 s and 1 s → under 60 ms (Avalonia collapses a selection row by row;
      Rift hands it a fresh list instead). Memory with that folder open: 626 → 277 MB
- [x] Icons and thumbnails load on worker threads for the rows on screen; sizes come with the listing
- [x] Network shares and slow drives don't freeze the window: drives are read in parallel with a time limit
      (a sleeping network drive shows by its letter until it answers), folder watchers are set up in the background,
      typed paths are checked off the UI thread, and a folder that takes longer than 150 ms to read shows the busy bar
      instead of the last folder's files
- [x] Drives in the sidebar and This PC follow USB sticks and cards being plugged in and pulled out
- [x] Cold start under 300 ms, Native AOT: the window shows in ~295 ms instead of ~1.25 s, 106 MB instead of 190 MB
- [x] Faster copies with `CopyFile2` and its progress callback (2 GB: 0.8 s → 0.45 s with a warm cache)

Not reproduced here: a share that's slow to answer (this machine's SMB client fails at once), so the network
part is checked by the code paths and a unit test, not by hand.

## Distribution

- [x] Releases from CI on tag: a `v*` tag builds the AOT exe and creates a GitHub release (0.7)
- [x] Installer with auto-update, Velopack (0.9): per-user setup with no admin rights, a self-updating portable zip,
      delta updates from GitHub Releases downloaded in the background and installed on restart; "Check for updates
      automatically" in Settings; uninstalling takes "Open in Rift" out of Explorer's menu
- [ ] winget package: `scripts/winget-manifest.ps1` writes the manifests for a release; the pull request to
      microsoft/winget-pkgs is still to be made, once 0.9.0 is out
- [ ] Signed binaries
- [ ] Top-level "Open in Rift" in the Windows 11 menu (`IExplorerCommand` + signed sparse package)

## Quality

- [ ] UI tests with Avalonia.Headless
- [ ] Logs and crash reports
- [ ] Accessibility: full keyboard navigation, screen readers
