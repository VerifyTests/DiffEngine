static class SolutionDirectoryFinder
{
    class Result(string directory, string name)
    {
        public string Directory { get; } = directory;
        public string Name { get; } = name;
    }

    static ConcurrentDictionary<string, Result?> cache = new(StringComparer.OrdinalIgnoreCase);

    public static string? Find(string file) =>
        cache.GetOrAdd(file, Inner)?.Name;

    static Result? Inner(string file)
    {
        // Reuse an already resolved solution when the file sits inside its directory.
        // Prefer the nearest (longest) enclosing directory so nested solutions resolve correctly.
        Result? nearest = null;
        foreach (var result in cache.Values)
        {
            if (result == null ||
                !IsInDirectory(file, result.Directory))
            {
                continue;
            }

            if (nearest == null ||
                result.Directory.Length > nearest.Directory.Length)
            {
                nearest = result;
            }
        }

        if (nearest != null)
        {
            return nearest;
        }

        var currentDirectory = Path.GetDirectoryName(file);
        if (string.IsNullOrEmpty(currentDirectory))
        {
            return null;
        }

        do
        {
            if (TryFind(currentDirectory, "*.slnx", out var result))
            {
                return result;
            }

            if (TryFind(currentDirectory, "*.sln", out result))
            {
                return result;
            }

            var parent = Directory.GetParent(currentDirectory);
            if (parent == null)
            {
                return null;
            }

            currentDirectory = parent.FullName;
        } while (true);
    }

    // True when file is directory itself or sits below it, requiring a directory-separator
    // boundary so that a sibling like "AppTests" is not treated as being inside "App".
    static bool IsInDirectory(string file, string directory)
    {
        if (!file.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (file.Length == directory.Length)
        {
            return true;
        }

        var boundary = file[directory.Length];
        return boundary == Path.DirectorySeparatorChar ||
               boundary == Path.AltDirectorySeparatorChar;
    }

    static bool TryFind(string directory, string searchPattern, [NotNullWhen(true)] out Result? result)
    {
        var solutions = Directory.GetFiles(directory, searchPattern);
        if (solutions.Length != 0)
        {
            result = new(directory, Path.GetFileNameWithoutExtension(solutions.First()));
            return true;
        }

        result = null;
        return false;
    }
}