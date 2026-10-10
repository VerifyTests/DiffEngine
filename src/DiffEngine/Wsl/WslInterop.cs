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
/// windowed tool, so the tool itself is ended instead, on the host (<see cref="Kill" />). And it
/// ends with the terminal session that started it, while the window stays: a tool left open
/// across sessions is no longer found, and so neither refreshed nor closed.
/// </para>
/// </summary>
static class WslInterop
{
    /// <summary>
    /// Set to <c>false</c> to leave Windows tools out and offer only what the distribution has.
    /// </summary>
    internal const string Variable = "DiffEngine_WslWindowsTools";

    // See Kill
    const int killTimeout = 30000;

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
    /// Closes the Windows tool showing a pair, and reports whether one was closed.
    /// <para>
    /// The process this side could signal is WSL's stand-in, and a windowed tool outlives that.
    /// So the tool is found where it runs, by Windows PowerShell on the host: the processes of
    /// the tool's image whose command line names both files, by the paths the tool was handed.
    /// By both, because a tool can be showing the same received file against another target,
    /// and by image, so nothing else that happens to mention them is ended.
    /// </para>
    /// <para>
    /// That is a process started and a question put to Windows, a third of a second, where
    /// ending a process here is nothing. So it is only asked once the stand-in has been found
    /// by its command, which is what says a window was opened for this pair and is still there:
    /// a passing verification with no window pays for a look through a list, as it does for
    /// any other tool.
    /// </para>
    /// <para>
    /// A third of a second is a machine that has run PowerShell lately. One that has not takes
    /// several to load what the question needs, and a build agent under WSL 1 took more than
    /// five: the wait given to the programs that describe the host. Giving up there left the
    /// tool open and said nothing was closed, so this has a wait of its own, long enough for
    /// that first run. It is spent only where there is a window to close.
    /// </para>
    /// </summary>
    public static bool Kill(ResolvedTool tool, string tempFile, string targetFile)
    {
        var command = tool.BuildCommand(tempFile, targetFile);
        if (!ProcessCleanup.IsRunning(command))
        {
            return false;
        }

        if (!Host!.TryFindPowerShell(out var powerShell))
        {
            Logging.Write($"Windows PowerShell was not found, so the tool was left open. Command: {command}");
            return false;
        }

        var script = KillScript(Path.GetFileName(tool.ExePath), ToWindows(tempFile), ToWindows(targetFile));
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var output = WslHost.Run(
            powerShell,
            $"-NoProfile -NonInteractive -EncodedCommand {encoded}",
            Path.GetDirectoryName(powerShell)!,
            Encoding.UTF8,
            killTimeout);
        var closed = int.TryParse(output?.Trim(), out var count) && count > 0;
        Logging.Write($"Kill on the Windows host: {command}. Closed: {closed}");
        return closed;
    }

    /// <summary>
    /// What PowerShell is asked to run: end every process of this image whose command line
    /// names both paths, and say how many there were.
    /// <para>
    /// The script is handed over encoded rather than as text, so nothing has to survive two
    /// command lines, and each value is encoded again inside it: a path can hold a quote, and
    /// PowerShell takes four other characters for one. The comparison ignores case, as the file
    /// system the paths are on does.
    /// </para>
    /// </summary>
    internal static string KillScript(string image, string tempFile, string targetFile) =>
        // The image is asked for in the query, so Windows hands back the tool's processes and
        // not every process on the machine with its command line. In a query a backslash and a
        // quote are each written behind a backslash
        $$"""
          function Decode($value) { [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($value)) }
          $image = Decode '{{Encode(image)}}'
          $temp = Decode '{{Encode(tempFile)}}'
          $target = Decode '{{Encode(targetFile)}}'
          $name = $image.Replace('\', '\\').Replace("'", "\'")
          $found = @(Get-CimInstance Win32_Process -Filter "Name = '$name'" | Where-Object {
              $_.CommandLine -and
              $_.CommandLine.IndexOf($temp, [StringComparison]::OrdinalIgnoreCase) -ge 0 -and
              $_.CommandLine.IndexOf($target, [StringComparison]::OrdinalIgnoreCase) -ge 0
          })
          $found | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
          $found.Count
          """;

    static string Encode(string value) =>
        Convert.ToBase64String(Encoding.Unicode.GetBytes(value));

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
