param(
    [int]$DesktopIndex = -1,
    [switch]$GetDesktopCount,
    [string]$ProcessName,
    [string]$WindowTitle,
    [switch]$WaitForWindow,
    [ValidateRange(1, 300)]
    [int]$TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'

function Get-VirtualDesktopCount {
    $desktopProbeSource = @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class VirtualDesktopCountProbe
{
    private static readonly Guid ImmersiveShellClassId = new Guid("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    private static readonly Guid ServiceProviderInterfaceId = new Guid("6D5140C1-7436-11CE-8034-00AA006009FA");
    private static readonly Guid VirtualDesktopManagerServiceId = new Guid("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");
    private const uint ClsctxAll = 0x17;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryServiceDelegate(IntPtr self, ref Guid serviceId, ref Guid interfaceId, out IntPtr result);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetCountDelegate(IntPtr self, out int count);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetCountWithWindowDelegate(IntPtr self, IntPtr window, out int count);

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int CoCreateInstance(
        ref Guid classId,
        IntPtr outer,
        uint context,
        ref Guid interfaceId,
        out IntPtr instance);

    public static int GetCount()
    {
        IntPtr serviceProvider = IntPtr.Zero;
        IntPtr manager = IntPtr.Zero;
        try
        {
            Guid classId = ImmersiveShellClassId;
            Guid providerId = ServiceProviderInterfaceId;
            int result = CoCreateInstance(ref classId, IntPtr.Zero, ClsctxAll, ref providerId, out serviceProvider);
            Marshal.ThrowExceptionForHR(result);

            var queryService = Marshal.GetDelegateForFunctionPointer<QueryServiceDelegate>(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(serviceProvider), 3 * IntPtr.Size));

            int build = Environment.OSVersion.Version.Build;
            Guid[] supportedInterfaces = build >= 26100
                ? new[]
                {
                    new Guid("53F5CA0B-158F-4124-900C-057158060B27"),
                    new Guid("4970BA3D-FD4E-4647-BEA3-D89076EF4B9C"),
                    new Guid("A3175F2D-239C-4BD2-8AA0-EEBA8B0B138E"),
                    new Guid("B2F925B9-5A0F-4D2E-9F4D-2B1507593C10"),
                    new Guid("F31574D6-B682-4CDC-BD56-1827860ABEC6")
                }
                : build >= 22631
                    ? new[]
                    {
                        new Guid("4970BA3D-FD4E-4647-BEA3-D89076EF4B9C"),
                        new Guid("A3175F2D-239C-4BD2-8AA0-EEBA8B0B138E"),
                        new Guid("B2F925B9-5A0F-4D2E-9F4D-2B1507593C10"),
                        new Guid("F31574D6-B682-4CDC-BD56-1827860ABEC6")
                    }
                    : build >= 22621
                        ? new[]
                        {
                            new Guid("A3175F2D-239C-4BD2-8AA0-EEBA8B0B138E"),
                            new Guid("B2F925B9-5A0F-4D2E-9F4D-2B1507593C10"),
                            new Guid("F31574D6-B682-4CDC-BD56-1827860ABEC6")
                        }
                        : build >= 22000
                            ? new[]
                            {
                                new Guid("B2F925B9-5A0F-4D2E-9F4D-2B1507593C10"),
                                new Guid("F31574D6-B682-4CDC-BD56-1827860ABEC6")
                            }
                            : build >= 14393
                                ? new[] { new Guid("F31574D6-B682-4CDC-BD56-1827860ABEC6") }
                                : new[] { new Guid("AF8DA486-95BB-4460-B3B7-6E7A6B2962B5") };

            int lastError = unchecked((int)0x80004002);
            foreach (Guid candidateInterfaceId in supportedInterfaces)
            {
                Guid serviceId = VirtualDesktopManagerServiceId;
                Guid interfaceId = candidateInterfaceId;
                result = queryService(serviceProvider, ref serviceId, ref interfaceId, out manager);
                if (result < 0 || manager == IntPtr.Zero)
                {
                    lastError = result;
                    if (manager != IntPtr.Zero)
                    {
                        Marshal.Release(manager);
                        manager = IntPtr.Zero;
                    }
                    continue;
                }

                IntPtr method = Marshal.ReadIntPtr(Marshal.ReadIntPtr(manager), 3 * IntPtr.Size);
                int count;
                result = build >= 22000 && build < 22621
                    ? Marshal.GetDelegateForFunctionPointer<GetCountWithWindowDelegate>(method)(manager, IntPtr.Zero, out count)
                    : Marshal.GetDelegateForFunctionPointer<GetCountDelegate>(method)(manager, out count);
                Marshal.ThrowExceptionForHR(result);
                if (count < 1 || count > 64)
                {
                    throw new InvalidOperationException(String.Format("The virtual desktop service returned an invalid desktop count: {0}.", count));
                }

                return count;
            }

            Marshal.ThrowExceptionForHR(lastError);
            throw new InvalidOperationException("Windows virtual desktop interfaces are unavailable.");
        }
        finally
        {
            if (manager != IntPtr.Zero)
            {
                Marshal.Release(manager);
            }
            if (serviceProvider != IntPtr.Zero)
            {
                Marshal.Release(serviceProvider);
            }
        }
    }
}
'@
    Add-Type -TypeDefinition $desktopProbeSource
    return [VirtualDesktopCountProbe]::GetCount()
}

if ($GetDesktopCount) {
    try {
        [Console]::Out.WriteLine((Get-VirtualDesktopCount))
        exit 0
    } catch {
        [Console]::Error.WriteLine("Virtual desktop detection failed: $_")
        exit 1
    }
}

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

try {
    $desktopCount = Get-VirtualDesktopCount
    if ($DesktopIndex -lt 0 -or $DesktopIndex -ge $desktopCount) {
        throw "Specify a desktop index from 0 through $($desktopCount - 1)."
    }
} catch {
    Write-Error "Desktop detection failed: $_"
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

    for ($step = 0; $step -lt ($desktopCount - 1); $step++) {
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