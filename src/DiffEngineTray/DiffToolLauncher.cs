static class DiffToolLauncher
{
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    public static void Launch(TrackedMove move)
    {
        var process = move.Process;
        if (process is {HasExited: false})
        {
            if (SetForegroundWindow(process.MainWindowHandle))
            {
                return;
            }
        }

        if (move.CanKill)
        {
            process?.Kill();
        }

        process?.Dispose();
        move.Process = null;

        var startInfo = new ProcessStartInfo(move.Exe!, move.Arguments!)
        {
            UseShellExecute = true
        };

        try
        {
            process = Process.Start(startInfo);
            if (process != null)
            {
                move.Process = process;
                return;
            }

            var message = $"""
                           Failed to launch diff tool.
                           {move.Exe} {move.Arguments}
                           """;
            Log.Error(message);
        }
        catch (Exception exception)
        {
            var message = $"""
                           Failed to launch diff tool.
                           {move.Exe} {move.Arguments}
                           """;
            Log.Error(exception, message);
        }
    }
}