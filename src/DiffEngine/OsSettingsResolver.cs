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
            envPaths = Array.Empty<string>();
        }
    }

    public static bool Resolve(
        string tool,
        OsSupport osSupport,
        [NotNullWhen(true)] out string? path,
        [NotNullWhen(true)] out LaunchArguments? launchArguments)
    {
        if (TryResolveForOs(tool, osSupport.Windows, out path, OSPlatform.Windows))
        {
            launchArguments = osSupport.Windows.LaunchArguments;
            return true;
        }

        if (TryResolveForOs(tool, osSupport.Linux, out path, OSPlatform.Linux))
        {
            launchArguments = osSupport.Linux.LaunchArguments;
            return true;
        }

        if (TryResolveForOs(tool, osSupport.Osx, out path, OSPlatform.OSX))
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
        OSPlatform platform)
    {
        path = null;

        if (os == null || !RuntimeInformation.IsOSPlatform(platform))
        {
            return false;
        }

        var environmentVariable = $"DiffEngine_{tool}";
        var basePath = Environment.GetEnvironmentVariable(environmentVariable);
        if (basePath is not null)
        {
            if (basePath.EndsWith(os.ExeName) &&
                File.Exists(basePath))
            {
                path = basePath;
                return true;
            }

            if (Directory.Exists(basePath))
            {
                path = Path.Combine(basePath, os.ExeName);
                if (File.Exists(path))
                {
                    return true;
                }
            }

            throw new($"Could not find exe defined by {environmentVariable}. Path: {basePath}");
        }

        return TryFindExe(os.ExeName, os.PathCommandName, os.SearchDirectories, out path);
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
        commandPath = envPaths
            .Select(_ => Path.Combine(_, pathCommandName))
            .FirstOrDefault(File.Exists);

        return commandPath != null;
    }
}