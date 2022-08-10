using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

static class OsSettingsResolver
{
    public static bool Resolve(
        OsSettings? windows,
        OsSettings? linux,
        OsSettings? osx,
        [NotNullWhen(true)] out string? path,
        [NotNullWhen(true)] out BuildArguments? leftArguments,
        [NotNullWhen(true)] out BuildArguments? rightArguments)
    {
        if (TryResolveForOs(windows, out path, out leftArguments, out rightArguments, OSPlatform.Windows))
        {
            return true;
        }

        if (TryResolveForOs(linux, out path, out leftArguments, out rightArguments, OSPlatform.Linux))
        {
            return true;
        }

        if (TryResolveForOs(osx, out path, out leftArguments, out rightArguments, OSPlatform.OSX))
        {
            return true;
        }

        path = null;
        leftArguments = null;
        rightArguments = null;
        return false;
    }

    static bool TryResolveForOs(
        OsSettings? os,
        [NotNullWhen(true)] out string? path,
        [NotNullWhen(true)] out BuildArguments? leftArguments,
        [NotNullWhen(true)] out BuildArguments? rightArguments,
        OSPlatform platform)
    {
        if (os != null &&
            RuntimeInformation.IsOSPlatform(platform))
        {
            if (TryFindExe(os.ExeName, os.SearchDirectories, out path))
            {
                leftArguments = os.LeftArguments;
                rightArguments = os.RightArguments;
                return true;
            }
        }

        leftArguments = null;
        rightArguments = null;
        path = null;
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

    public static bool TryFindExe(string exeName, IEnumerable<string> searchDirectories, [NotNullWhen(true)] out string? exePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            searchDirectories = ExpandProgramFiles(searchDirectories);
        }

        foreach (var path in searchDirectories.Distinct())
        {
            if (WildcardFileFinder.TryFind(path, out exePath))
            {
                return true;
            }
        }

        return TryFindInEnvPath(exeName, out exePath);
    }

    public static bool TryFindInEnvPath(string exeName, [NotNullWhen(true)] out string? exePath)
    {
        // For each path in PATH, append cliApp and check if it exists.
        // Return the first one that exists.

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            exePath = Environment.GetEnvironmentVariable("PATH")!
                .Split(';')
                .Select(s => Path.Combine(s, exeName))
                .FirstOrDefault(x => File.Exists(x));

            return exePath != null;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            exePath = Environment.GetEnvironmentVariable("PATH")!
                .Split(':')
                .Select(s => Path.Combine(s, exeName))
                .FirstOrDefault(x => File.Exists(x));

            return exePath != null;
        }

        exePath = null;
        return false;
    }
}