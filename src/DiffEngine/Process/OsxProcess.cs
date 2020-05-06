using System.Collections.Generic;
using System.Linq;
using DiffEngine;

static class OsxProcess
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