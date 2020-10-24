using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;

static class SolutionDirectoryFinder
{
    class Result
    {
        public string Directory { get; }
        public string Name { get; }

        public Result(string directory, string name)
        {
            Directory = directory;
            Name = name;
        }
    }

    static ConcurrentDictionary<string, Result?> cache = new ConcurrentDictionary<string, Result?>(StringComparer.OrdinalIgnoreCase);

    public static string? Find(string file)
    {
        return cache.GetOrAdd(file, Inner)?.Name;
    }

    static Result? Inner(string file)
    {
        foreach (var result in cache.Values.Where(x => x != null))
        {
            if (file.StartsWith(result!.Directory))
            {
                return result;
            }
        }

        var currentDirectory = Path.GetDirectoryName(file);
        if (string.IsNullOrEmpty(currentDirectory))
        {
            return null;
        }

        do
        {
            var solutions = Directory.GetFiles(currentDirectory, "*.sln");
            if (solutions.Any())
            {
                return new Result(currentDirectory, Path.GetFileNameWithoutExtension(solutions.First()));
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