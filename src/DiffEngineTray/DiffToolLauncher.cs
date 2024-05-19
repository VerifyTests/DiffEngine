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
            // Given the full exe path is known we dont need UseShellExecute https://stackoverflow.com/a/5255335
            UseShellExecute = false
        };

        try
        {
            process = Process.Start(startInfo);
            if (process != null)
            {
                move.Process = process;
                return;
            }

            Log.Error(
                $"""
                 Failed to launch diff tool.
                 {move.Exe} {move.Arguments}
                 """);
        }
        catch (Exception exception)
        {
            Log.Error(
                exception,
                $"""
                 Failed to launch diff tool.
                 {move.Exe} {move.Arguments}
                 """);
        }
    }
}