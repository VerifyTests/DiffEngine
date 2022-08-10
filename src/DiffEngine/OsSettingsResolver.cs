using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

static class OsSettingsResolver
{
    public static bool Resolve(
        OsSettings? windows,
        OsSettings? linux,
        OsSettings? osx,
        [NotNullWhen(true)] out string? path,
        [NotNullWhen(true)] out BuildArguments? targetLeftArguments,
        [NotNullWhen(true)] out BuildArguments? targetRightArguments)
    {
        if (windows != null &&
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var paths = ExpandProgramFiles(windows.ExePaths);

            if (TryFindExe(paths, out path))
            {
                targetLeftArguments = windows.TargetLeftArguments;
                targetRightArguments = windows.TargetRightArguments;
                return true;
            }
        }

        if (linux != null &&
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (TryFindExe(linux.ExePaths, out path))
            {
                targetLeftArguments = linux.TargetLeftArguments;
                targetRightArguments = linux.TargetRightArguments;
                return true;
            }
        }

        if (osx != null &&
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (TryFindExe(osx.ExePaths, out path))
            {
                targetLeftArguments = osx.TargetLeftArguments;
                targetRightArguments = osx.TargetRightArguments;
                return true;
            }
        }

        path = null;
        targetLeftArguments = null;
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

    public static bool TryFindExe(IEnumerable<string> paths, [NotNullWhen(true)] out string? exePath)
    {
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (IsCliDefinition(path) && TryFindInEnvPath(path, out exePath))
            {
                return true;
            }

            if (WildcardFileFinder.TryFind(path, out exePath))
            {
                return true;
            }
        }

        exePath = null;
        return false;
    }

    public static bool IsCliDefinition(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return path.Count(c => c == '\\') == 0;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return path.Count(c => c == '/') == 0;
        }

        return false;
    }

    public static bool TryFindInEnvPath(string cliApp, [NotNullWhen(true)] out string? filePath)
    {
        // For each path in PATH, append cliApp and check if it exists.
        // Return the first one that exists.

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            filePath = Environment.GetEnvironmentVariable("PATH")!
                .Split(';')
                .Select(s => Path.Combine(s, cliApp))
                .FirstOrDefault(x => File.Exists(x));

            return filePath != null;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            filePath = Environment.GetEnvironmentVariable("PATH")!
                .Split(':')
                .Select(s => Path.Combine(s, cliApp))
                .FirstOrDefault(x => File.Exists(x));

            return filePath != null;
        }

        filePath = null;
        return false;
    }
}