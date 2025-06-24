static class OsSettingsResolver
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
            envPaths = [];
        }
    }

    public static bool Resolve(
        string tool,
        OsSupport osSupport,
        [NotNullWhen(true)] out string? path,
        [NotNullWhen(true)] out LaunchArguments? launchArguments)
    {
        if (TryResolveForOs(tool, osSupport.Windows, out path, "WINDOWS"))
        {
            launchArguments = osSupport.Windows.LaunchArguments;
            return true;
        }

        if (TryResolveForOs(tool, osSupport.Linux, out path, "LINUX"))
        {
            launchArguments = osSupport.Linux.LaunchArguments;
            return true;
        }

        if (TryResolveForOs(tool, osSupport.Osx, out path, "OSX"))
        {
            launchArguments = osSupport.Osx.LaunchArguments;
            return true;
        }

        path = null;
        launchArguments = null;
        return false;
    }

    static bool TryResolveForOs(
        string tool,
        [NotNullWhen(true)] OsSettings? os,
        [NotNullWhen(true)] out string? path,
        string platform)
    {
        path = null;

        if (os == null || !OperatingSystem.IsOSPlatform(platform))
        {
            return false;
        }

        var exeName = os.ExeName;
        if (TryFindForEnvironmentVariable(tool, exeName, out var envPath))
        {
            path = envPath;
            return true;
        }

        return TryFindExe(exeName, os.PathCommandName, os.SearchDirectories, out path);
    }

    public static bool TryFindForEnvironmentVariable(string tool, string exeName, [NotNullWhen(true)] out string? envPath)
    {
        var environmentVariable = $"DiffEngine_{tool}";
        var basePath = Environment.GetEnvironmentVariable(environmentVariable);
        if (basePath is null)
        {
            envPath = null;
            return false;
        }

        if (basePath.EndsWith(exeName) &&
            File.Exists(basePath))
        {
            envPath = basePath;
            return true;
        }

        if (Directory.Exists(basePath))
        {
            envPath = Path.Combine(basePath, exeName);
            if (File.Exists(envPath))
            {
                return true;
            }
        }

        throw new($"Could not find exe defined by {environmentVariable}. Path: {basePath}");
    }

    // Note: Windows can have multiple paths, and will resolve %ProgramFiles% as 'C:\Program Files (x86)'
    // when running inside a 32-bit process. To
    // overcome this issue, we need to manually add any option so the correct paths will be resolved
    public static IEnumerable<string> ExpandProgramFiles(IEnumerable<string> paths)
    {
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

    static bool TryFindExe(string exeName, string pathCommandName, IEnumerable<string> searchDirectories, [NotNullWhen(true)] out string? exePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            searchDirectories = ExpandProgramFiles(searchDirectories);
        }

        foreach (var directory in searchDirectories.Distinct())
        {
            var exeSearchPath = Path.Combine(directory, exeName);
            if (WildcardFileFinder.TryFind(exeSearchPath, out exePath))
            {
                return true;
            }
        }

        return TryFindInEnvPath(pathCommandName, out exePath);
    }

    // For each path in PATH, append cliApp and check if it exists.
    // Return the first one that exists.
    public static bool TryFindInEnvPath(string pathCommandName, [NotNullWhen(true)] out string? commandPath)
    {
        foreach (var path in envPaths)
        {
            var combine = Path.Combine(path, pathCommandName);
            if (File.Exists(combine))
            {
                commandPath = combine;
                return true;
            }
        }

        commandPath = null;
        return false;
    }
}