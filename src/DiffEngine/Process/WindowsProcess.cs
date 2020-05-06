using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
using DiffEngine;
using Microsoft.Win32.SafeHandles;

static class WindowsProcess
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeProcessHandle OpenProcess(
        int access,
        bool inherit,
        int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool TerminateProcess(
        SafeProcessHandle processHandle,
        int exitCode);

    public static bool TryTerminateProcess(ProcessCommand processCommand)
    {
        var processId = processCommand.Process;
        using var processHandle = OpenProcess(4097, false, processId);
        if (processHandle.IsInvalid)
        {
            return false;
        }

        TerminateProcess(processHandle, -1);
        return true;
    }

    public static IEnumerable<ProcessCommand> FindAll()
    {
        var wmiQuery = @"
select CommandLine, ProcessId
from Win32_Process
where CommandLine like '% %.%.%'";
        using var searcher = new ManagementObjectSearcher(wmiQuery);
        using var collection = searcher.Get();
        foreach (var process in collection)
        {
            var command = (string) process["CommandLine"];
            var id = (int) Convert.ChangeType(process["ProcessId"], typeof(int));
            process.Dispose();
            yield return new ProcessCommand(command, id);
        }
    }
}