namespace DiffEngine;

/// <summary>
/// The Windows machine a WSL distribution runs on, as far as finding a diff tool installed there
/// needs it: where its drives are mounted, and the variables its install directories are written
/// with.
/// <para>
/// Those variables are the host's and not this process's. <c>%ProgramFiles%</c> and
/// <c>%LocalAppData%</c> do not exist inside a distribution, so they are asked of the host once,
/// by running its <c>cmd.exe</c>, which a distribution can do for the same reason it can run the
/// tool.
/// </para>
/// </summary>
class WslHost
{
    // For the two programs asked what the host looks like, each run once a process. Long enough
    // for a machine under load, and short enough that one which never answers does not hold up
    // the first verification for good
    const int timeout = 5000;

    readonly IReadOnlyDictionary<string, string> variables;
    readonly Lazy<Dictionary<string, string>> programsOnPath;

    /// <param name="paths">How a path here is written on the host, and the reverse.</param>
    /// <param name="variables">The host's environment variables.</param>
    /// <param name="pathDirectories">
    /// This process's PATH. WSL appends the host's to it, so a program on the host's is found in
    /// one of these.
    /// </param>
    public WslHost(WslPaths paths, IReadOnlyDictionary<string, string> variables, IReadOnlyList<string> pathDirectories)
    {
        Paths = paths;
        this.variables = variables;
        programsOnPath = new(() => ListPrograms(pathDirectories));
    }

    public WslPaths Paths { get; }

    /// <summary>
    /// A Windows program on the PATH, by its file name.
    /// <para>
    /// Answered from one listing of each of the host's directories on the PATH, made the first
    /// time anything is asked for. The ordinary lookup asks each directory about each name, and
    /// a question put to a Windows drive from inside WSL takes most of a millisecond: thirty
    /// directories and twenty tools were over half a second of every test run, where listing
    /// them once is a tenth of that.
    /// </para>
    /// </summary>
    public bool TryFindOnPath(string name, [NotNullWhen(true)] out string? path) =>
        programsOnPath.Value.TryGetValue(name, out path);

    /// <summary>
    /// Every <c>.exe</c> in the host's directories on the PATH, the first of each name winning
    /// as it does for the PATH itself. Names are matched without regard to case, as the host
    /// matches them.
    /// <para>
    /// Not the Windows directory or anything in it. No diff tool is installed there, and it
    /// holds most of the files on a PATH.
    /// </para>
    /// </summary>
    Dictionary<string, string> ListPrograms(IReadOnlyList<string> directories)
    {
        var programs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var systemRoot = SystemRoot();
        foreach (var directory in directories)
        {
            if (!Paths.IsOnHost(directory) ||
                IsWithin(directory, systemRoot))
            {
                continue;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    if (!file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var name = Path.GetFileName(file);
                    if (!programs.ContainsKey(name))
                    {
                        programs[name] = file;
                    }
                }
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                // A PATH entry that is not there, or cannot be read, holds nothing to find
            }
        }

        return programs;
    }

    /// <summary>
    /// The host's Windows directory as this process sees it, or null when the host did not say
    /// where it is or its drive is not mounted.
    /// </summary>
    string? SystemRoot()
    {
        if (variables.TryGetValue("SystemRoot", out var windowsDirectory) &&
            Paths.TryToLinux(windowsDirectory, out var systemRoot))
        {
            return systemRoot;
        }

        return null;
    }

    /// <summary>
    /// Windows PowerShell, which every Windows this can run on has, and which is the one way to
    /// ask the host about its own processes from here. Looked for where Windows keeps it before
    /// the PATH, since that is one question rather than one for each directory on it.
    /// </summary>
    public bool TryFindPowerShell([NotNullWhen(true)] out string? path)
    {
        if (SystemRoot() is { } systemRoot)
        {
            path = $"{systemRoot}/System32/WindowsPowerShell/v1.0/powershell.exe";
            if (File.Exists(path))
            {
                return true;
            }
        }

        return OsSettingsResolver.TryFindInEnvPath("powershell.exe", out path);
    }

    static bool IsWithin(string directory, string? parent)
    {
        if (parent is null)
        {
            return false;
        }

        var trimmed = directory.TrimEnd('/');
        // A Windows drive ignores case, and a PATH spells this directory several ways
        return trimmed.StartsWith(parent, StringComparison.OrdinalIgnoreCase) &&
               (trimmed.Length == parent.Length || trimmed[parent.Length] == '/');
    }

    /// <summary>
    /// The directories a Windows definition searches, as paths this process can look in.
    /// <para>
    /// One written with a variable the host does not define, or naming a drive that is not
    /// mounted, is left out: there is nowhere here to look for it.
    /// </para>
    /// </summary>
    public IEnumerable<string> SearchDirectories(IEnumerable<string> directories)
    {
        foreach (var directory in directories)
        {
            if (Expand(directory) is { } expanded &&
                Paths.TryToLinux(expanded, out var linux))
            {
                yield return linux;
            }
        }
    }

    /// <summary>
    /// <paramref name="directory" /> with each <c>%NAME%</c> replaced by the host's value for it,
    /// or null when the host has none.
    /// </summary>
    internal string? Expand(string directory)
    {
        var builder = new StringBuilder(directory.Length);
        var position = 0;
        while (position < directory.Length)
        {
            var start = directory.IndexOf('%', position);
            if (start < 0)
            {
                break;
            }

            var end = directory.IndexOf('%', start + 1);
            if (end < 0)
            {
                break;
            }

            var name = directory.Substring(start + 1, end - start - 1);
            if (!variables.TryGetValue(name, out var value))
            {
                return null;
            }

            builder.Append(directory, position, start - position);
            builder.Append(value);
            position = end + 1;
        }

        builder.Append(directory, position, directory.Length - position);
        return builder.ToString();
    }

    /// <summary>
    /// What <c>cmd.exe /c set</c> printed, as names and values. Names are matched without regard
    /// to case, as Windows matches them: definitions write both <c>%LocalAppData%</c> and
    /// <c>%LOCALAPPDATA%</c>.
    /// </summary>
    public static Dictionary<string, string> ParseVariables(string output)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            var separator = trimmed.IndexOf('=');
            // Not at 0: cmd keeps a current directory per drive in variables named "=C:"
            if (separator < 1)
            {
                continue;
            }

            result[trimmed.Substring(0, separator)] = trimmed.Substring(separator + 1);
        }

        return result;
    }

    /// <summary>
    /// The host of this process, or null when it has none it can start a program on: not Linux,
    /// not inside WSL, turned off with <c>DiffEngine_WslWindowsTools</c>, or WSL's own switch for
    /// running Windows programs is off.
    /// <para>
    /// Never throws. This runs from the type initializer of DiffTools, where a throw is a
    /// TypeInitializationException for every later use in the process.
    /// </para>
    /// </summary>
    public static WslHost? Detect()
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                !BuildServerDetector.IsWsl ||
                TurnedOff() ||
                !RunsWindowsPrograms())
            {
                return null;
            }

            var mounts = WslPaths.ParseMounts(File.ReadAllText("/proc/mounts"));
            // No drive, so no directory a tool could be installed in is reachable from here
            if (mounts.Count == 0)
            {
                return null;
            }

            var root = DistributionRoot();
            if (root is null)
            {
                return null;
            }

            return new(new(mounts, root), ReadVariables(mounts), OsSettingsResolver.EnvPaths);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"DiffEngine: Could not read the WSL host. Windows diff tools will not be offered. {exception.Message}");
            return null;
        }
    }

    static bool TurnedOff()
    {
        var value = Environment.GetEnvironmentVariable(WslInterop.Variable);
        return value != null &&
               bool.TryParse(value, out var enabled) &&
               !enabled;
    }

    /// <summary>
    /// Whether the kernel hands a Windows executable to WSL to run. That is a binfmt_misc
    /// registration, absent when interop is turned off in <c>wsl.conf</c> or <c>.wslconfig</c>,
    /// and named differently by a distribution that registers it after systemd has started.
    /// </summary>
    static bool RunsWindowsPrograms()
    {
        foreach (var name in new[] { "WSLInterop", "WSLInterop-late" })
        {
            var file = $"/proc/sys/fs/binfmt_misc/{name}";
            if (File.Exists(file) &&
                File.ReadLines(file).FirstOrDefault() == "enabled")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What the host calls the distribution's <c>/</c>. Asked of <c>wslpath</c> rather than
    /// built from the distribution's name, because the share is <c>\\wsl.localhost</c> on a
    /// current Windows and <c>\\wsl$</c> on an older one, and only WSL knows which.
    /// </summary>
    static string? DistributionRoot()
    {
        if (TryFind("wslpath", ["/usr/bin/wslpath", "/bin/wslpath"], out var wslpath) &&
            Run(wslpath, "-w /", "/", Encoding.UTF8) is { } output)
        {
            var root = output.Trim();
            if (root.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return root;
            }
        }

        var distribution = Environment.GetEnvironmentVariable("WSL_DISTRO_NAME");
        if (string.IsNullOrEmpty(distribution))
        {
            return null;
        }

        return $@"\\wsl.localhost\{distribution}";
    }

    /// <summary>
    /// The host's environment, or nothing when it could not be asked. Nothing still leaves a
    /// tool on the PATH to be found, since WSL appends the host's PATH to the distribution's.
    /// </summary>
    static Dictionary<string, string> ReadVariables(List<WslMount> mounts)
    {
        var candidates = mounts
            .Select(_ => $"{_.MountPoint}/Windows/System32/cmd.exe")
            .ToArray();
        if (!TryFind("cmd.exe", candidates, out var cmd))
        {
            return ParseVariables("");
        }

        // /u for UTF-16, since the default is the console's code page and a user directory need
        // not fit in it. Started in its own directory: given one inside the distribution, cmd
        // says at length that it cannot use it
        var output = Run(cmd, "/u /c set", Path.GetDirectoryName(cmd)!, Encoding.Unicode);
        return ParseVariables(output ?? "");
    }

    static bool TryFind(string name, string[] candidates, [NotNullWhen(true)] out string? path)
    {
        if (OsSettingsResolver.TryFindInEnvPath(name, out path))
        {
            return true;
        }

        path = candidates.FirstOrDefault(File.Exists);
        return path != null;
    }

    /// <summary>
    /// What a program printed, or null when it did not start, failed, or did not finish in time.
    /// </summary>
    internal static string? Run(string file, string arguments, string directory, Encoding encoding, int wait = timeout)
    {
        using var process = new Process
        {
            StartInfo = new(file, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = encoding,
                WorkingDirectory = directory
            }
        };
        try
        {
            process.Start();
        }
        catch (Exception exception)
            when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }

        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        // Read so that a program with a lot to say there is not left waiting on a full pipe
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(wait))
        {
            try
            {
                process.Kill();
            }
            catch (Exception exception)
                when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Gone between the wait and the kill
            }

            return null;
        }

        if (process.ExitCode != 0 ||
            !Task.WaitAll([output, error], wait))
        {
            return null;
        }

        return output.Result;
    }
}
