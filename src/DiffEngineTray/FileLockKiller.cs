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

    public static bool KillLockingProcesses(string filePath)
    {
        var killed = false;

        if (RmStartSession(out var sessionHandle, 0, Guid.NewGuid().ToString()) != 0)
        {
            return false;
        }

        try
        {
            var resources = new[] { filePath };
            if (RmRegisterResources(sessionHandle, (uint)resources.Length, resources, 0, [], 0, []) != 0)
            {
                return false;
            }

            var procInfo = 0u;
            var rebootReasons = 0u;
            var result = RmGetList(sessionHandle, out var procInfoNeeded, ref procInfo, null, ref rebootReasons);

            if (result != errorMoreData || procInfoNeeded == 0)
            {
                return false;
            }

            var processInfo = new RM_PROCESS_INFO[procInfoNeeded];
            procInfo = procInfoNeeded;
            result = RmGetList(sessionHandle, out procInfoNeeded, ref procInfo, processInfo, ref rebootReasons);

            if (result != 0)
            {
                return false;
            }

            for (var i = 0; i < procInfo; i++)
            {
                var processId = (int)processInfo[i].Process.dwProcessId;
                if (!ProcessEx.TryGet(processId, out var process))
                {
                    continue;
                }

                Log.Information(
                    "Killing locking process '{ProcessName}' (PID: {ProcessId}) for file '{FilePath}'",
                    processInfo[i].strAppName,
                    processId,
                    filePath);
                process.KillAndDispose();
                killed = true;
            }
        }
        catch (Exception exception)
        {
            ExceptionHandler.Handle($"Failed to kill locking processes for '{filePath}'.", exception);
        }
        finally
        {
            RmEndSession(sessionHandle);
        }

        return killed;
    }
}
