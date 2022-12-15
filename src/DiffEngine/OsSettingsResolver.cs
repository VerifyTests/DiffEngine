﻿static class OsSettingsResolver
{
    static string[] envPaths;

    static OsSettingsResolver()
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH")!;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            envPaths = pathVariable.Split(';');
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                 RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            envPaths = pathVariable.Split(':');
        }
        else
        {
            envPaths = Array.Empty<string>();
        }
    }

    public static bool Resolve(
        OsSupport osSupport,
        [NotNullWhen(true)] out string? path,
        [NotNullWhen(true)] out LaunchArguments? launchArguments)
    {
        if (TryResolveForOs(osSupport.Windows, out path, out launchArguments, OSPlatform.Windows))
        {
            return true;
        }

        if (TryResolveForOs(osSupport.Linux, out path, out launchArguments, OSPlatform.Linux))
        {
            return true;
        }

        if (TryResolveForOs(osSupport.Osx, out path, out launchArguments, OSPlatform.OSX))
        {
            return true;
        }

        path = null;
        launchArguments = null;
        return false;
    }

    static bool TryResolveForOs(
        OsSettings? os,
        [NotNullWhen(true)] out string? path,
        [NotNullWhen(true)] out LaunchArguments? launchArguments,
        OSPlatform platform)
    {
        if (os != null &&
            RuntimeInformation.IsOSPlatform(platform))
        {
            if (TryFindExe(os.ExeName, os.SearchDirectories, out path))
            {
                launchArguments = os.LaunchArguments;
                return true;
            }
        }

        launchArguments = null;
        path = null;
        return false;
    }

    public static IEnumerable<string> ExpandProgramFiles(IEnumerable<string> paths)
    {
        // Note: Windows can have multiple paths, and will resolve %ProgramFiles% as 'C:\Program Files (x86)'
        // when running inside a 32-bit process. To
        // overcome this issue, we need to manually add any option so the correct paths will be resolved

        foreach (var path in paths)
        {
            yield return path;

            if (!path.Contains("%ProgramFiles%"))
            {
                continue;
            }

            yield return path.Replace("%ProgramFiles%", "%ProgramW6432%");
            yield return path.Replace("%ProgramFiles%", "%ProgramFiles(x86)%");
        }
    }

    static bool TryFindExe(string exeName, IEnumerable<string> searchDirectories, [NotNullWhen(true)] out string? exePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            searchDirectories = ExpandProgramFiles(searchDirectories);
        }

        foreach (var directory in searchDirectories.Distinct())
        {
            var exeSearchPath = Path.Combine(directory, exeName);
            if (ExeFinder.TryFind(exeSearchPath, out exePath))
            {
                return true;
            }
        }

        return ExeFinder.TryFindInEnvPath(exeName, out exePath);
    }
}