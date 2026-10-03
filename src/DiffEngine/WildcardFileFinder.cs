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
            return NewestFirst(Directory.EnumerateDirectories(root, segment).ToList());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Newest first, which is what a wildcard in a search directory is for: it stands for the part
    /// of an install path that changes from one release of a tool to the next.
    /// <para>
    /// By version where every name is one, because those are package folders, and when one was
    /// written says when it was restored rather than how new it is. The NuGet cache holds a folder
    /// for each version of DiffEngine any project on the machine has referenced, so restoring an
    /// old project put a viewer from before the arguments this library passes ahead of every
    /// newer one.
    /// </para>
    /// <para>
    /// By when it was written otherwise, as it always was. "Beyond Compare 5" and "vim91" are not
    /// versions, and neither is a number on its own: Visual Studio 2022 installs to a folder
    /// named 2022 and its successor to one named 18.
    /// </para>
    /// </summary>
    static List<string> NewestFirst(List<string> directories)
    {
        var versioned = new List<(string Directory, FolderVersion Version)>();
        foreach (var directory in directories)
        {
            if (!FolderVersion.TryParse(Path.GetFileName(directory), out var version))
            {
                return directories
                    .OrderByDescending(Directory.GetLastWriteTime)
                    .ToList();
            }

            versioned.Add((directory, version));
        }

        return versioned
            .OrderByDescending(_ => _.Version)
            .Select(_ => _.Directory)
            .ToList();
    }

    /// <summary>
    /// A directory name read the way NuGet names a package's folder: two to four numbers, then
    /// whatever follows a hyphen as a prerelease label.
    /// </summary>
    readonly struct FolderVersion(Version numbers, string[] prerelease) :
        IComparable<FolderVersion>
    {
        Version Numbers { get; } = numbers;

        string[] Prerelease { get; } = prerelease;

        public static bool TryParse(string name, out FolderVersion version)
        {
            var hyphen = name.IndexOf('-');
            var numbers = hyphen < 0 ? name : name.Substring(0, hyphen);
            var label = hyphen < 0 ? "" : name.Substring(hyphen + 1);
            // Version.TryParse also takes a sign and surrounding space, neither of which is in a
            // folder NuGet made
            if (numbers.Any(_ => _ != '.' && !char.IsDigit(_)) ||
                (hyphen >= 0 && label.Length == 0) ||
                !Version.TryParse(numbers, out var parsed))
            {
                version = default;
                return false;
            }

            version = new(parsed, label.Length == 0 ? [] : label.Split('.'));
            return true;
        }

        /// <summary>
        /// Semantic versioning's order: the numbers first, a release above every prerelease of
        /// it, and prerelease labels part by part, where a numeric part is compared as a number
        /// and sorts below one that is not.
        /// </summary>
        public int CompareTo(FolderVersion other)
        {
            var byNumbers = Numbers.CompareTo(other.Numbers);
            if (byNumbers != 0)
            {
                return byNumbers;
            }

            var others = other.Prerelease;
            if (Prerelease.Length == 0 ||
                others.Length == 0)
            {
                return others.Length.CompareTo(Prerelease.Length);
            }

            for (var index = 0; index < Math.Min(Prerelease.Length, others.Length); index++)
            {
                var byPart = ComparePart(Prerelease[index], others[index]);
                if (byPart != 0)
                {
                    return byPart;
                }
            }

            return Prerelease.Length.CompareTo(others.Length);
        }

        static int ComparePart(string left, string right)
        {
            var leftIsNumber = ulong.TryParse(left, out var leftNumber);
            var rightIsNumber = ulong.TryParse(right, out var rightNumber);
            if (leftIsNumber &&
                rightIsNumber)
            {
                return leftNumber.CompareTo(rightNumber);
            }

            if (leftIsNumber != rightIsNumber)
            {
                return leftIsNumber ? -1 : 1;
            }

            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static bool TryFind(
        string path,
        [NotNullWhen(true)] out string? result)
    {
        result = FindAll(path).FirstOrDefault();
        if (result != null)
        {
            return true;
        }

        Logging.Write($"Could not find file: {path}");
        return false;
    }

    /// <summary>
    /// Every file the path names, in the order <see cref="TryFind" /> would come to them, for a
    /// caller that may not want the first. One path names several only through a wildcard.
    /// </summary>
    public static IEnumerable<string> FindAll(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (!path.Contains('*'))
        {
            if (File.Exists(expanded))
            {
                yield return expanded;
            }

            yield break;
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
                yield return filePath;
            }
        }
    }
}