using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

static class ExeFinder
{
    public static bool TryFindExe(
        string[] windowsExePaths,
        string[] linuxExePaths,
        string[] osxExePaths,
        [NotNullWhen(true)] out string? exePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return TryFindExe(windowsExePaths, out exePath);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return TryFindExe(linuxExePaths, out exePath);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return TryFindExe(osxExePaths, out exePath);
        }

        throw new Exception($"OS not supported: {RuntimeInformation.OSDescription}");
    }

    static bool TryFindExe(string[] exePaths, [NotNullWhen(true)] out string? exePath)
    {
        foreach (var path in exePaths)
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