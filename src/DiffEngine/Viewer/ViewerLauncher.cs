namespace DiffEngine;

/// <summary>
/// Starts DiffEngineViewer with a patch on stdin.
/// <para>
/// Resolution goes through the normal tool discovery, so the bundled copy, a globally installed
/// dotnet tool and a <c>DiffEngine_DiffEngineViewer</c> override all work the same way.
/// </para>
/// </summary>
static class ViewerLauncher
{
    public static async Task<bool> LaunchAsync(InlinePatch patch, string payload, Cancel cancel)
    {
        var process = Start(patch);
        if (process == null)
        {
            return false;
        }

        try
        {
            // Written as bytes rather than through the StreamWriter: on .NET Framework that
            // writer uses Console.InputEncoding, which is UTF8 *with* a preamble, so the viewer
            // received a leading BOM and rejected the payload.
            var bytes = Encoding.UTF8.GetBytes(payload);
            var stream = process.StandardInput.BaseStream;
#if NET6_0_OR_GREATER
            await stream.WriteAsync(bytes.AsMemory(), cancel);
#else
            await stream.WriteAsync(bytes, 0, bytes.Length, cancel);
#endif
            await stream.FlushAsync(cancel);
            process.StandardInput.Close();
            return true;
        }
        catch (IOException)
        {
            return false;
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
    /// On the command line rather than on stdin, which is what an inline patch needs: a path fits
    /// inside the length limit where snapshot content does not. It also keeps each launch
    /// distinguishable, which is what ProcessCleanup matches on.
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

    static Process? Start(InlinePatch patch) =>
        // The source and line go on the command line, not just in the payload, so each launch is
        // distinguishable: ProcessCleanup matches on command line, and it makes the process
        // readable in a task manager.
        Start($"--inline --source \"{patch.SourceFile}\" --line {patch.LineHint}", stdin: true);

    static Process? Start(string arguments, bool stdin = false)
    {
        if (!DiffTools.TryFindByName(DiffTool.DiffEngineViewer, out var tool))
        {
            return null;
        }

        var info = new ProcessStartInfo(tool.ExePath, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            // A patch is written to stdin rather than passed as an argument: snapshots routinely
            // exceed the command line length limit and would need escaping.
            RedirectStandardInput = stdin
        };

        try
        {
            return Process.Start(info);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"Failed to launch DiffEngineViewer: {exception}");
            return null;
        }
    }
}
