# Workspace Launcher

Workspace Launcher is a Windows 10/11 x64 desktop app for defining reusable groups of applications and websites, then opening them across Windows virtual desktops. It supports configurable workspace profiles and a separate, ordered launch sequence.

## Install the app

Download and run `WorkspaceLauncher-Setup-win-x64.exe`. The setup wizard installs the app for the current Windows user, creates a Start Menu shortcut, and offers an optional desktop shortcut. Administrator access and the .NET SDK are not required.

The installer is for Windows x64. The app is not currently code-signed, so Windows SmartScreen may show a warning when you or other users run the setup file.

To update, run a newer setup file and close the app first. To uninstall, use **Settings > Apps > Installed apps** (or **Apps & features**) and uninstall **Workspace Launcher**. Uninstalling removes the installed app and shortcuts, but leaves your workspace configuration in your user profile.

## Use the app

On first run, the app starts with no workspaces, launch items, or websites. Add only the profiles and launch targets you want. Updating an existing installation preserves its saved configuration; users who already have the previous sample profiles can delete them in the app, including the last profile, to return to a clean slate.

### Edit workspaces

- Choose **+ New** to create a workspace; edit its name and choose a virtual desktop.
- Choose **+ App or shortcut** to browse for an `.exe` or `.lnk` file.
- Choose **Add website** to open a dialog. Enter a display name and a complete `http://` or `https://` address, then confirm to add it.
- Edit launch-item names, targets, and optional process names in the table. Process names are used to skip an app that is already running. If left blank, the app infers the process name from an `.exe` target when possible.
- Select an item and use **Move up** or **Move down** to change its launch order. Use **Remove** to delete the selected item, then choose **Save changes**.
- Choose **Delete workspace** to remove the selected profile. The workspace list may be left empty.

Hover over controls for brief tips. When there are no workspaces or the selected workspace has no launch items, the app shows a getting-started hint.

### Configure and launch a sequence

Choose **Configure sequence** to move profiles into or out of the sequence and arrange their order. The sequence can contain any subset of your workspaces. Choose **Save sequence** to save it, then **Launch sequence** to run each selected workspace in order. Each workspace switches to its assigned desktop before opening its items; the app remains on the last workspace's desktop afterward.

You can also choose **Launch workspace** in the editor to run just the selected workspace.

During launch, websites open in the default browser. Applications already running under their configured process name are skipped. After starting a process-detectable app, the launcher waits up to 60 seconds for its window; if no matching window appears, it reports a warning and continues. Missing application files and invalid websites are reported in Launch Activity.

## Virtual desktop behavior

The app detects the current number of Windows virtual desktops when it starts and only offers those desktops in workspace settings. If the count has decreased since a workspace was configured, its desktop is adjusted to the last available desktop and the configuration is saved. Restart the app after adding or removing virtual desktops to refresh the options.

Desktop detection and switching use Windows shell interfaces plus keyboard input. The installed helper is `workspace_desktop.ps1`, stored beside the app executable. If detection or switching is unavailable on a particular Windows build, the app reports an error rather than silently choosing a desktop.

## Developer: run locally

Requirements: Windows 10/11 x64 and the .NET 10 SDK.

From the repository root, run:

```powershell
dotnet run --project WorkspaceLauncher\WorkspaceLauncher.csproj
```

## Developer: build the installer

Requirements: Windows, the .NET 10 SDK, and Inno Setup 6.

Build the self-contained x64 setup program from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Publish-WorkspaceLauncher.ps1
```

The script publishes the app, includes its PowerShell helper, and builds:

```text
artifacts\WorkspaceLauncher-Setup-win-x64.exe
```

Share that single setup file with users. Intermediate publish files are stored in `artifacts\WorkspaceLauncher-win-x64`; the `artifacts` directory is excluded from Git.

## Configuration and legacy launcher

Workspace settings are stored per user at:

```text
%LOCALAPPDATA%\WorkspaceLauncher\workspaces.json
```

The original `workspace_launcher.bat` remains in the repository as a legacy command-line launcher with its original fixed app groups. Its GitHub link is generic, and its YouTube Music shortcut lookup uses the current user's `%APPDATA%` path. The WPF app is the recommended way to configure profiles and sequences.
