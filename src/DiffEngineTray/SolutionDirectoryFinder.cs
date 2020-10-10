using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

static class SolutionDirectoryFinder
{
    static ConcurrentDictionary<string, string?> cache = new ConcurrentDictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    public static bool TryFind(string file, [NotNullWhen(true)] out string? path)
    {
        path = cache.GetOrAdd(file, Inner);
        return path != null;
    }

    static string? Inner(string file)
    {
        var currentDirectory = Path.GetDirectoryName(file)!;
        do
        {
            var solutions = Directory.GetFiles(currentDirectory, "*.sln");
            if (solutions.Any())
            {
                return Path.GetFileNameWithoutExtension(solutions.First());
            }

            var parent = Directory.GetParent(currentDirectory);
            if (parent == null)
            {
                return null;
            }

            currentDirectory = parent.FullName;
        } while (true);
    }
}