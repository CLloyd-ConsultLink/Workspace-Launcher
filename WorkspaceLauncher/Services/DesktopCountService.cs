using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace WorkspaceLauncher.Services;

public sealed class DesktopCountService
{
    public int GetDesktopCount()
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
        startInfo.ArgumentList.Add("-GetDesktopCount");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start PowerShell to detect virtual desktops.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        string output = outputTask.GetAwaiter().GetResult().Trim();
        string error = errorTask.GetAwaiter().GetResult().Trim();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? $"Virtual desktop detection failed with exit code {process.ExitCode}."
                : error);
        }

        if (!int.TryParse(output, NumberStyles.None, CultureInfo.InvariantCulture, out int count) ||
            count < 1 || count > 64)
        {
            throw new InvalidOperationException($"Virtual desktop detection returned an invalid count: {output}");
        }

        return count;
    }
}
