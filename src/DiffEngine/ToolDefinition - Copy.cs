using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

static class ExeFinder
{
    public static bool TryFindExe(string[] windowsExePaths, string[] linuxExePaths, string[] osxExePaths, [NotNullWhen(true)] out string? result)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return  TryFindExe(windowsExePaths, out result);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return TryFindExe(linuxExePaths, out result);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return TryFindExe(osxExePaths, out result);
        }

        throw new Exception($"OS not supported: {RuntimeInformation.OSDescription}");
    }

    static bool TryFindExe(string[] exePaths, [NotNullWhen(true)] out string? result)
    {
        foreach (var exePath in exePaths)
        {
            if (WildcardFileFinder.TryFind(exePath, out result))
            {
                return true;
            }
        }

        result = null;
        return false;
    }
}