static class OsSettingsResolver
{
    static string[] envPaths;

    static OsSettingsResolver()
    {
        // An unset PATH is a NullReferenceException in a static constructor, and so permanent
        // for the process. `env -i` and some service launchers really do start a process without
        // one; nothing on PATH simply means no tool is found that way
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? "";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            envPaths = ParsePath(pathVariable, ';');
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                 RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            envPaths = ParsePath(pathVariable, ':');
        }
        else
        {
            envPaths = [];
        }
    }

    /// <summary>
    /// PATH as directories that can be combined with a file name. Windows allows an entry in
    /// quotes, and some installers write them that way, and .NET Framework's Path.Combine throws
    /// on the quote - out of this type's static constructor, so every tool lookup in the process
    /// failed for good. Quotes and surrounding space are taken off, and whatever still holds a
    /// character no path can is dropped, along with empty entries.
    /// </summary>
    internal static string[] ParsePath(string value, char separator)
    {
        var invalid = Path.GetInvalidPathChars();
        var paths = new List<string>();
        foreach (var entry in value.Split(separator))
        {
            var path = entry.Trim().Trim('"').Trim();
            if (path.Length == 0 ||
                path.IndexOfAny(invalid) >= 0)
            {
                continue;
            }

            paths.Add(path);
        }

        return paths.ToArray();
    }

    /// <summary>
    /// The tool's executable and how it is launched, on whichever operating system this is.
    /// <para>
    /// <c>preferred</c> says which of several installed copies the caller would rather have. The
    /// first copy it accepts is the one resolved, wherever that comes in the search order, and the
    /// first copy of all when it accepts none - so it reorders what is found and never empties it.
    /// Not asked about a copy an environment variable names, which is somebody's explicit choice.
    /// </para>
    /// </summary>
    public static bool Resolve(
        string tool,
        OsSupport osSupport,
        [NotNullWhen(true)] out string? path,
        [NotNullWhen(true)] out LaunchArguments? launchArguments,
        Func<string, bool>? preferred = null)
    {
        if (TryResolveForOs(tool, osSupport.Windows, out path, "WINDOWS", preferred))
        {
            launchArguments = osSupport.Windows.LaunchArguments;
            return true;
        }

        if (TryResolveForOs(tool, osSupport.Linux, out path, "LINUX", preferred))
        {
            launchArguments = osSupport.Linux.LaunchArguments;
            return true;
        }

        if (TryResolveForOs(tool, osSupport.Osx, out path, "OSX", preferred))
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
        string platform,
        Func<string, bool>? preferred)
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

        return TryFindExe(exeName, os.PathCommandName, os.SearchDirectories, preferred, out path);
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

        if (basePath.EndsWith(exeName, StringComparison.OrdinalIgnoreCase) &&
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

    static bool TryFindExe(string exeName, string pathCommandName, IEnumerable<string> searchDirectories, Func<string, bool>? preferred, [NotNullWhen(true)] out string? exePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            searchDirectories = ExpandProgramFiles(searchDirectories);
        }

        // With nothing preferred this stops at the first copy, having looked no further than it
        // ever did. With a preference it goes on past the copies that are turned down, and comes
        // back to the first of them only if nothing better turns up
        exePath = null;
        foreach (var candidate in Installed(exeName, pathCommandName, searchDirectories))
        {
            if (preferred == null ||
                preferred(candidate))
            {
                exePath = candidate;
                return true;
            }

            exePath ??= candidate;
        }

        return exePath != null;
    }

    /// <summary>
    /// Every installed copy, in the order they are looked for: the search directories as written,
    /// then PATH.
    /// </summary>
    static IEnumerable<string> Installed(string exeName, string pathCommandName, IEnumerable<string> searchDirectories)
    {
        foreach (var directory in searchDirectories.Distinct())
        {
            var exeSearchPath = Path.Combine(directory, exeName);
            var found = false;
            foreach (var exePath in WildcardFileFinder.FindAll(exeSearchPath))
            {
                found = true;
                yield return exePath;
            }

            if (!found)
            {
                Logging.Write($"Could not find file: {exeSearchPath}");
            }
        }

        foreach (var commandPath in InEnvPath(pathCommandName))
        {
            yield return commandPath;
        }
    }

    // For each path in PATH, append cliApp and check if it exists.
    // Return the first one that exists.
    public static bool TryFindInEnvPath(string pathCommandName, [NotNullWhen(true)] out string? commandPath)
    {
        commandPath = InEnvPath(pathCommandName).FirstOrDefault();
        return commandPath != null;
    }

    static IEnumerable<string> InEnvPath(string pathCommandName)
    {
        foreach (var path in envPaths)
        {
            var combine = Path.Combine(path, pathCommandName);
            if (File.Exists(combine))
            {
                yield return combine;
            }
        }
    }
}