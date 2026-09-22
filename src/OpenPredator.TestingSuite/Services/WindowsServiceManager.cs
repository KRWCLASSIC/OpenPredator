using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace OpenPredator.TestingSuite.Services;

public enum ServiceState
{
    Unknown = 0,
    NotFound = 1,
    Stopped = 2,
    StartPending = 3,
    StopPending = 4,
    Running = 5,
    ContinuePending = 6,
    PausePending = 7,
    Paused = 8
}

public static class WindowsServiceManager
{
    private const uint SC_MANAGER_CONNECT = 0x0001;
    private const uint SC_MANAGER_ENUMERATE_SERVICE = 0x0004;
    private const uint SC_MANAGER_ALL_ACCESS = 0xF003F;

    private const uint SERVICE_QUERY_STATUS = 0x0004;
    private const uint SERVICE_START = 0x0010;
    private const uint SERVICE_STOP = 0x0020;
    private const uint SERVICE_ALL_ACCESS = 0xF01FF;

    private const uint SERVICE_CONTROL_STOP = 0x00000001;

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

    [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", EntryPoint = "StartServiceW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool StartService(IntPtr hService, uint dwNumServiceArgs, IntPtr lpServiceArgVectors);

    [DllImport("advapi32.dll", EntryPoint = "ControlService", SetLastError = true)]
    private static extern bool ControlService(IntPtr hService, uint dwControl, out SERVICE_STATUS lpServiceStatus);

    [DllImport("advapi32.dll", EntryPoint = "QueryServiceStatus", SetLastError = true)]
    private static extern bool QueryServiceStatus(IntPtr hService, out SERVICE_STATUS lpServiceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr hSCObject);

    public static bool IsAdministrator()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
        return Environment.UserName == "root" || Environment.GetEnvironmentVariable("USER") == "root";
    }

    public static ServiceState GetServiceState(string serviceName)
    {
        if (!OperatingSystem.IsWindows()) return ServiceState.Unknown;

        IntPtr hSc = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (hSc == IntPtr.Zero) return ServiceState.Unknown;

        try
        {
            IntPtr hSvc = OpenService(hSc, serviceName, SERVICE_QUERY_STATUS);
            if (hSvc == IntPtr.Zero) return ServiceState.NotFound;

            try
            {
                if (QueryServiceStatus(hSvc, out var status))
                {
                    return status.dwCurrentState switch
                    {
                        1 => ServiceState.Stopped,
                        2 => ServiceState.StartPending,
                        3 => ServiceState.StopPending,
                        4 => ServiceState.Running,
                        5 => ServiceState.ContinuePending,
                        6 => ServiceState.PausePending,
                        7 => ServiceState.Paused,
                        _ => ServiceState.Unknown
                    };
                }
            }
            finally
            {
                CloseServiceHandle(hSvc);
            }
        }
        finally
        {
            CloseServiceHandle(hSc);
        }

        return ServiceState.Unknown;
    }

    public static async Task<bool> StartServiceAsync(string serviceName, int timeoutMs = 8000, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows()) return false;

        return await Task.Run(async () =>
        {
            IntPtr hSc = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
            if (hSc == IntPtr.Zero)
            {
                hSc = OpenSCManager(null, null, SC_MANAGER_CONNECT);
                if (hSc == IntPtr.Zero) return false;
            }

            try
            {
                IntPtr hSvc = OpenService(hSc, serviceName, SERVICE_START | SERVICE_QUERY_STATUS);
                if (hSvc == IntPtr.Zero) return false;

                try
                {
                    if (QueryServiceStatus(hSvc, out var status))
                    {
                        if (status.dwCurrentState == 4 /* SERVICE_RUNNING */) return true;
                    }

                    StartService(hSvc, 0, IntPtr.Zero);

                    var startTime = DateTime.UtcNow;
                    while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
                    {
                        if (ct.IsCancellationRequested) return false;

                        if (QueryServiceStatus(hSvc, out status))
                        {
                            if (status.dwCurrentState == 4 /* SERVICE_RUNNING */) return true;
                        }
                        await Task.Delay(200, ct);
                    }
                }
                finally
                {
                    CloseServiceHandle(hSvc);
                }
            }
            finally
            {
                CloseServiceHandle(hSc);
            }

            return GetServiceState(serviceName) == ServiceState.Running;
        }, ct);
    }

    public static async Task<bool> StopServiceAsync(string serviceName, int timeoutMs = 8000, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows()) return false;

        return await Task.Run(async () =>
        {
            IntPtr hSc = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
            if (hSc == IntPtr.Zero)
            {
                hSc = OpenSCManager(null, null, SC_MANAGER_CONNECT);
                if (hSc == IntPtr.Zero) return false;
            }

            try
            {
                IntPtr hSvc = OpenService(hSc, serviceName, SERVICE_STOP | SERVICE_QUERY_STATUS);
                if (hSvc == IntPtr.Zero) return true; // Already not present or stopped

                try
                {
                    if (QueryServiceStatus(hSvc, out var status))
                    {
                        if (status.dwCurrentState == 1 /* SERVICE_STOPPED */) return true;
                    }

                    ControlService(hSvc, SERVICE_CONTROL_STOP, out _);

                    var startTime = DateTime.UtcNow;
                    while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
                    {
                        if (ct.IsCancellationRequested) return false;

                        if (QueryServiceStatus(hSvc, out status))
                        {
                            if (status.dwCurrentState == 1 /* SERVICE_STOPPED */) return true;
                        }
                        await Task.Delay(200, ct);
                    }
                }
                finally
                {
                    CloseServiceHandle(hSvc);
                }
            }
            finally
            {
                CloseServiceHandle(hSc);
            }

            return GetServiceState(serviceName) == ServiceState.Stopped;
        }, ct);
    }

    public static async Task<bool> SwitchServiceAsync(string targetServiceToStart, string? otherServiceToStop, int timeoutMs = 8000, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(otherServiceToStop))
        {
            var otherState = GetServiceState(otherServiceToStop);
            if (otherState == ServiceState.Running || otherState == ServiceState.StartPending)
            {
                await StopServiceAsync(otherServiceToStop, timeoutMs, ct);
                await Task.Delay(300, ct);
            }
        }

        var targetState = GetServiceState(targetServiceToStart);
        if (targetState != ServiceState.Running)
        {
            await StartServiceAsync(targetServiceToStart, timeoutMs, ct);
            await Task.Delay(400, ct);
        }

        return GetServiceState(targetServiceToStart) == ServiceState.Running;
    }
}
