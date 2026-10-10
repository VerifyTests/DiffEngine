namespace DiffEngine;

/// <summary>
/// Windows diff tools, for a test process running inside WSL.
/// <para>
/// A distribution can start a Windows executable: the kernel recognises the file and hands it to
/// WSL, which runs it on the host. So a tool installed on Windows is offered when the
/// distribution has no copy of its own, found through its Windows definition
/// (<see cref="OsSettingsResolver" />) and handed the two files by the paths the host knows them
/// by (<see cref="WslPaths" />).
/// </para>
/// <para>
/// What this side holds for a tool started that way is not the tool. It is a process of WSL's
/// that stands in for it, and three things follow. It is listed by <c>ps</c> with WSL's own
/// program in front of the tool's command line, which <see cref="StripProxy" /> takes off so a
/// running tool is still found by the command it was started with. Ending it does not close a
/// windowed tool, so such a tool is never killed from here: it is not closed when its test
/// passes, and one that does not refresh gets a second window rather than a replacement. And it
/// ends with the terminal session that started it, while the window stays.
/// </para>
/// </summary>
static class WslInterop
{
    /// <summary>
    /// Set to <c>false</c> to leave Windows tools out and offer only what the distribution has.
    /// </summary>
    internal const string Variable = "DiffEngine_WslWindowsTools";

    static readonly Lazy<WslHost?> host = new(WslHost.Detect);
    static readonly ConcurrentDictionary<string, bool> programs = new(StringComparer.Ordinal);

    /// <summary>
    /// The Windows machine this process can start a tool on, or null: see
    /// <see cref="WslHost.Detect" />.
    /// </summary>
    public static WslHost? Host => host.Value;

    /// <summary>
    /// Whether a Windows copy of a tool is looked for at all.
    /// <para>
    /// Not the viewer. It is not handed a pair on a command line and left to it: it is spoken to
    /// over a loopback port, and the host's loopback is not the distribution's. Not the editors
    /// that run in the terminal they were started from either, since a test process has none to
    /// give a Windows console program.
    /// </para>
    /// </summary>
    public static bool Offers(DiffTool? tool) =>
        tool is not (DiffTool.DiffEngineViewer or DiffTool.Vim or DiffTool.Neovim);

    /// <summary>
    /// Whether starting this file here starts a program on the Windows host.
    /// <para>
    /// Asked of the file, by the two bytes the kernel itself goes by, rather than of how the tool
    /// was resolved: one registered with a path of the caller's own is as much a Windows program
    /// as one found through a definition, and is no more able to open a Linux path.
    /// </para>
    /// </summary>
    public static bool IsWindowsProgram(string exePath) =>
        Host != null &&
        programs.GetOrAdd(exePath, HasWindowsHeader);

    static bool HasWindowsHeader(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return stream.ReadByte() == 'M' &&
                   stream.ReadByte() == 'Z';
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// A file's path as a program on the host has to be given it.
    /// </summary>
    public static string ToWindows(string path) =>
        Host!.Paths.ToWindows(Path.GetFullPath(path));

    /// <summary>
    /// Starts a Windows tool and returns the id of the process that stands in for it here.
    /// <para>
    /// Nothing of the test host's is handed over, for the reason <see cref="ViewerLauncher" />
    /// gives: a child holding the pipe <c>dotnet test</c> reads the host's output from keeps the
    /// run from returning until it exits, and this one lives as long as the tool's window. All
    /// three streams are redirected and this side of them closed. A windowed tool has nothing to
    /// say on them. One that does write ends its stand-in by doing so, which costs only finding
    /// the tool again later, since its window stays.
    /// </para>
    /// <para>
    /// Started in the tool's own directory rather than the host's, which the arguments no longer
    /// depend on: both paths are absolute by the time they are Windows ones.
    /// </para>
    /// </summary>
    public static int Start(string exePath, string arguments)
    {
        var startInfo = new ProcessStartInfo(exePath, arguments)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? ""
        };
        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new($"No process was started for {exePath}");
        }

        process.StandardInput.Close();
        process.StandardOutput.Close();
        process.StandardError.Close();
        return process.Id;
    }

    /// <summary>
    /// The command line a Windows tool was started with, from the one <c>ps</c> lists for it.
    /// <para>
    /// WSL registers its own <c>/init</c> as the interpreter for Windows executables, and asks
    /// the kernel to keep the caller's first argument. So the process is <c>/init</c>, given the
    /// path the kernel resolved and then everything the caller passed, which begins with that
    /// same path: <c>/init /mnt/c/Tool/tool.exe /mnt/c/Tool/tool.exe left right</c>. What follows
    /// the first copy is the command DiffEngine built.
    /// </para>
    /// <para>
    /// The path can hold spaces, and nothing in the line says where it ends, so it is found as
    /// the text that is there twice over.
    /// </para>
    /// </summary>
    public static string StripProxy(string command)
    {
        const string proxy = "/init ";
        if (!command.StartsWith(proxy, StringComparison.Ordinal))
        {
            return command;
        }

        var rest = command.Substring(proxy.Length);
        var space = rest.IndexOf(' ');
        while (space > 0)
        {
            var path = rest.Substring(0, space);
            var after = rest.Substring(space + 1);
            if (after.StartsWith(path, StringComparison.Ordinal) &&
                (after.Length == path.Length || after[path.Length] == ' '))
            {
                return after;
            }

            space = rest.IndexOf(' ', space + 1);
        }

        return command;
    }
}
