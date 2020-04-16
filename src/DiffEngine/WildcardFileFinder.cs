using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

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
        var currentSearchRoots = new List<string>{segments[0] + Path.DirectorySeparatorChar};
        foreach (var segment in segments.Skip(1))
        {
            var newSearchRoots = new List<string>();
            foreach (var searchRoot in currentSearchRoots)
            {
                if (segment.Contains('*'))
                {
                    newSearchRoots.AddRange(Directory.EnumerateDirectories(searchRoot, segment)
                        .OrderByDescending(Directory.GetLastWriteTime));
                }
                else
                {
                    var newSearchRoot = Path.Combine(searchRoot, segment);
                    if (Directory.Exists(newSearchRoot))
                    {
                        newSearchRoots.Add(newSearchRoot);
                    }
                }
            }

            if (!newSearchRoots.Any())
            {
                return Enumerable.Empty<string>();
            }
            currentSearchRoots = newSearchRoots;
        }

        return currentSearchRoots;
    }

    public static bool TryFind(string path, [NotNullWhen(true)] out string? result)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (!path.Contains('*'))
        {
            if (File.Exists(expanded))
            {
                result = expanded;
                return true;
            }

            result = null;
            return false;
        }

        var filePart = Path.GetFileName(expanded);
        var directoryPart = Path.GetDirectoryName(expanded);
        foreach (var directory in EnumerateDirectories(directoryPart))
        {
            if (filePart.Contains('*'))
            {
                throw new Exception("Wildcard in file part currently not supported.");
            }

            var filePath = Path.Combine(directory, filePart);
            if (File.Exists(filePath))
            {
                result = filePath;
                return true;
            }
        }

        result = null;
        return false;
    }
}