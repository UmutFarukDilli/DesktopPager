# DesktopPager

DesktopPager is a lightweight utility that gives you multiple desktops ("Pages") on Windows. Unlike Windows virtual desktops, each page has its **own desktop files, icons, and wallpaper** — switch contexts instantly with a single click or hotkey.

## How It Works

DesktopPager converts your Desktop folder into a directory junction that points to the active page's folder. Switching pages simply retargets the junction — no files are copied or moved, so switching is instant. Icon layouts are saved and restored per page.

Your files live in `%USERPROFILE%\DesktopPager\Pages\PageN` and are always accessible even when the app is not running.

VIDEO
https://www.youtube.com/watch?v=fRWqS3EL9pA

<img width="1920" height="1080" alt="image" src="https://github.com/user-attachments/assets/e7fe6a67-37ad-4dd4-bf9e-34fa88a16ea0" />

<img width="1920" height="1080" alt="image" src="https://github.com/user-attachments/assets/611e4d87-01ed-4e70-88da-2998bed2c612" />

<img width="1920" height="1080" alt="image" src="https://github.com/user-attachments/assets/55f90943-0e81-4176-a211-6a5c9f0c8f22" />

<img width="1920" height="1080" alt="image" src="https://github.com/user-attachments/assets/07074997-9ef5-4464-8242-5fda03d93f7c" />

## Features

*   **Unlimited pages** — create as many desktop pages as you need
*   **Per-page wallpapers** — static images or [Wallpaper Engine](https://www.wallpaperengine.io/) wallpapers (Workshop IDs supported)
*   **Icon layout memory** — each page remembers its icon positions
*   **CLI support** — automate page switching with command-line arguments
*   **Safe restore** — a separate restorer tool returns your Desktop to its original state at any time

## Installation

1.  Download `DesktopPager.exe` (and optionally `DesktopPager_Restorer.exe`) from the [Releases](../../releases) page, or build from source (see below).
2.  Run `DesktopPager.exe` and confirm the activation prompt.

> **Note:** Page data, settings, and logs are stored in `%USERPROFILE%\DesktopPager`.

> [!WARNING]
> 1. OneDrive desktop sync must be turned off.
> 2. If you have **important** files in **your desktop**, put them another folder in your disk before the **First Launch or before the Uninstallation.**

## Usage

### UI

*   **Click** a page card to switch to that desktop.
*   Use the buttons on each card to **rename**, **set wallpaper**, or **delete** a page.
*   The wallpaper menu supports static images, Wallpaper Engine Workshop IDs/URLs, applying to all pages, and removal.

### Command Line

| Command | Description |
| --- | --- |
| `DesktopPager.exe <number>` | Switch to a specific page |
| `DesktopPager.exe next` / `prev` | Switch to the next / previous page |
| `DesktopPager.exe new` | Create a new page and switch to it |
| `DesktopPager.exe set-wallpaper <path>` | Set a static wallpaper for the current page |
| `DesktopPager.exe set-we <id_or_path>` | Set a Wallpaper Engine wallpaper for the current page |
| `DesktopPager.exe refresh` | Refresh the desktop |

### Restoring Your Desktop

Run `DesktopPager_Restorer.exe` to deactivate DesktopPager. It stops the app, removes the Desktop junction, and restores your Desktop as a normal folder. Your files stay safe in `%USERPROFILE%\DesktopPager\Pages` and a shortcut to them is placed on your Desktop.

## Wallpaper Engine Integration

If Wallpaper Engine is installed (detected via common paths and the Steam registry entry), DesktopPager uses it for all wallpapers:

*   **Workshop IDs** — enter a Steam Workshop ID (e.g. `2581056441`) or a workshop URL; the app resolves the matching `project.json`, `scene.pkg`, or video file automatically.
*   **Static images** — applied through Wallpaper Engine when available for seamless transitions, otherwise via the standard Windows wallpaper API.

## Building from Source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows.

```powershell
# Main application
dotnet publish DesktopPager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish

# Restorer / uninstaller
dotnet publish Uninstaller.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

## Project Structure

| File | Purpose |
| --- | --- |
| `Program.cs` | Entry point, single-instance guard, CLI handling |
| `PageManager.cs` | Core page logic: junctions, migration, layouts |
| `WallpaperManager.cs` | Per-page wallpapers, Wallpaper Engine integration |
| `DesktopIcons.cs` | Saving/restoring desktop icon positions |
| `IconGenerator.cs` | Runtime generation of app/page icons |
| `NativeMethods.cs` | Win32 interop (shell notifications, wallpaper API) |
| `MainWindow.xaml(.cs)` | WPF user interface |
| `Uninstaller.cs` | Standalone restorer tool (`DesktopPager_Restorer`) |

## Warnings

*   DesktopPager **replaces your Desktop folder with a junction**. This is reversible at any time via the restorer, but close any open Desktop files before activating or restoring.
*   OneDrive-redirected Desktops are detected, but syncing a junctioned Desktop may cause unexpected OneDrive behavior. Consider pausing Desktop sync.

## Contributing

Issues and pull requests are welcome.

## License

This project is licensed under the [MIT License](LICENSE).
