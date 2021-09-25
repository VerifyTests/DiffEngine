using System.Diagnostics.CodeAnalysis;
using System.Linq;
using DiffEngine;

static class WildcardFileFinder
{
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
                return new List<string> {directory};
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

    public static bool TryFindExe(
        IEnumerable<string> paths,
        [NotNullWhen(true)] out string? exePath)
    {
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (TryFind(path, out exePath))
            {
                return true;
            }
        }

        exePath = null;
        return false;
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