using System.Collections.Generic;
using System.Linq;
using DiffEngine;

static class LinuxProcess
{
    public static bool TryTerminateProcess(ProcessCommand processCommand)
    {
        return false;
    }

    public static IEnumerable<ProcessCommand> FindAll()
    {
        return Enumerable.Empty<ProcessCommand>();
    }
}