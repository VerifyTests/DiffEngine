using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using DiffEngine;

static class OsSettingsResolver
{
    public static bool Resolve(
        OsSettings? windows,
        OsSettings? linux,
        OsSettings? osx,
        [NotNullWhen(true)] out string? path,
        [NotNullWhen(true)] out BuildArguments? targetRightArguments)
    {
        if (windows != null &&
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var paths = ExpandProgramFiles(windows.ExePaths);

            if (WildcardFileFinder.TryFindExe(paths, out path))
            {
                targetRightArguments = windows.TargetRightArguments;
                return true;
            }
        }

        if (linux != null &&
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (WildcardFileFinder.TryFindExe(linux.ExePaths, out path))
            {
                targetRightArguments = linux.TargetRightArguments;
                return true;
            }
        }

        if (osx != null &&
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (WildcardFileFinder.TryFindExe(osx.ExePaths, out path))
            {
                targetRightArguments = osx.TargetRightArguments;
                return true;
            }
        }

        path = null;
        targetRightArguments = null;
        return false;
    }

    public static IEnumerable<string> ExpandProgramFiles(IEnumerable<string> paths)
    {
        // Note: Windows can have multiple paths, and will resolve %ProgramFiles% as 'C:\Program Files (x86)'
        // when running inside a 32-bit process. To
        // overcome this issue, we need to manually add any option so the correct paths will be resolved

        foreach (var windowsPath in paths)
        {
            yield return windowsPath;

            if (windowsPath.Contains("%ProgramFiles%"))
            {
                yield return windowsPath.Replace("%ProgramFiles%", "%ProgramW6432%");
                yield return windowsPath.Replace("%ProgramFiles%", "%ProgramFiles(x86)%");
            }
        }
    }
}