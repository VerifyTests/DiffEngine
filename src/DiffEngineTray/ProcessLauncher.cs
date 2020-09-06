using System;
using System.Diagnostics;
using Serilog;

static class ProcessLauncher
{
    public static void Launch(TrackedMove move)
    {
        move.Process?.Dispose();
        move.Process = null;

        var startInfo = new ProcessStartInfo(move.Exe, move.Arguments)
        {
            UseShellExecute = true
        };

        try
        {
            var process = Process.Start(startInfo);
            if (process != null)
            {
                move.Process = process;
                return;
            }

            var message = $@"Failed to launch diff tool.
{move.Exe} {move.Arguments}";
            Log.Error(message);
        }
        catch (Exception exception)
        {
            var message = $@"Failed to launch diff tool.
{move.Exe} {move.Arguments}";
            Log.Error(exception, message);
        }
    }
}