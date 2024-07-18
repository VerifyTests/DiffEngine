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
            var solutions = Directory.GetFiles(currentDirectory, "*.sln");
            if (solutions.Length != 0)
            {
                return new(currentDirectory, Path.GetFileNameWithoutExtension(solutions.First()));
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