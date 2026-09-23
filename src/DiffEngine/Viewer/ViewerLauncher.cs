namespace DiffEngine;

/// <summary>
/// Starts DiffEngineViewer.
/// <para>
/// Resolution goes through the normal tool discovery, so the bundled copy, a globally installed
/// dotnet tool and a <c>DiffEngine_DiffEngineViewer</c> override all work the same way.
/// </para>
/// <para>
/// Nothing the test host holds is handed to the viewer. The host's own output goes to a pipe
/// <c>dotnet test</c> reads until every writer has closed it, and a viewer that inherited the
/// write end kept the whole run from returning until its window was closed - 23 seconds for a
/// run that took 1.5, against a child that lived for 22. On Windows a process started without
/// ShellExecute inherits every inheritable handle, whatever is redirected, so the launch uses
/// ShellExecute, which inherits none. Elsewhere .NET marks every descriptor it opens
/// close-on-exec, so only the three standard streams pass to a child, and redirecting all three
/// and closing this side of them is enough.
/// </para>
/// </summary>
static class ViewerLauncher
{
    /// <summary>
    /// Starts a viewer with a patch, which goes in a file rather than on stdin: a launch that
    /// redirects stdin cannot use ShellExecute (see the class remarks). The viewer reads the file
    /// and deletes it.
    /// </summary>
    public static async Task<bool> LaunchAsync(InlinePatch patch, string payload, Cancel cancel)
    {
        var file = Path.Combine(Path.GetTempPath(), $"DiffEngineViewer_{Guid.NewGuid():N}.inlinepatch");
        try
        {
            // Bytes rather than text, so no preamble: a BOM is exactly what a .NET Framework
            // writer used to put in front of a payload on stdin
            var bytes = Encoding.UTF8.GetBytes(payload);
            // ReSharper disable once UseAwaitUsing
            using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
#if NET6_0_OR_GREATER
            await stream.WriteAsync(bytes.AsMemory(), cancel);
#else
            await stream.WriteAsync(bytes, 0, bytes.Length, cancel);
#endif
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Failed to write the inline patch for DiffEngineViewer: {exception}");
            TryDelete(file);
            return false;
        }

        if (Start(PayloadArguments(patch, file)) is null)
        {
            TryDelete(file);
            return false;
        }

        return true;
    }

    /// <summary>
    /// The source and line go on the command line, not just in the payload, so each launch is
    /// distinguishable: ProcessCleanup matches on command line, and it makes the process readable
    /// in a task manager.
    /// </summary>
    internal static string PayloadArguments(InlinePatch patch, string file) =>
        $"--inline --source \"{patch.SourceFile}\" --line {patch.LineHint} --payload \"{file}\"";

    static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Starts a viewer to display a queue this process owns. No payload, because there is nothing
    /// to hand over: the viewer reads the queue back over the socket. The process is returned so
    /// the owner can tell whether it still has a window rather than probing a port it holds itself.
    /// </summary>
    public static Process? LaunchAttached() =>
        Start("--attach");

    /// <summary>
    /// Starts a viewer holding one pending delete, for when no tray is running and nothing owns
    /// the queue.
    /// <para>
    /// On the command line rather than in a payload file, which is what an inline patch needs: a
    /// path fits inside the length limit where snapshot content does not. It also keeps each
    /// launch distinguishable, which is what ProcessCleanup matches on.
    /// </para>
    /// <para>
    /// Two deletes racing both launch. Only one binds the port; the other forwards its delete to
    /// the winner and exits, which is the same resolution a second inline viewer reaches.
    /// </para>
    /// </summary>
    public static bool LaunchDelete(string file) =>
        Start($"--delete \"{file}\"") is not null;

    /// <summary>
    /// Starts a viewer holding one failing pair, for when the tool resolved for that pair is the
    /// viewer itself and nothing owns the queue. The same launch <see cref="LaunchDelete"/> makes,
    /// for the same reason: the pair joins a queue that later pairs can join too.
    /// </summary>
    public static bool LaunchDiff(string temp, string target) =>
        Start(DiffArguments(temp, target)) is not null;

    /// <summary>
    /// Built here rather than at each caller, because the tray stores these arguments against the
    /// tracked move and re-runs them for "Open diff tool". A relaunch that did not say --diff would
    /// open a window of its own instead of raising the queue the pair is already in.
    /// </summary>
    public static string DiffArguments(string temp, string target) =>
        $"--diff \"{temp}\" \"{target}\"";

    static Process? Start(string arguments)
    {
        // With nowhere to draw, a viewer binds the port, fails to open its window and exits, and
        // to whoever launched it the bind reads as a viewer that took the work. Not starting one
        // tells the caller no viewer was found instead, which is the answer that has it keep what
        // it sent.
        if (!HasDisplay(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), Environment.GetEnvironmentVariable))
        {
            return null;
        }

        if (!DiffTools.TryFindByName(DiffTool.DiffEngineViewer, out var tool))
        {
            return null;
        }

        try
        {
            return Start(StartInfo(tool.ExePath, arguments, RuntimeInformation.IsOSPlatform(OSPlatform.Windows)));
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"Failed to launch DiffEngineViewer: {exception}");
            return null;
        }
    }

    /// <summary>
    /// How a viewer is started so that it holds nothing of the test host's: see the class
    /// remarks.
    /// </summary>
    internal static ProcessStartInfo StartInfo(string exePath, string arguments, bool windows)
    {
        if (windows)
        {
            return new(exePath, arguments)
            {
                UseShellExecute = true
            };
        }

        return new(exePath, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
    }

    static Process? Start(ProcessStartInfo info)
    {
        var process = Process.Start(info);
        if (process is null ||
            info.UseShellExecute)
        {
            return process;
        }

        // Only this side's ends. The viewer then reads end of input, and a write to either output
        // meets a closed pipe, which .NET's console ignores rather than failing on
        process.StandardInput.Close();
        process.StandardOutput.Close();
        process.StandardError.Close();
        return process;
    }

    /// <summary>
    /// Whether a window started from this process has anywhere to go. Only Linux can be asked: an
    /// SSH session or a container has neither variable, while a desktop session, a forwarded X
    /// connection and WSLg each set one. Windows and macOS are taken to have a desktop, since
    /// nothing in the environment says otherwise.
    /// </summary>
    internal static bool HasDisplay(bool linux, Func<string, string?> variable) =>
        !linux ||
        !string.IsNullOrEmpty(variable("DISPLAY")) ||
        !string.IsNullOrEmpty(variable("WAYLAND_DISPLAY"));
}
