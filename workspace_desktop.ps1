param(
    [int]$DesktopIndex = -1,
    [switch]$GetDesktopCount,
    [string]$ProcessName,
    [string]$WindowTitle,
    [int]$RootProcessId = 0,
    [Guid]$MoveToDesktopId = [Guid]::Empty,
    [switch]$CaptureDesktopId,
    [switch]$WaitForWindow,
    [ValidateRange(1, 300)]
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'

function Get-VirtualDesktopCount {
    $desktopProbeSource = @'
using System;
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
using System.ComponentModel;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class VisibleWindowProbe
{
    private static readonly HashSet<int> KnownProcessIds = new HashSet<int>();

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

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

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        out ProcessBasicInformation processInformation,
        int processInformationLength,
        out int returnLength);

    public static IntPtr FindWindow(
        string processName,
        string titlePart,
        bool includeCloaked,
        int rootProcessId)
    {
        HashSet<int> processIds = GetProcessTree(rootProcessId);
        IntPtr foundWindow = IntPtr.Zero;
        IntPtr matchingProcessWindow = IntPtr.Zero;
        EnumWindows((hwnd, data) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            int cloaked;
            if (!includeCloaked && DwmGetWindowAttribute(hwnd, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0)
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
                    bool matchesName = String.IsNullOrWhiteSpace(processName) ||
                        String.Equals(process.ProcessName, processName, StringComparison.OrdinalIgnoreCase);
                    bool matchesTree = rootProcessId <= 0 || processIds.Contains((int)processId);
                    if (matchesName)
                    {
                        if (matchesTree)
                        {
                            foundWindow = hwnd;
                            return false;
                        }

                        if (matchingProcessWindow == IntPtr.Zero)
                        {
                            matchingProcessWindow = hwnd;
                        }
                    }
                }
            }
            catch
            {
            }

            return true;
        }, IntPtr.Zero);

        return foundWindow != IntPtr.Zero ? foundWindow : matchingProcessWindow;
    }

    private static HashSet<int> GetProcessTree(int rootProcessId)
    {
        var processIds = new HashSet<int>(KnownProcessIds);
        if (rootProcessId <= 0)
        {
            return processIds;
        }

        processIds.Add(rootProcessId);
        KnownProcessIds.Add(rootProcessId);
        bool foundChild;
        do
        {
            foundChild = false;
            foreach (var process in System.Diagnostics.Process.GetProcesses())
            {
                using (process)
                {
                    int processId;
                    try
                    {
                        processId = process.Id;
                        if (KnownProcessIds.Contains(processId))
                        {
                            continue;
                        }

                        int? parentProcessId = GetParentProcessId(process);
                        if (parentProcessId.HasValue && processIds.Contains(parentProcessId.Value))
                        {
                            processIds.Add(processId);
                            KnownProcessIds.Add(processId);
                            foundChild = true;
                        }
                    }
                    catch
                    {
                    }
                }
            }
        } while (foundChild);

        return processIds;
    }

    private static int? GetParentProcessId(System.Diagnostics.Process process)
    {
        try
        {
            ProcessBasicInformation information;
            int returnLength;
            int status = NtQueryInformationProcess(
                process.Handle,
                0,
                out information,
                Marshal.SizeOf(typeof(ProcessBasicInformation)),
                out returnLength);
            return status == 0 ? information.InheritedFromUniqueProcessId.ToInt32() : (int?)null;
        }
        catch
        {
            return null;
        }
    }
}
'@
    $windowDesktopOperationsSource = @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class VirtualDesktopWindowOperations
{
    private static readonly Guid ImmersiveShellClassId = new Guid("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    private static readonly Guid ServiceProviderInterfaceId = new Guid("6D5140C1-7436-11CE-8034-00AA006009FA");
    private static readonly Guid VirtualDesktopManagerServiceId = new Guid("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");
    private static readonly Guid ApplicationViewCollectionId = new Guid("1841C6D7-4F9D-42C0-AF41-8747538F10E5");

    [ComImport]
    [Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(IntPtr window, [MarshalAs(UnmanagedType.Bool)] out bool isOnCurrentDesktop);

        [PreserveSig]
        int GetWindowDesktopId(IntPtr window, out Guid desktopId);

        [PreserveSig]
        int MoveWindowToDesktop(IntPtr window, ref Guid desktopId);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryServiceDelegate(IntPtr self, ref Guid serviceId, ref Guid interfaceId, out IntPtr result);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesktopArrayDelegate(IntPtr self, out IntPtr desktops);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesktopArrayWithWindowDelegate(IntPtr self, IntPtr window, out IntPtr desktops);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetArrayCountDelegate(IntPtr self, out int count);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetArrayItemDelegate(IntPtr self, int index, ref Guid interfaceId, out IntPtr item);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesktopIdDelegate(IntPtr self, out Guid desktopId);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetViewForWindowDelegate(IntPtr self, IntPtr window, out IntPtr view);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int MoveViewToDesktopDelegate(IntPtr self, IntPtr view, IntPtr desktop);

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int CoCreateInstance(
        ref Guid classId,
        IntPtr outer,
        uint context,
        ref Guid interfaceId,
        out IntPtr instance);

    [ComImport]
    [Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A")]
    private class VirtualDesktopManagerClass
    {
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    public static void MoveWindowToDesktop(long windowHandle, Guid targetDesktopId)
    {
        IntPtr window = new IntPtr(windowHandle);
        if (targetDesktopId == Guid.Empty)
        {
            throw new InvalidOperationException("The assigned virtual desktop identifier is empty.");
        }
        if (!IsWindow(window))
        {
            throw new InvalidOperationException("The detected application window no longer exists.");
        }

        IVirtualDesktopManager publicManager = (IVirtualDesktopManager)new VirtualDesktopManagerClass();
        try
        {
            Guid currentDesktopId;
            Marshal.ThrowExceptionForHR(publicManager.GetWindowDesktopId(window, out currentDesktopId));
            if (currentDesktopId == targetDesktopId)
            {
                return;
            }

            MoveWindowWithInternalManager(window, targetDesktopId);

            Guid resultingDesktopId;
            Marshal.ThrowExceptionForHR(publicManager.GetWindowDesktopId(window, out resultingDesktopId));
            if (resultingDesktopId != targetDesktopId)
            {
                throw new InvalidOperationException("Windows did not place the application window on its assigned desktop.");
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(publicManager);
        }
    }

    private static void MoveWindowWithInternalManager(IntPtr window, Guid targetDesktopId)
    {
        IntPtr serviceProvider = IntPtr.Zero;
        IntPtr desktopManager = IntPtr.Zero;
        IntPtr viewCollection = IntPtr.Zero;
        IntPtr desktops = IntPtr.Zero;
        IntPtr view = IntPtr.Zero;
        IntPtr targetDesktop = IntPtr.Zero;
        try
        {
            Guid classId = ImmersiveShellClassId;
            Guid providerId = ServiceProviderInterfaceId;
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref classId, IntPtr.Zero, 0x17, ref providerId, out serviceProvider));

            QueryServiceDelegate queryService = Marshal.GetDelegateForFunctionPointer<QueryServiceDelegate>(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(serviceProvider), 3 * IntPtr.Size));
            viewCollection = QueryService(queryService, serviceProvider, ApplicationViewCollectionId, ApplicationViewCollectionId);
            desktopManager = QueryVirtualDesktopManager(queryService, serviceProvider);

            int build = Environment.OSVersion.Version.Build;
            IntPtr getViewMethod = Marshal.ReadIntPtr(Marshal.ReadIntPtr(viewCollection), 6 * IntPtr.Size);
            int result = Marshal.GetDelegateForFunctionPointer<GetViewForWindowDelegate>(getViewMethod)(
                viewCollection,
                window,
                out view);
            Marshal.ThrowExceptionForHR(result);
            if (view == IntPtr.Zero)
            {
                throw new InvalidOperationException("Windows did not provide an application view for the detected window.");
            }

            int getDesktopsIndex = build >= 22000 && build < 22621 ? 8 : 7;
            IntPtr getDesktopsMethod = Marshal.ReadIntPtr(Marshal.ReadIntPtr(desktopManager), getDesktopsIndex * IntPtr.Size);
            if (build >= 22000 && build < 22621)
            {
                result = Marshal.GetDelegateForFunctionPointer<GetDesktopArrayWithWindowDelegate>(getDesktopsMethod)(
                    desktopManager,
                    IntPtr.Zero,
                    out desktops);
            }
            else
            {
                result = Marshal.GetDelegateForFunctionPointer<GetDesktopArrayDelegate>(getDesktopsMethod)(
                    desktopManager,
                    out desktops);
            }
            Marshal.ThrowExceptionForHR(result);
            if (desktops == IntPtr.Zero)
            {
                throw new InvalidOperationException("Windows returned no virtual desktop list.");
            }

            IntPtr getCountMethod = Marshal.ReadIntPtr(Marshal.ReadIntPtr(desktops), 3 * IntPtr.Size);
            int desktopCount;
            Marshal.ThrowExceptionForHR(Marshal.GetDelegateForFunctionPointer<GetArrayCountDelegate>(getCountMethod)(
                desktops,
                out desktopCount));

            Guid desktopInterfaceId = GetDesktopInterfaceId(build);
            IntPtr getAtMethod = Marshal.ReadIntPtr(Marshal.ReadIntPtr(desktops), 4 * IntPtr.Size);
            GetArrayItemDelegate getAt = Marshal.GetDelegateForFunctionPointer<GetArrayItemDelegate>(getAtMethod);
            for (int index = 0; index < desktopCount; index++)
            {
                IntPtr desktop = IntPtr.Zero;
                Guid requestedInterfaceId = desktopInterfaceId;
                result = getAt(desktops, index, ref requestedInterfaceId, out desktop);
                Marshal.ThrowExceptionForHR(result);
                if (desktop == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr getIdMethod = Marshal.ReadIntPtr(Marshal.ReadIntPtr(desktop), 4 * IntPtr.Size);
                Guid desktopId;
                Marshal.ThrowExceptionForHR(Marshal.GetDelegateForFunctionPointer<GetDesktopIdDelegate>(getIdMethod)(
                    desktop,
                    out desktopId));
                if (desktopId == targetDesktopId)
                {
                    targetDesktop = desktop;
                    break;
                }
                Marshal.Release(desktop);
            }

            if (targetDesktop == IntPtr.Zero)
            {
                throw new InvalidOperationException("The assigned virtual desktop no longer exists.");
            }

            IntPtr moveMethod = Marshal.ReadIntPtr(Marshal.ReadIntPtr(desktopManager), 4 * IntPtr.Size);
            Marshal.ThrowExceptionForHR(Marshal.GetDelegateForFunctionPointer<MoveViewToDesktopDelegate>(moveMethod)(
                desktopManager,
                view,
                targetDesktop));
        }
        finally
        {
            if (targetDesktop != IntPtr.Zero) Marshal.Release(targetDesktop);
            if (view != IntPtr.Zero) Marshal.Release(view);
            if (desktops != IntPtr.Zero) Marshal.Release(desktops);
            if (viewCollection != IntPtr.Zero) Marshal.Release(viewCollection);
            if (desktopManager != IntPtr.Zero) Marshal.Release(desktopManager);
            if (serviceProvider != IntPtr.Zero) Marshal.Release(serviceProvider);
        }
    }

    private static IntPtr QueryService(
        QueryServiceDelegate queryService,
        IntPtr serviceProvider,
        Guid serviceId,
        Guid interfaceId)
    {
        IntPtr service;
        int result = queryService(serviceProvider, ref serviceId, ref interfaceId, out service);
        Marshal.ThrowExceptionForHR(result);
        if (service == IntPtr.Zero)
        {
            throw new InvalidOperationException("Windows returned an empty virtual desktop service.");
        }
        return service;
    }

    private static IntPtr QueryVirtualDesktopManager(QueryServiceDelegate queryService, IntPtr serviceProvider)
    {
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
        foreach (Guid candidate in supportedInterfaces)
        {
            Guid serviceId = VirtualDesktopManagerServiceId;
            Guid interfaceId = candidate;
            IntPtr manager;
            int result = queryService(serviceProvider, ref serviceId, ref interfaceId, out manager);
            if (result >= 0 && manager != IntPtr.Zero)
            {
                return manager;
            }
            lastError = result;
            if (manager != IntPtr.Zero)
            {
                Marshal.Release(manager);
            }
        }

        Marshal.ThrowExceptionForHR(lastError);
        throw new InvalidOperationException("Windows virtual desktop interfaces are unavailable.");
    }

    private static Guid GetDesktopInterfaceId(int build)
    {
        if (build >= 22621) return new Guid("3F07F4BE-B107-441A-AF0F-39D82529072C");
        if (build >= 21313) return new Guid("536D3495-B208-4CC9-AE26-DE8111275BF8");
        if (build >= 20231) return new Guid("62FDF88B-11CA-4AFB-8BD8-2296DFAE49E2");
        return new Guid("FF72FFDD-BE7E-43FC-9C03-AD81681E88E4");
    }
}
'@
    Add-Type -TypeDefinition $windowProbeSource
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $window = [VisibleWindowProbe]::FindWindow(
            $ProcessName,
            $WindowTitle,
            $MoveToDesktopId -ne [Guid]::Empty,
            $RootProcessId)
        if ($window -ne [IntPtr]::Zero) {
            if ($MoveToDesktopId -ne [Guid]::Empty) {
                try {
                    Add-Type -TypeDefinition $windowDesktopOperationsSource
                    [VirtualDesktopWindowOperations]::MoveWindowToDesktop($window.ToInt64(), $MoveToDesktopId)
                } catch {
                    [Console]::Error.WriteLine("The $ProcessName window was detected but could not be moved to its assigned desktop: $_")
                    exit 3
                }
            }
            [Console]::Out.WriteLine($window.ToInt64())
            exit 0
        }
        if (-not $WaitForWindow) {
            exit 1
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    [Console]::Error.WriteLine("Timed out waiting for a window from $ProcessName.")
    exit 2
}

try {
    $desktopCount = Get-VirtualDesktopCount
    if (($DesktopIndex -lt 0 -and -not $CaptureDesktopId) -or $DesktopIndex -ge $desktopCount) {
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

$desktopIdentitySource = @'
using System;
using System.Runtime.InteropServices;

public static class CurrentVirtualDesktopIdentity
{
    private static readonly Guid ImmersiveShellClassId = new Guid("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    private static readonly Guid ServiceProviderInterfaceId = new Guid("6D5140C1-7436-11CE-8034-00AA006009FA");
    private static readonly Guid VirtualDesktopManagerServiceId = new Guid("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryServiceDelegate(IntPtr self, ref Guid serviceId, ref Guid interfaceId, out IntPtr result);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetCurrentDesktopDelegate(IntPtr self, out IntPtr desktop);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetCurrentDesktopWithWindowDelegate(IntPtr self, IntPtr window, out IntPtr desktop);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesktopIdDelegate(IntPtr self, out Guid desktopId);

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int CoCreateInstance(
        ref Guid classId,
        IntPtr outer,
        uint context,
        ref Guid interfaceId,
        out IntPtr instance);

    public static Guid Capture()
    {
        IntPtr serviceProvider = IntPtr.Zero;
        IntPtr manager = IntPtr.Zero;
        IntPtr desktop = IntPtr.Zero;
        try
        {
            Guid classId = ImmersiveShellClassId;
            Guid providerId = ServiceProviderInterfaceId;
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref classId, IntPtr.Zero, 0x17, ref providerId, out serviceProvider));

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
                int result = queryService(serviceProvider, ref serviceId, ref interfaceId, out manager);
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

                IntPtr method = Marshal.ReadIntPtr(Marshal.ReadIntPtr(manager), 6 * IntPtr.Size);
                result = build >= 22000 && build < 22621
                    ? Marshal.GetDelegateForFunctionPointer<GetCurrentDesktopWithWindowDelegate>(method)(manager, IntPtr.Zero, out desktop)
                    : Marshal.GetDelegateForFunctionPointer<GetCurrentDesktopDelegate>(method)(manager, out desktop);
                Marshal.ThrowExceptionForHR(result);
                if (desktop == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Windows returned no current virtual desktop.");
                }

                IntPtr getId = Marshal.ReadIntPtr(Marshal.ReadIntPtr(desktop), 4 * IntPtr.Size);
                Guid currentDesktopId;
                Marshal.ThrowExceptionForHR(Marshal.GetDelegateForFunctionPointer<GetDesktopIdDelegate>(getId)(desktop, out currentDesktopId));
                if (currentDesktopId == Guid.Empty)
                {
                    throw new InvalidOperationException("Windows returned an empty virtual desktop identifier.");
                }

                return currentDesktopId;
            }

            Marshal.ThrowExceptionForHR(lastError);
            throw new InvalidOperationException("Windows virtual desktop interfaces are unavailable.");
        }
        finally
        {
            if (desktop != IntPtr.Zero)
            {
                Marshal.Release(desktop);
            }
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

if ($DesktopIndex -lt 0 -and $CaptureDesktopId) {
    try {
        Add-Type -TypeDefinition $desktopIdentitySource
        [Console]::Out.WriteLine([CurrentVirtualDesktopIdentity]::Capture())
        exit 0
    } catch {
        [Console]::Error.WriteLine("Could not identify the current virtual desktop: $_")
        exit 1
    }
}

try {
    Add-Type -TypeDefinition $keyboardSource

    for ($step = 0; $step -lt ($desktopCount - 1); $step++) {
        [VirtualDesktopHotkeys]::Switch(-1)
        Start-Sleep -Milliseconds 350
    }

    for ($step = 0; $step -lt $DesktopIndex; $step++) {
        [VirtualDesktopHotkeys]::Switch(1)
        Start-Sleep -Milliseconds 350
    }

    if ($CaptureDesktopId) {
        Add-Type -TypeDefinition $desktopIdentitySource
        [Console]::Out.WriteLine([CurrentVirtualDesktopIdentity]::Capture())
    } else {
        Write-Output "Switched to desktop $($DesktopIndex + 1)."
    }
} catch {
    Write-Error "Desktop switch failed: $_"
    exit 1
}