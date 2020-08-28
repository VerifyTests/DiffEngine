using System.Diagnostics;
using Serilog;

static class ProcessLauncher
{
    public static void Launch(TrackedMove move)
    {
        var startInfo = new ProcessStartInfo(move.Exe, move.Arguments)
        {
            UseShellExecute = true
        };

        using var process = Process.Start(startInfo);
        if (process != null)
        {
            move.AddProcess(process.Id, process.StartTime);
            return;
        }
        var message = $@"Failed to launch diff tool.
{move.Exe} {move.Arguments}";
        Log.Error(message);
    }
}