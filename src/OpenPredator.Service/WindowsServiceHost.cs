using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace OpenPredator.Service;

public static unsafe class WindowsServiceHost
{
    private const uint SERVICE_WIN32_OWN_PROCESS = 0x00000010;
    private const uint SERVICE_STOPPED = 0x00000001;
    private const uint SERVICE_START_PENDING = 0x00000002;
    private const uint SERVICE_STOP_PENDING = 0x00000003;
    private const uint SERVICE_RUNNING = 0x00000004;

    private const uint SERVICE_ACCEPT_STOP = 0x00000001;
    private const uint SERVICE_ACCEPT_SHUTDOWN = 0x00000004;

    private const uint SERVICE_CONTROL_STOP = 0x00000001;
    private const uint SERVICE_CONTROL_SHUTDOWN = 0x00000004;

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_TABLE_ENTRY
    {
        public char* lpServiceName;
        public delegate* unmanaged<uint, char**, void> lpServiceProc;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    [DllImport("advapi32.dll", EntryPoint = "StartServiceCtrlDispatcherW", SetLastError = true)]
    private static extern bool StartServiceCtrlDispatcher(SERVICE_TABLE_ENTRY* lpServiceStartTable);

    [DllImport("advapi32.dll", EntryPoint = "RegisterServiceCtrlHandlerExW", SetLastError = true)]
    private static extern IntPtr RegisterServiceCtrlHandlerEx(char* lpServiceName, delegate* unmanaged<uint, uint, IntPtr, IntPtr, uint> lpHandlerProc, IntPtr lpContext);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetServiceStatus(IntPtr hServiceStatus, SERVICE_STATUS* lpServiceStatus);

    private static IntPtr _statusHandle;
    private static SERVICE_STATUS _status;
    private static CancellationTokenSource? _serviceCts;
    private static Func<CancellationToken, Task>? _serviceWorker;

    public static bool RunAsService(Func<CancellationToken, Task> worker)
    {
        if (!OperatingSystem.IsWindows()) return false;

        _serviceWorker = worker;

        fixed (char* pName = "OpenPredator")
        {
            SERVICE_TABLE_ENTRY* table = stackalloc SERVICE_TABLE_ENTRY[2];
            table[0].lpServiceName = pName;
            table[0].lpServiceProc = &ServiceMain;
            table[1].lpServiceName = null;
            table[1].lpServiceProc = null;

            return StartServiceCtrlDispatcher(table);
        }
    }

    [UnmanagedCallersOnly]
    private static void ServiceMain(uint argc, char** argv)
    {
        fixed (char* pName = "OpenPredator")
        {
            _statusHandle = RegisterServiceCtrlHandlerEx(pName, &ServiceCtrlHandler, IntPtr.Zero);
        }

        if (_statusHandle == IntPtr.Zero) return;

        _status = new SERVICE_STATUS
        {
            dwServiceType = SERVICE_WIN32_OWN_PROCESS,
            dwCurrentState = SERVICE_START_PENDING,
            dwControlsAccepted = SERVICE_ACCEPT_STOP | SERVICE_ACCEPT_SHUTDOWN,
            dwWin32ExitCode = 0,
            dwCheckPoint = 0,
            dwWaitHint = 3000
        };
        fixed (SERVICE_STATUS* pStatus = &_status)
        {
            SetServiceStatus(_statusHandle, pStatus);
        }

        _serviceCts = new CancellationTokenSource();

        _status.dwCurrentState = SERVICE_RUNNING;
        fixed (SERVICE_STATUS* pStatus = &_status)
        {
            SetServiceStatus(_statusHandle, pStatus);
        }

        try
        {
            _serviceWorker?.Invoke(_serviceCts.Token).GetAwaiter().GetResult();
        }
        catch { }

        _status.dwCurrentState = SERVICE_STOPPED;
        fixed (SERVICE_STATUS* pStatus = &_status)
        {
            SetServiceStatus(_statusHandle, pStatus);
        }
    }

    [UnmanagedCallersOnly]
    private static uint ServiceCtrlHandler(uint control, uint eventType, IntPtr eventData, IntPtr context)
    {
        if (control == SERVICE_CONTROL_STOP || control == SERVICE_CONTROL_SHUTDOWN)
        {
            _status.dwCurrentState = SERVICE_STOP_PENDING;
            fixed (SERVICE_STATUS* pStatus = &_status)
            {
                SetServiceStatus(_statusHandle, pStatus);
            }
            _serviceCts?.Cancel();
            return 0;
        }
        return 0;
    }
}
