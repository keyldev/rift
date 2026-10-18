<p align="center">
  <img src="docs/logo.png" width="96" alt="Rift">
</p>

<h1 align="center">Rift</h1>

<p align="center">
  A fast file manager for Windows with tabs and side-by-side panes.
</p>

<p align="center">
  <a href="https://github.com/keyldev/rift/actions/workflows/build.yml"><img src="https://github.com/keyldev/rift/actions/workflows/build.yml/badge.svg" alt="Build"></a>
  &nbsp;·&nbsp; English &nbsp;·&nbsp; <a href="README.ru.md">Русский</a>
</p>

![Rift, dark theme](docs/screenshot-dark.png)

<details>
<summary>Light theme</summary>

![Rift, light theme](docs/screenshot-light.png)

</details>

> Early stage. Rift already browses, copies, moves, searches and previews, and fits into Windows like Explorer does;
> next up are settings.
> See [ROADMAP.md](ROADMAP.md).

## Features

- As many panes as you like, split side by side or stacked, each with its own tabs and history
- Drag tabs between panes or to a pane's edge to split it; pinned tabs; reopen closed tabs
- The session (layout, tabs, views, window) comes back after a restart; save layouts as workspaces
- Copy and move between panes: F5 / F6, drag-n-drop, or Ctrl+C / Ctrl+V
- Drag files to and from Explorer, clipboard compatible with Explorer (including Cut)
- Operations panel with progress, pause and cancel; conflict dialog with "do this for all"
- Undo (Ctrl+Z) for copy, move, rename, new folder and delete to the Recycle Bin
- Delete to the Recycle Bin or permanently; browse the Recycle Bin, restore or empty it
- Right-click menu with Rift's actions, plus Explorer's full menu (7-Zip, Git, "Send to", …) under "More options"
- System file icons, Properties and "Open with" dialogs
- Folders refresh by themselves when files change on disk
- Hidden and system files on demand (Ctrl+H)
- Inline rename, new folder
- Sort by any column, resize and pick columns; details, tiles and thumbnail views
- Preview pane for images, text and code, video and PDF thumbnails
- Type-to-filter, recursive search with results in their own tab
- Command palette (Ctrl+K) for commands, recent folders, tabs and paths
- Editable address bar with autocomplete, breadcrumbs, back / forward / up
- Sidebar: pinned folders, quick access, This PC, Libraries, Network, Recycle Bin, drives
- Dark and light themes, custom title bar
- One window: "Open in Rift" and the taskbar jump list (recent folders) open tabs in the running Rift
- "Open in Rift" in Explorer's context menu
- English and Russian UI

## Download

Grab the latest build from the artifacts of the latest [CI run](https://github.com/keyldev/rift/actions/workflows/build.yml)
(`rift-win-x64`: a single `Rift.exe`, no .NET install needed).

## Keyboard

| Key | Action |
| --- | --- |
| Tab / Shift+Tab | Next / previous pane |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous tab |
| Ctrl+\\ / Ctrl+Shift+\\ | Split right / down |
| Ctrl+Shift+Enter | Show only this pane / all panes |
| Ctrl+Shift+W | Close pane |
| Ctrl+Shift+T | Reopen closed tab |
| F5 / F6 | Copy / move selection to the other pane |
| Ctrl+C / Ctrl+X / Ctrl+V | Copy / cut / paste files (works with Explorer) |
| Del / Shift+Del | Move to Recycle Bin / delete permanently |
| F2 | Rename |
| Ctrl+Shift+N | New folder |
| Ctrl+Z | Undo (also brings files back from the Recycle Bin) |
| Shift+F10, Menu key | Context menu; Shift+right-click opens Explorer's menu directly |
| Alt+Enter | Properties |
| Ctrl+Shift+C | Copy path |
| Ctrl+H | Show / hide hidden files |
| Ctrl+K, Ctrl+Shift+P | Command palette |
| Ctrl+F, Ctrl+E | Search in this folder and below |
| Ctrl+L, Alt+D, F4 | Edit the path |
| Alt+P | Preview pane |
| Ctrl+Shift+1 / 2 / 3, Ctrl+wheel | Details / tiles / thumbnails |
| Typing in the list | Filter; Esc clears |
| Ctrl+T / Ctrl+W | New tab / close tab |
| Enter, double click | Open |
| Backspace, Alt+↑ | Up |
| Alt+← / Alt+→ | Back / forward |
| Ctrl+R | Refresh |

Dragging: same drive moves, another drive copies; hold Ctrl to copy, Shift to move. Drop on a folder row to put files inside it.

## Command line

```bash
Rift.exe C:\Projects --right D:\Backup
```

- Without paths Rift brings back the last session. With paths it opens exactly those, each as a tab:
  bare paths and `--left` go to the first pane, `--right` to a second one.
- If Rift is already running, the paths open as new tabs in its window; `--new-window` starts a separate window instead.
- `shell:MyComputerFolder`, `shell:RecycleBinFolder`, `shell:Libraries` and `shell:NetworkPlacesFolder` open This PC,
  the Recycle Bin, Libraries and Network.
- `--lang en` or `--lang ru` overrides the UI language (by default Rift follows Windows).
- `--register-shell` / `--unregister-shell` add or remove "Open in Rift" in Explorer.

## "Open in Rift" in Explorer

Turn it on with the checkbox at the bottom of the sidebar, or run:

```bash
Rift.exe --register-shell
```

The item shows up for folders, empty space inside a folder and drives. It's written to `HKCU`, so no admin rights are needed.
On Windows 11 it's under "Show more options". The menu points to the `Rift.exe` it was enabled from, so re-enable it after moving the exe.

## Building

Needs the .NET 10 SDK.

```bash
dotnet run --project src/Rift.App
```

```bash
dotnet test
```

### Stack

- .NET 10, C#
- [Avalonia 12](https://avaloniaui.net/) + Fluent theme
- CommunityToolkit.Mvvm

### Layout

```
src/Rift.App     UI: windows, views, themes, view models
src/Rift.Core    logic without UI: folders, paths, history, file operations, undo, localization
src/Rift.Shell   Windows integration: shell menus and dialogs, icons, Recycle Bin, clipboard, jump list, Explorer menu
tests/           Rift.Core tests
design/          interactive design mockup (React), open design/preview.html
scripts/         icon and demo files for screenshots
docs/            logo and screenshots
```

### Translations

UI strings live in `src/Rift.Core/Localization`: `Strings.resx` (English) and `Strings.ru.resx`.
To add a language, add `Strings.<culture>.resx` with the same keys and list the culture in `Strings.Supported`;
a test checks that every translation has every key.

### Screenshots and icon

Screenshots are taken on mock files, never on real folders:

```bash
powershell -File scripts/demo-fixture.ps1
```

```bash
dotnet run --project src/Rift.App -- --left C:\RiftDemo\Downloads --left C:\RiftDemo\Projects\rift --right C:\RiftDemo\Backup --right C:\RiftDemo\Media
```

```bash
powershell -File scripts/demo-fixture.ps1 -Remove
```

The app icon is generated by `python scripts/make-icon.py` (needs Pillow).

## License

[MIT](LICENSE)
