static class SolutionDirectoryFinder
{
    class Result(string directory, string name)
    {
        public string Directory { get; } = directory;
        public string Name { get; } = name;
    }

    static readonly ConcurrentDictionary<string, Result?> cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Directories already known to hold a solution, which is what lets the walk stop early
    /// without asking the disk again.
    /// </summary>
    static readonly ConcurrentDictionary<string, Result> directories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The solution a file belongs to, or null when it has none.
    /// <para>
    /// Never throws, because the paths reaching here arrive from another process — over the piper
    /// socket, or as an inline snapshot source path — and are not guaranteed to exist on this
    /// machine, or even to be a usable path. Every caller treats null as ungrouped, while a throw
    /// used to escape the tray's piper server, drop the pending item, and open an issue page.
    /// </para>
    /// </summary>
    public static string? Find(string file) =>
        cache.GetOrAdd(file, Inner)?.Name;

    static Result? Inner(string file)
    {
        try
        {
            return Walk(file);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Nothing usable in the path itself, which no amount of walking up improves on. A
            // directory that merely cannot be listed is handled in TryFind, and keeps walking.
            return null;
        }
    }

    static Result? Walk(string file)
    {
        var currentDirectory = Path.GetDirectoryName(file);
        if (string.IsNullOrEmpty(currentDirectory))
        {
            return null;
        }

        do
        {
            // Asked level by level on the way up, so the nearest solution is always the one that
            // answers. Reuse used to be a scan for any cached directory the file sat under, which
            // took whichever had been resolved first: a file in a nested solution was given the
            // one above it, because that directory encloses it too and nothing had yet looked
            // between the two
            if (directories.TryGetValue(currentDirectory, out var known))
            {
                return known;
            }

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

    static bool TryFind(string directory, string searchPattern, [NotNullWhen(true)] out Result? result)
    {
        string[] solutions;
        try
        {
            solutions = Directory.GetFiles(directory, searchPattern);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            // A directory that is not there, or that cannot be listed, holds no solution this
            // process can see. Keep walking up rather than giving up: a file under a directory
            // that has since been deleted still belongs to the solution above it.
            result = null;
            return false;
        }

        if (solutions.Length != 0)
        {
            result = new(directory, Path.GetFileNameWithoutExtension(solutions.First()));
            directories[directory] = result;
            return true;
        }

        result = null;
        return false;
    }
}
