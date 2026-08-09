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
#if NET6_0_OR_GREATER
            await process.StandardInput.WriteAsync(payload.AsMemory(), cancel);
#else
            await process.StandardInput.WriteAsync(payload);
#endif
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
