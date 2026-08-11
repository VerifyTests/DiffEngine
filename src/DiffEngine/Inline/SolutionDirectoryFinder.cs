using System.Collections.Concurrent;

namespace DiffEngine;

static class SolutionDirectoryFinder
{
    class Result(string directory, string name)
    {
        public string Directory { get; } = directory;
        public string Name { get; } = name;
    }

    static ConcurrentDictionary<string, Result?> cache = new(StringComparer.OrdinalIgnoreCase);

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
            return true;
        }

        result = null;
        return false;
    }
}
