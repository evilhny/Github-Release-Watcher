# GitHub Release Watcher

A lightweight Windows desktop app built with C# and WPF. It polls the GitHub REST API for releases, keeps local release history, and can notify when new releases are detected.

## Features

- Add public repositories as `owner/repo` or paste a GitHub repository URL.
- Configurable polling interval from 1 to 1440 minutes.
- **Check now** scans enabled repositories immediately.
- Release history with repository, version/tag, publication date, and clickable release URL (double-click a row or select it and use **Open selected release**).
- Native Windows system-tray balloon notifications for new releases after the first baseline scan (no Toast Toolkit dependency).
- Minimize to the system tray, tray menu, and optional run-at-startup registry entry.
- JSON settings stored under `%LOCALAPPDATA%\GitHubReleaseWatcher\settings.json`.
- Includes/excludes prereleases toggle.

## Requirements

- Windows 10/11 x64.
- .NET 10 SDK to build from source.
- Internet access to `api.github.com`.

## Build and run

1. Install the .NET 10 SDK.
2. Open a terminal in this folder.
3. Run:

   ```powershell
   dotnet restore
   dotnet run
   ```

## Publish a self-contained Windows x64 build

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The published app will be under `bin\Release\net10.0-windows\win-x64\publish\`.

## Behavior notes

- The first successful scan establishes a baseline for each repository. Existing releases are added to history without generating notifications. New releases detected on later scans generate notifications.
- The app asks GitHub for the latest 20 releases per repository. Releases older than that window can be missed if many releases are published between scans.
- Public GitHub API requests are unauthenticated. GitHub's normal unauthenticated rate limit applies, so avoid very short intervals when monitoring many repositories.
- Settings are stored locally. No token or credentials are needed for public repositories.
- On closing the main window, the app hides to the tray if that option is enabled. Use the tray menu's **Exit** command to stop it fully.
- Notifications use the tray icon’s native Windows balloon mechanism. Windows may present or suppress these according to taskbar notification settings and Windows version. These are legacy tray balloons, not modern Action Center toast notifications.
