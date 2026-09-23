static class FileLockKiller
{
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll")]
    static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    static extern int RmRegisterResources(
        uint pSessionHandle,
        uint nFiles,
        string[] rgsFileNames,
        uint nApplications,
        [In] RM_UNIQUE_PROCESS[] rgApplications,
        uint nServices,
        string[] rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
        ref uint lpdwRebootReasons);

    const int errorMoreData = 234;

    [StructLayout(LayoutKind.Sequential)]
    struct RM_UNIQUE_PROCESS
    {
        public uint dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strAppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string strServiceShortName;

        public uint ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    public static List<LockingProcess> GetLockingProcesses(string filePath)
    {
        var processes = new List<LockingProcess>();

        if (RmStartSession(out var sessionHandle, 0, Guid.NewGuid().ToString()) != 0)
        {
            return processes;
        }

        try
        {
            var resources = new[] { filePath };
            if (RmRegisterResources(sessionHandle, (uint)resources.Length, resources, 0, [], 0, []) != 0)
            {
                return processes;
            }

            var procInfo = 0u;
            var rebootReasons = 0u;
            var result = RmGetList(sessionHandle, out var procInfoNeeded, ref procInfo, null, ref rebootReasons);

            if (result != errorMoreData || procInfoNeeded == 0)
            {
                return processes;
            }

            var processInfo = new RM_PROCESS_INFO[procInfoNeeded];
            procInfo = procInfoNeeded;
            result = RmGetList(sessionHandle, out procInfoNeeded, ref procInfo, processInfo, ref rebootReasons);

            if (result != 0)
            {
                return processes;
            }

            var currentProcessId = Environment.ProcessId;
            for (var i = 0; i < procInfo; i++)
            {
                var info = processInfo[i];
                var processId = (int)info.Process.dwProcessId;
                if (processId == currentProcessId)
                {
                    continue;
                }

                processes.Add(new(processId, info.strAppName, StartTime(info.Process.ProcessStartTime)));
            }
        }
        catch (Exception exception)
        {
            ExceptionHandler.Handle($"Failed to get locking processes for '{filePath}'.", exception);
        }
        finally
        {
            // Nothing useful to do if ending the session fails
            _ = RmEndSession(sessionHandle);
        }

        return processes;
    }

    static DateTime? StartTime(System.Runtime.InteropServices.ComTypes.FILETIME time)
    {
        var ticks = ((long) (uint) time.dwHighDateTime << 32) | (uint) time.dwLowDateTime;
        if (ticks == 0)
        {
            return null;
        }

        return DateTime.FromFileTimeUtc(ticks);
    }

    /// <summary>
    /// Whether the process that holds the id now is the one Restart Manager reported, by its start
    /// time, read through the handle the kill then uses.
    /// </summary>
    static bool IsSame(Process process, LockingProcess locking)
    {
        if (locking.StartTime is not { } reported)
        {
            return false;
        }

        try
        {
            // Restart Manager and the process's own creation time are the same FILETIME
            return Math.Abs((process.StartTime.ToUniversalTime() - reported).TotalMilliseconds) < 1;
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    public static bool Kill(IEnumerable<LockingProcess> processes)
    {
        var killed = false;

        foreach (var locking in processes)
        {
            if (!ProcessEx.TryGet(locking.ProcessId, out var process))
            {
                continue;
            }

            if (!IsSame(process, locking))
            {
                Log.Information(
                    "Not killing PID {ProcessId}: it is no longer the '{ProcessName}' that held the file",
                    locking.ProcessId,
                    locking.Name);
                process.Dispose();
                continue;
            }

            Log.Information(
                "Killing locking process '{ProcessName}' (PID: {ProcessId})",
                locking.Name,
                locking.ProcessId);
            process.KillAndDispose();
            killed = true;
        }

        return killed;
    }

    public static bool KillLockingProcesses(string filePath) =>
        Kill(GetLockingProcesses(filePath));
}