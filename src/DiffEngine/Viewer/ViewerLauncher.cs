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

    static Process? Start(InlinePatch patch)
    {
        if (!DiffTools.TryFindByName(DiffTool.DiffEngineViewer, out var tool))
        {
            return null;
        }

        // The source and line go on the command line, not just in the payload, so each launch is
        // distinguishable: ProcessCleanup matches on command line, and it makes the process
        // readable in a task manager.
        var arguments = $"--inline --source \"{patch.SourceFile}\" --line {patch.LineHint}";
        var info = new ProcessStartInfo(tool.ExePath, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            // The patch is written here rather than passed as an argument: snapshots routinely
            // exceed the command line length limit and would need escaping.
            RedirectStandardInput = true
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
