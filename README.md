# Workspace Launcher

Workspace Launcher is a native Windows desktop app for configuring and launching groups of applications and websites across Windows virtual desktops.

## Run locally

1. Install the .NET 10 SDK.
2. Open a terminal in the repository root.
3. Run `dotnet run --project WorkspaceLauncher\WorkspaceLauncher.csproj`.

The first run creates Admin, Work, and Entertainment workspaces based on the existing launcher script. Workspace settings are stored at `%LOCALAPPDATA%\WorkspaceLauncher\workspaces.json`.

## Configure workspaces

- Select **+ New** to create a workspace. Edit its name and choose Desktop 1, 2, or 3.
- Use **+ App or shortcut** to add an `.exe` or `.lnk`; enter a website address and choose **Add website** for web links.
- Edit launch-item names, targets, and optional process names in the table. Process names let the app detect an already-running app and skip relaunching it.
- Use **Move up** and **Move down** to arrange each workspace's launch items, then save changes.
- Choose **Configure sequence** to select which workspaces to include and set their launch order. Use **Launch sequence** to start only those workspaces in that order.

The app detects the available virtual desktops on Windows 10/11 when it starts and offers only those desktops in workspace settings. Restart the app after adding or removing virtual desktops. It includes `workspace_desktop.ps1` as its virtual-desktop and visible-window helper. The original `workspace_launcher.bat` remains available as a legacy command-line launcher.
