namespace DiffEngine;

/// <summary>
/// The two spellings of a path inside a WSL distribution: the Linux one a test process has, and
/// the Windows one a program on the host has to be handed.
/// <para>
/// A Windows drive is mounted into the distribution, at <c>/mnt/c</c> unless configured
/// otherwise, and a file under it is <c>C:\...</c> to the host. Everything else is in the
/// distribution's own file system, which the host reaches as a share:
/// <c>\\wsl.localhost\Ubuntu\home\...</c>.
/// </para>
/// <para>
/// Worked out here rather than by running <c>wslpath</c> per path. A command line is built for
/// every pair, to open a window for one that fails and to find a window to close for one that
/// passes, so a process a path would be two for every verification in a run. What is needed
/// is read once instead: the drive mounts from <c>/proc/mounts</c>, and what the host calls
/// the distribution's root.
/// </para>
/// </summary>
class WslPaths
{
    // Longest first, so a drive mounted beneath another mount is found before its parent
    readonly List<WslMount> byMountPoint;
    readonly List<WslMount> byWindowsRoot;
    readonly string distributionRoot;

    /// <param name="mounts">The Windows drives mounted in the distribution.</param>
    /// <param name="distributionRoot">
    /// What the host calls the distribution's <c>/</c>, as <c>wslpath -w /</c> prints it.
    /// </param>
    public WslPaths(IEnumerable<WslMount> mounts, string distributionRoot)
    {
        var list = mounts.ToList();
        byMountPoint = list
            .OrderByDescending(_ => _.MountPoint.Length)
            .ToList();
        byWindowsRoot = list
            .OrderByDescending(_ => _.WindowsRoot.Length)
            .ToList();
        this.distributionRoot = distributionRoot.TrimEnd('\\');
    }

    /// <summary>
    /// The path as the host writes it. <paramref name="path" /> is absolute.
    /// </summary>
    public string ToWindows(string path)
    {
        foreach (var mount in byMountPoint)
        {
            if (TryRelative(path, mount.MountPoint, out var relative))
            {
                return mount.WindowsRoot + relative.Replace('/', '\\');
            }
        }

        return distributionRoot + path.Replace('/', '\\');
    }

    /// <summary>
    /// The path as the distribution writes it, for one on a drive that is mounted. Anything else,
    /// a path that is already a Linux one among them, has no answer.
    /// </summary>
    public bool TryToLinux(string path, [NotNullWhen(true)] out string? linux)
    {
        var normalized = path.Replace('/', '\\');
        foreach (var mount in byWindowsRoot)
        {
            var root = mount.WindowsRoot;
            if (normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                var relative = normalized.Substring(root.Length).Replace('\\', '/');
                linux = relative.Length == 0 ? mount.MountPoint : $"{mount.MountPoint}/{relative}";
                return true;
            }

            // The drive with nothing after it: "C:"
            if (string.Equals(normalized, root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                linux = mount.MountPoint;
                return true;
            }
        }

        linux = null;
        return false;
    }

    /// <summary>
    /// Whether a path here is on one of the host's drives rather than in the distribution.
    /// </summary>
    public bool IsOnHost(string path)
    {
        var trimmed = path.Length > 1 ? path.TrimEnd('/') : path;
        foreach (var mount in byMountPoint)
        {
            if (TryRelative(trimmed, mount.MountPoint, out _))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The mount point itself, or something beneath it. Not <c>/mnt/cache</c> for <c>/mnt/c</c>,
    /// which only begins the same way.
    /// </summary>
    static bool TryRelative(string path, string mountPoint, out string relative)
    {
        if (path == mountPoint)
        {
            relative = "";
            return true;
        }

        if (path.Length > mountPoint.Length &&
            path[mountPoint.Length] == '/' &&
            path.StartsWith(mountPoint, StringComparison.Ordinal))
        {
            relative = path.Substring(mountPoint.Length + 1);
            return true;
        }

        relative = "";
        return false;
    }

    /// <summary>
    /// The Windows drives in the text of <c>/proc/mounts</c>.
    /// <para>
    /// A drive is a <c>drvfs</c> mount under WSL 1, and under WSL 2 a <c>9p</c> one whose options
    /// name drvfs. Either way the first field is what was mounted, <c>C:\</c> or a share, with
    /// the backslash written as an octal escape as every field's awkward characters are.
    /// </para>
    /// </summary>
    public static List<WslMount> ParseMounts(string content)
    {
        var mounts = new List<WslMount>();
        foreach (var line in content.Split('\n'))
        {
            var fields = line.Trim().Split(' ');
            if (fields.Length < 4)
            {
                continue;
            }

            var type = fields[2];
            if (type != "drvfs" &&
                !(type == "9p" && fields[3].Contains("aname=drvfs")))
            {
                continue;
            }

            var source = Unescape(fields[0]);
            if (!IsWindowsRoot(source))
            {
                continue;
            }

            var mountPoint = Unescape(fields[1]);
            if (mountPoint.Length > 1)
            {
                mountPoint = mountPoint.TrimEnd('/');
            }

            mounts.Add(new(mountPoint, source.TrimEnd('\\') + '\\'));
        }

        return mounts;
    }

    /// <summary>
    /// A drive, with or without its separator, or a share.
    /// </summary>
    static bool IsWindowsRoot(string source)
    {
        if (source.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return source.Length > 2;
        }

        return source.Length >= 2 &&
               char.IsLetter(source[0]) &&
               source[1] == ':';
    }

    /// <summary>
    /// A field of <c>/proc/mounts</c> with its octal escapes read back: a space is <c>\040</c>
    /// and a backslash <c>\134</c>.
    /// </summary>
    static string Unescape(string field)
    {
        if (field.IndexOf('\\') < 0)
        {
            return field;
        }

        var builder = new StringBuilder(field.Length);
        for (var index = 0; index < field.Length; index++)
        {
            var current = field[index];
            if (current == '\\' &&
                index + 3 < field.Length &&
                IsOctal(field[index + 1]) &&
                IsOctal(field[index + 2]) &&
                IsOctal(field[index + 3]))
            {
                var value = (field[index + 1] - '0') * 64 +
                            (field[index + 2] - '0') * 8 +
                            (field[index + 3] - '0');
                builder.Append((char) value);
                index += 3;
                continue;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    static bool IsOctal(char value) =>
        value is >= '0' and <= '7';
}

/// <summary>
/// One Windows drive as a distribution sees it: where it is mounted, without a trailing
/// separator, and what the host calls it, with one.
/// </summary>
readonly record struct WslMount(string MountPoint, string WindowsRoot);
