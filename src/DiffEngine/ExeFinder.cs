static class ExeFinder
{
    static string[] envPaths;

    static ExeFinder()
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

    static char[] separators =
    {
        Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar
    };

    static IEnumerable<string> EnumerateDirectories(string directory)
    {
        var expanded = Environment.ExpandEnvironmentVariables(directory);
        if (!directory.Contains('*'))
        {
            if (Directory.Exists(directory))
            {
                return new List<string>
                {
                    directory
                };
            }
        }

        var segments = expanded.Split(separators);
        var currentRoots = new List<string>
        {
            segments[0] + Path.DirectorySeparatorChar
        };
        foreach (var segment in segments.Skip(1))
        {
            var newRoots = new List<string>();
            foreach (var root in currentRoots)
            {
                if (segment.Contains('*'))
                {
                    newRoots.AddRange(Directory.EnumerateDirectories(root, segment)
                        .OrderByDescending(Directory.GetLastWriteTime));
                }
                else
                {
                    var newRoot = Path.Combine(root, segment);
                    if (Directory.Exists(newRoot))
                    {
                        newRoots.Add(newRoot);
                    }
                }
            }

            if (!newRoots.Any())
            {
                return Enumerable.Empty<string>();
            }

            currentRoots = newRoots;
        }

        return currentRoots;
    }

    public static bool TryFind(
        string path,
        [NotNullWhen(true)] out string? result)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (!path.Contains('*'))
        {
            if (File.Exists(expanded))
            {
                result = expanded;
                return true;
            }

            Logging.Write($"Could not find file: {path}");
            result = null;
            return false;
        }

        var filePart = Path.GetFileName(expanded);
        var directoryPart = Path.GetDirectoryName(expanded)!;
        foreach (var directory in EnumerateDirectories(directoryPart))
        {
            if (filePart.Contains('*'))
            {
                throw new("Wildcard in file part currently not supported.");
            }

            var filePath = Path.Combine(directory, filePart);
            if (File.Exists(filePath))
            {
                result = filePath;
                return true;
            }
        }

        Logging.Write($"Could not find file: {path}");
        result = null;
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

    public static bool TryFindExe(string exeName, IEnumerable<string> searchDirectories, [NotNullWhen(true)] out string? exePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            searchDirectories = ExpandProgramFiles(searchDirectories);
        }

        foreach (var directory in searchDirectories.Distinct())
        {
            var exeSearchPath = Path.Combine(directory, exeName);
            if (TryFind(exeSearchPath, out exePath))
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
        exePath = envPaths
            .Select(_ => Path.Combine(_, exeName))
            .FirstOrDefault(File.Exists);

        return exePath != null;
    }
}