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
        foreach (var result in cache.Values.Where(_ => _ != null))
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