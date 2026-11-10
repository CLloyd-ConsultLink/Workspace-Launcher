using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using WorkspaceLauncher.Models;

namespace WorkspaceLauncher.Services;

public sealed class WorkspaceLauncherService
{
    private const int WindowWaitTimeoutSeconds = 120;

    public async Task LaunchProfileAsync(WorkspaceProfile profile, Action<string> report)
    {
        report($"Switching to desktop {profile.DesktopIndex + 1} for {profile.Name}...");
        Guid targetDesktopId = await RunDesktopHelperAsync(profile.DesktopIndex);
        await LaunchItemsAsync(profile, targetDesktopId, report);
    }

    public async Task LaunchSequenceAsync(
        IEnumerable<WorkspaceProfile> profiles,
        Action<string> report,
        int? endDesktopIndex = null)
    {
        var sequence = profiles.ToList();
        if (sequence.Count == 0)
        {
            throw new InvalidOperationException("The launch sequence is empty. Configure the sequence first.");
        }

        foreach (var profile in sequence)
        {
            await LaunchProfileAsync(profile, report);
        }

        if (endDesktopIndex is int desktopIndex)
        {
            report($"Switching to Desktop {desktopIndex + 1} after the launch sequence...");
            await RunDesktopHelperAsync(desktopIndex);
        }
    }

    public async Task CloseProfileAsync(
        WorkspaceProfile profile,
        IEnumerable<LaunchItem> selectedItems,
        Action<string> report)
    {
        LaunchItem[] items = selectedItems
            .Where(item => item.Kind is LaunchItemKind.Application or LaunchItemKind.File)
            .ToArray();
        if (items.Length == 0)
        {
            report("No application or file items were selected to close.");
            return;
        }

        report($"Requesting closure of selected applications and files for {profile.Name} on Desktop {profile.DesktopIndex + 1}...");
        foreach (LaunchItem item in items)
        {
            string processName = GetProcessName(item);
            if (string.IsNullOrWhiteSpace(processName))
            {
                report($"WARNING: {item.Name} cannot be closed because its process name could not be determined.");
                continue;
            }

            try
            {
                ProcessStartInfo startInfo = CreateWindowProbeStartInfo(
                    processName,
                    GetWindowTitleFilter(item),
                    waitForWindow: false);
                startInfo.ArgumentList.Add("-DesktopIndex");
                startInfo.ArgumentList.Add(profile.DesktopIndex.ToString());
                startInfo.ArgumentList.Add("-CloseWindows");

                using var process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException($"Could not request closure of {item.Name}.");
                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                string output = (await outputTask).Trim();
                string error = (await errorTask).Trim();
                if (process.ExitCode != 0)
                {
                    report($"ERROR: Could not request closure of {item.Name}. {(string.IsNullOrWhiteSpace(error) ? $"Helper exited with code {process.ExitCode}." : error)}");
                    continue;
                }

                string[] counts = output.Split('|');
                if (counts.Length != 4 ||
                    !int.TryParse(counts[0], out int matched) ||
                    !int.TryParse(counts[1], out int requested) ||
                    !int.TryParse(counts[2], out int failed) ||
                    !int.TryParse(counts[3], out int lookupFailures))
                {
                    throw new InvalidOperationException($"The window helper returned an invalid close result for {item.Name}: {output}");
                }

                if (matched == 0 && failed == 0 && lookupFailures == 0)
                {
                    report($"{item.Name}: no matching visible window was found on Desktop {profile.DesktopIndex + 1}.");
                }
                else if (failed > 0 || lookupFailures > 0)
                {
                    report($"WARNING: Requested closure of {requested} {GetWindowWord(requested)} for {item.Name}; {failed} matching window(s) could not receive a close request, and {lookupFailures} window(s) could not be checked.");
                }
                else
                {
                    report($"Requested closure of {requested} {GetWindowWord(requested)} for {item.Name} on Desktop {profile.DesktopIndex + 1}.");
                }
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
            {
                report($"ERROR: Could not request closure of {item.Name}. {exception.Message}");
            }
        }
    }

    private static async Task LaunchItemsAsync(
        WorkspaceProfile profile,
        Guid targetDesktopId,
        Action<string> report)
    {
        foreach (var item in profile.Items)
        {
            if (item.Kind == LaunchItemKind.Website)
            {
                if (!Uri.TryCreate(item.Target, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    report($"ERROR: {item.Name} has an invalid web address: {item.Target}");
                    continue;
                }

                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                report($"Opened website: {item.Name}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.Target))
            {
                report($"ERROR: {item.Name} does not have an application path.");
                continue;
            }

            bool isFile = item.Kind == LaunchItemKind.File;
            if ((isFile || LooksLikeFilePath(item.Target)) && !File.Exists(item.Target))
            {
                report($"ERROR: {item.Name} was not found: {item.Target}");
                continue;
            }

            string processName = GetProcessName(item);
            string windowTitle = GetWindowTitleFilter(item);
            if (!isFile && processName.Length > 0 &&
                await HasVisibleWindowAsync(processName, item.WindowTitle, item.Name))
            {
                report($"{item.Name} already has a visible window; skipped.");
                continue;
            }

            int rootProcessId;
            using (Process? launchedProcess = Process.Start(
                new ProcessStartInfo(item.Target) { UseShellExecute = true }))
            {
                rootProcessId = launchedProcess?.Id ?? 0;
            }
            report(isFile ? $"Opened file: {item.Name}." : $"Launched {item.Name}.");

            if (processName.Length > 0)
            {
                await WaitForWindowAsync(
                    processName,
                    windowTitle,
                    item.Name,
                    targetDesktopId,
                    profile.DesktopIndex,
                    rootProcessId,
                    report);
            }
            else
            {
                report(isFile
                    ? $"WARNING: {item.Name} was opened with its default app, but Workspace Manager could not determine the app process to place its window on Desktop {profile.DesktopIndex + 1}. Set Process name in the item settings to enable tracking."
                    : $"WARNING: {item.Name} was launched, but its window cannot be tracked. Configure a process name to place late-opening windows on the assigned desktop.");
            }
        }
    }

    private static async Task<Guid> RunDesktopHelperAsync(int desktopIndex)
    {
        string scriptPath = Path.Combine(AppContext.BaseDirectory, "workspace_desktop.ps1");
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("The desktop switching helper is missing.", scriptPath);
        }

        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-DesktopIndex");
        startInfo.ArgumentList.Add(desktopIndex.ToString());
        startInfo.ArgumentList.Add("-CaptureDesktopId");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start PowerShell for desktop switching.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string error = await errorTask;
        string output = (await outputTask).Trim();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? $"Desktop switching failed with exit code {process.ExitCode}."
                : error.Trim());
        }

        if (!Guid.TryParse(output, out Guid desktopId) || desktopId == Guid.Empty)
        {
            throw new InvalidOperationException($"Desktop switching did not return a valid desktop identifier: {output}");
        }

        return desktopId;
    }

    private static async Task WaitForWindowAsync(
        string processName,
        string windowTitle,
        string appName,
        Guid targetDesktopId,
        int desktopIndex,
        int rootProcessId,
        Action<string> report)
    {
        ProcessStartInfo startInfo = CreateWindowProbeStartInfo(
            processName,
            windowTitle,
            waitForWindow: true,
            targetDesktopId: targetDesktopId,
            rootProcessId: rootProcessId);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not wait for the {appName} window.");
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        _ = await outputTask;
        string error = await errorTask;
        if (process.ExitCode == 0)
        {
            report($"{appName} window placed on Desktop {desktopIndex + 1}.");
        }
        else if (process.ExitCode == 2)
        {
            report($"WARNING: {appName} launched, but its window was not detected within {WindowWaitTimeoutSeconds} seconds and could not be placed on Desktop {desktopIndex + 1}. {error.Trim()}");
        }
        else
        {
            report($"WARNING: {appName} launched, but its window could not be placed on Desktop {desktopIndex + 1}. {error.Trim()}");
        }
    }

    private static async Task<bool> HasVisibleWindowAsync(string processName, string windowTitle, string appName)
    {
        ProcessStartInfo startInfo = CreateWindowProbeStartInfo(processName, windowTitle, waitForWindow: false);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not check whether {appName} has a visible window.");
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        _ = await outputTask;
        string error = await errorTask;

        if (process.ExitCode == 0)
        {
            return true;
        }

        if (process.ExitCode == 1 && string.IsNullOrWhiteSpace(error))
        {
            return false;
        }

        throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
            ? $"Could not check whether {appName} has a visible window (exit code {process.ExitCode})."
            : error.Trim());
    }

    private static ProcessStartInfo CreateWindowProbeStartInfo(
        string processName,
        string windowTitle,
        bool waitForWindow,
        Guid? targetDesktopId = null,
        int rootProcessId = 0)
    {
        string scriptPath = Path.Combine(AppContext.BaseDirectory, "workspace_desktop.ps1");
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("The desktop switching helper is missing.", scriptPath);
        }

        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-ProcessName");
        startInfo.ArgumentList.Add(processName);
        if (!string.IsNullOrWhiteSpace(windowTitle))
        {
            startInfo.ArgumentList.Add("-WindowTitle");
            startInfo.ArgumentList.Add(windowTitle);
        }

        if (waitForWindow)
        {
            startInfo.ArgumentList.Add("-WaitForWindow");
            startInfo.ArgumentList.Add("-TimeoutSeconds");
            startInfo.ArgumentList.Add(WindowWaitTimeoutSeconds.ToString());
        }

        if (targetDesktopId is Guid desktopId)
        {
            startInfo.ArgumentList.Add("-MoveToDesktopId");
            startInfo.ArgumentList.Add(desktopId.ToString());
        }

        if (rootProcessId > 0)
        {
            startInfo.ArgumentList.Add("-RootProcessId");
            startInfo.ArgumentList.Add(rootProcessId.ToString());
        }

        return startInfo;
    }

    private static string GetProcessName(LaunchItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.ProcessName))
        {
            return Path.GetFileNameWithoutExtension(item.ProcessName.Trim());
        }

        if (item.Kind == LaunchItemKind.File)
        {
            return TryGetAssociatedProcessName(item.Target);
        }

        string extension = Path.GetExtension(item.Target);
        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileNameWithoutExtension(item.Target);
        }

        if (!extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        return TryGetShortcutProcessName(item.Target);
    }

    private static string GetWindowTitleFilter(LaunchItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.WindowTitle))
        {
            return item.WindowTitle.Trim();
        }

        return item.Kind == LaunchItemKind.File
            ? Path.GetFileNameWithoutExtension(item.Target)
            : "";
    }

    private static string GetWindowWord(int count) => count == 1 ? "window" : "windows";

    private static string TryGetAssociatedProcessName(string filePath)
    {
        string extension = Path.GetExtension(filePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "";
        }

        uint bufferLength = 0;
        int result = AssocQueryString(0, 2, extension, null, null, ref bufferLength);
        if (bufferLength == 0 || bufferLength > 32768)
        {
            return "";
        }

        var executablePath = new StringBuilder((int)bufferLength);
        result = AssocQueryString(0, 2, extension, null, executablePath, ref bufferLength);
        if (result < 0)
        {
            return "";
        }

        string path = executablePath.ToString().Trim().Trim('"');
        return string.IsNullOrWhiteSpace(path) ? "" : Path.GetFileNameWithoutExtension(path);
    }

    [DllImport("Shlwapi.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int AssocQueryString(
        uint flags,
        uint associationString,
        string association,
        string? extra,
        StringBuilder? output,
        ref uint outputLength);

    private static string TryGetShortcutProcessName(string shortcutPath)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return "";
            }

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return "";
            }

            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shell,
                [shortcutPath]);
            string? target = shortcut?.GetType().InvokeMember(
                "TargetPath",
                System.Reflection.BindingFlags.GetProperty,
                null,
                shortcut,
                null) as string;
            return string.IsNullOrWhiteSpace(target) ? "" : Path.GetFileNameWithoutExtension(target);
        }
        catch (COMException)
        {
            return "";
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                Marshal.FinalReleaseComObject(shortcut);
            }
            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static bool LooksLikeFilePath(string target)
    {
        return Path.IsPathRooted(target) ||
               target.Contains(Path.DirectorySeparatorChar) ||
               target.Contains(Path.AltDirectorySeparatorChar);
    }
}
