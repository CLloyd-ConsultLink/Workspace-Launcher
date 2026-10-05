param(
    [int]$DesktopIndex = -1,
    [string]$ProcessName,
    [string]$WindowTitle,
    [switch]$WaitForWindow,
    [ValidateRange(1, 300)]
    [int]$TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'

if ($ProcessName) {
    $windowProbeSource = @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class VisibleWindowProbe
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr extraData);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int valueSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    public static bool HasWindow(string processName, string titlePart)
    {
        bool found = false;
        EnumWindows((hwnd, data) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            int cloaked;
            if (DwmGetWindowAttribute(hwnd, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0)
            {
                return true;
            }

            StringBuilder title = new StringBuilder(512);
            GetWindowText(hwnd, title, title.Capacity);
            if (title.Length == 0 || (!String.IsNullOrEmpty(titlePart) &&
                title.ToString().IndexOf(titlePart, StringComparison.OrdinalIgnoreCase) < 0))
            {
                return true;
            }

            uint processId;
            GetWindowThreadProcessId(hwnd, out processId);
            try
            {
                using (var process = System.Diagnostics.Process.GetProcessById((int)processId))
                {
                    if (String.Equals(process.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        return false;
                    }
                }
            }
            catch
            {
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }
}
'@
    Add-Type -TypeDefinition $windowProbeSource
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        if ([VisibleWindowProbe]::HasWindow($ProcessName, $WindowTitle)) {
            exit 0
        }
        if (-not $WaitForWindow) {
            exit 1
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    [Console]::Error.WriteLine("Timed out waiting for a window from $ProcessName.")
    exit 1
}

if ($DesktopIndex -lt 0 -or $DesktopIndex -gt 2) {
    Write-Error 'Specify a desktop index from 0 through 2.'
    exit 1
}

$keyboardSource = @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class VirtualDesktopHotkeys
{
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint type;
        public InputUnion data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput keyboard;
        [FieldOffset(0)] public MouseInput mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort virtualKey;
        public ushort scanCode;
        public uint flags;
        public uint time;
        public UIntPtr extraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint flags;
        public uint time;
        public UIntPtr extraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    public static void Switch(int direction)
    {
        ushort arrowKey = direction < 0 ? (ushort)0x25 : (ushort)0x27;
        Input[] inputs = new Input[]
        {
            CreateKeyboardInput(0x5B, 0),
            CreateKeyboardInput(0x11, 0),
            CreateKeyboardInput(arrowKey, 0),
            CreateKeyboardInput(arrowKey, KeyeventfKeyup),
            CreateKeyboardInput(0x11, KeyeventfKeyup),
            CreateKeyboardInput(0x5B, KeyeventfKeyup)
        };

        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input)));
        if (sent != (uint)inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static Input CreateKeyboardInput(ushort key, uint flags)
    {
        Input input = new Input();
        input.type = InputKeyboard;
        input.data.keyboard.virtualKey = key;
        input.data.keyboard.flags = flags;
        return input;
    }
}
'@

try {
    Add-Type -TypeDefinition $keyboardSource
    Write-Output "Switching to desktop $($DesktopIndex + 1)..."

    for ($step = 0; $step -lt 2; $step++) {
        [VirtualDesktopHotkeys]::Switch(-1)
        Start-Sleep -Milliseconds 350
    }

    for ($step = 0; $step -lt $DesktopIndex; $step++) {
        [VirtualDesktopHotkeys]::Switch(1)
        Start-Sleep -Milliseconds 350
    }
} catch {
    Write-Error "Desktop switch failed: $_"
    exit 1
}