using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using DiffEngine;

static class ExeFinder
{
    public static bool TryFindExe(
        OsSettings? windows,
        OsSettings? linux,
        OsSettings? osx,
        [NotNullWhen(true)] out string? path,
        [NotNullWhen(true)] out BuildArguments? arguments)
    {
        if (windows != null &&
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (TryFindExe(windows.ExePaths, out path))
            {
                arguments = windows.Arguments;
                return true;
            }
        }

        if (linux != null && 
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (TryFindExe(linux.ExePaths, out path))
            {
                arguments = linux.Arguments;
                return true;
            }
        }

        if (osx != null &&
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (TryFindExe(osx.ExePaths, out path))
            {
                arguments = osx.Arguments;
                return true;
            }
        }

        path = null;
        arguments = null;
        return false;
    }

    static bool TryFindExe(IEnumerable<string> paths, [NotNullWhen(true)] out string? exePath)
    {
        foreach (var path in paths)
        {
            if (WildcardFileFinder.TryFind(path, out exePath))
            {
                return true;
            }
        }

        exePath = null;
        return false;
    }
}