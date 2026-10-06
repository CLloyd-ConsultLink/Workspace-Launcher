using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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

            if (LooksLikeFilePath(item.Target) && !File.Exists(item.Target))
            {
                report($"ERROR: {item.Name} was not found: {item.Target}");
                continue;
            }

            string processName = GetProcessName(item);
            if (processName.Length > 0 &&
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
            report($"Launched {item.Name}.");

            if (processName.Length > 0)
            {
                await WaitForWindowAsync(
                    processName,
                    item.WindowTitle,
                    item.Name,
                    targetDesktopId,
                    profile.DesktopIndex,
                    rootProcessId,
                    report);
            }
            else
            {
                report($"WARNING: {item.Name} was launched, but its window cannot be tracked. Configure a process name to place late-opening windows on the assigned desktop.");
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
