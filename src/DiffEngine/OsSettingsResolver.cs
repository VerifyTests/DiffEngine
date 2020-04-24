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
        [NotNullWhen(true)] out BuildArguments? arguments)
    {
        if (windows != null &&
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (WildcardFileFinder.TryFindExe(windows.ExePaths, out path))
            {
                arguments = windows.Arguments;
                return true;
            }
        }

        if (linux != null &&
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (WildcardFileFinder.TryFindExe(linux.ExePaths, out path))
            {
                arguments = linux.Arguments;
                return true;
            }
        }

        if (osx != null &&
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (WildcardFileFinder.TryFindExe(osx.ExePaths, out path))
            {
                arguments = osx.Arguments;
                return true;
            }
        }

        path = null;
        arguments = null;
        return false;
    }
}