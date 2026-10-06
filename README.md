# Workspace Launcher

Workspace Launcher is a Windows 10/11 x64 desktop app for defining reusable groups of applications and websites, then opening them across Windows virtual desktops. It supports configurable workspace profiles and a separate, ordered launch sequence.

## Install the app

Download and run `WorkspaceLauncher-Setup-win-x64.exe`. The setup wizard installs the app for the current Windows user, creates a Start Menu shortcut, and offers an optional desktop shortcut. Administrator access and the .NET SDK are not required.

The installer is for Windows x64.

Workspace Launcher checks GitHub Releases for a newer stable version when it starts. If one is available, the app asks whether to download and start the installer. Choosing **No** postpones the update until the next time the app starts. Choosing **Yes** downloads the installer, checks its SHA-256 digest against GitHub's release metadata, closes Workspace Launcher, and opens the normal setup wizard. The check needs an internet connection; if GitHub cannot be reached, you can continue using the installed version and the app will check again next time. You can also update manually by running a newer setup file and closing the app first.

The SHA-256 check detects a download that differs from the file GitHub published, but it does not independently verify the publisher's identity. The setup program is not currently code-signed, so Windows SmartScreen may show a warning.

To uninstall, use **Settings > Apps > Installed apps** (or **Apps & features**) and uninstall **Workspace Launcher**. Uninstalling removes the installed app and shortcuts, but leaves your workspace configuration in your user profile.

## Use the app

On first run, the app starts with no workspaces, launch items, or websites. Add only the profiles and launch targets you want. Updating an existing installation preserves its saved configuration; users who already have the previous sample profiles can delete them in the app, including the last profile, to return to a clean slate.

### Edit workspaces

- Choose **+ New** to create a workspace; edit its name and choose a virtual desktop.
- Choose **+ Add launch item** to open one dialog, choose **Application or shortcut** or **Website**, and enter the relevant details. Use **Browse...** to select an `.exe` or `.lnk` file; process names for `.exe` files are filled in automatically when possible.
- The launch-item table is read-only. Double-click any row to edit it in the same launch-item dialog. For launchers that start the actual application as a child process, set the process name to the child app (for example, `javaw` for a Java application); the launcher tracks the launched process tree so it can find and move the child window. If an app shows a splash or launcher window before its main window, set **Window title contains** to text unique to the main window so the launcher doesn't mistake the splash for the finished app.
- Select an item and use **Move up** or **Move down** to change its launch order. Use **Remove** to delete the selected item, then choose **Save changes**.
- Choose **Delete workspace** to remove the selected profile. The workspace list may be left empty.

Hover over controls for brief tips. When there are no workspaces or the selected workspace has no launch items, the app shows a getting-started hint.

### Configure and launch a sequence

Choose **Configure sequence** to move profiles into or out of the sequence, arrange their order, and choose the desktop to leave active when the sequence finishes. The sequence can contain any subset of your workspaces. Choose **Save sequence** to save it, then **Launch sequence** to run each selected workspace in order. Each workspace switches to its assigned desktop before opening its items. By default the app remains on the last workspace's desktop; select a specific desktop in the sequence settings to end there instead.

You can also choose **Launch workspace** in the editor to run just the selected workspace.

During launch, websites open in the default browser. Applications with an existing matching visible window are skipped; background-only processes are not treated as already open. After starting an app with a detectable process name, the launcher waits up to 120 seconds for its visible window and moves it to the workspace's assigned desktop, even if you switched desktops while it was starting. The launched process and its child processes are tracked, so launchers that start the real application separately can be supported by setting its process name in the table. If the window is not detected or cannot be moved, Launch Activity reports a warning. Applications with no resolvable process name and websites opened in the default browser cannot be moved reliably. Missing application files and invalid websites are also reported in Launch Activity.

## Virtual desktop behavior

The app detects the current number of Windows virtual desktops when it starts and refreshes the desktop choices whenever you open the desktop dropdown. If the count has decreased since a workspace was configured, its desktop is adjusted to the last available desktop and the configuration is saved. You do not need to restart the app after adding or removing virtual desktops. For apps launched by the Workspace Launcher, late-opening windows are moved to the assigned desktop after they appear; this does not affect apps launched manually outside the launcher.

Desktop detection and switching use Windows shell interfaces plus keyboard input. Moving a window between processes uses an internal Windows shell interface that may vary between Windows builds. The installed helper is `workspace_desktop.ps1`, stored beside the app executable. If detection, switching, or window relocation is unavailable on a particular Windows build, the app reports an error or warning rather than silently claiming success.

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

### Publish an update

The app version in `WorkspaceLauncher\WorkspaceLauncher.csproj` is the source of truth for both the app and installer. Increase it using `major.minor.patch` format, build the installer, and publish a stable GitHub Release in `CLloyd-ConsultLink/Workspace-Launcher` with a matching `v`-prefixed tag (for example, version `1.2.0` uses tag `v1.2.0`). Attach the generated `WorkspaceLauncher-Setup-win-x64.exe` file. The app checks the latest stable release only; drafts and prereleases are not offered.

## Configuration and legacy launcher

Workspace settings are stored per user at:

```text
%LOCALAPPDATA%\WorkspaceLauncher\workspaces.json
```

The original `workspace_launcher.bat` remains in the repository as a legacy command-line launcher with its original fixed app groups. Its GitHub link is generic, and its YouTube Music shortcut lookup uses the current user's `%APPDATA%` path. The WPF app is the recommended way to configure profiles and sequences.
