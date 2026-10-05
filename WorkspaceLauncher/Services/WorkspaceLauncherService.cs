using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using WorkspaceLauncher.Models;

namespace WorkspaceLauncher.Services;

public sealed class WorkspaceLauncherService
{
    public async Task LaunchProfileAsync(WorkspaceProfile profile, Action<string> report)
    {
        report($"Switching to desktop {profile.DesktopIndex + 1} for {profile.Name}...");
        await RunDesktopHelperAsync(profile.DesktopIndex);
        await LaunchItemsAsync(profile, report);
    }

    public async Task LaunchSequenceAsync(IEnumerable<WorkspaceProfile> profiles, Action<string> report)
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
    }

    private static async Task LaunchItemsAsync(WorkspaceProfile profile, Action<string> report)
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
            if (processName.Length > 0 && IsRunning(processName))
            {
                report($"{item.Name} is already running; skipped.");
                continue;
            }

            Process.Start(new ProcessStartInfo(item.Target) { UseShellExecute = true });
            report($"Launched {item.Name}.");

            if (processName.Length > 0)
            {
                await WaitForWindowAsync(processName, item.WindowTitle, item.Name, report);
            }
        }
    }

    private static async Task RunDesktopHelperAsync(int desktopIndex)
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

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start PowerShell for desktop switching.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string error = await errorTask;
        _ = await outputTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? $"Desktop switching failed with exit code {process.ExitCode}."
                : error.Trim());
        }
    }

    private static async Task WaitForWindowAsync(
        string processName,
        string windowTitle,
        string appName,
        Action<string> report)
    {
        string scriptPath = Path.Combine(AppContext.BaseDirectory, "workspace_desktop.ps1");
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
        startInfo.ArgumentList.Add("-WaitForWindow");
        startInfo.ArgumentList.Add("-TimeoutSeconds");
        startInfo.ArgumentList.Add("60");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not wait for the {appName} window.");
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        _ = await outputTask;
        string error = await errorTask;
        if (process.ExitCode != 0)
        {
            report($"WARNING: {appName} launched, but its window was not detected within 60 seconds. {error.Trim()}");
        }
    }

    private static bool IsRunning(string processName)
    {
        string normalizedName = Path.GetFileNameWithoutExtension(processName);
        Process[] processes = Process.GetProcessesByName(normalizedName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
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
