static class WildcardFileFinder
{
    static char[] separators =
    [
        Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar
    ];

    static List<string> EnumerateDirectories(string directory)
    {
        // `directory` is already environment-variable-expanded by the sole caller (TryFind).
        if (!directory.Contains('*'))
        {
            if (Directory.Exists(directory))
            {
                return [directory];
            }
        }

        var segments = directory.Split(separators);
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
                    newRoots.AddRange(Children(root, segment));
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

            if (newRoots.Count == 0)
            {
                return [];
            }

            currentRoots = newRoots;
        }

        return currentRoots;
    }

    /// <summary>
    /// The directories under <paramref name="root" /> matching a wildcard segment, or none when the
    /// root cannot be listed.
    /// <para>
    /// A root that does not exist is ordinary here, not an error: a variable the machine does not
    /// define, such as <c>%ProgramW6432%</c> on 32 bit Windows, is left as written by
    /// ExpandEnvironmentVariables and becomes a relative root. Thrown, it escaped through
    /// DiffTools' static constructor, and every later use of DiffTools in the process was a
    /// TypeInitializationException.
    /// </para>
    /// </summary>
    static IEnumerable<string> Children(string root, string segment)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateDirectories(root, segment)
                .OrderByDescending(Directory.GetLastWriteTime)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
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
}