static class DiffToolLauncher
{
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    public static void Launch(TrackedMove move) =>
        Launch(move.Exe!, move.Arguments!, move.CanKill, move.Process, _ => move.Process = _);

    // Inline diff processes are always tray owned, so always killable
    public static void Launch(TrackedInlineMove move) =>
        Launch(move.Exe!, move.Arguments!, canKill: true, move.Process, _ => move.Process = _);

    static void Launch(string exe, string arguments, bool canKill, Process? process, Action<Process?> assign)
    {
        if (process is { HasExited: false })
        {
            if (SetForegroundWindow(process.MainWindowHandle))
            {
                return;
            }
        }

        if (canKill)
        {
            process?.Kill();
        }

        process?.Dispose();
        assign(null);

        var startInfo = new ProcessStartInfo(exe, arguments)
        {
            // Given the full exe path is known we dont need UseShellExecute https://stackoverflow.com/a/5255335
            // however UseShellExecute allows the test running to not block when the difftool is launched
            // https://github.com/VerifyTests/Verify/issues/1229
            UseShellExecute = true
        };

        try
        {
            process = Process.Start(startInfo);
            if (process != null)
            {
                assign(process);
                return;
            }

            Log.Error(
                """
                Failed to launch diff tool.
                {Exe} {Arguments}
                """,
                exe, arguments);
        }
        catch (Exception exception)
        {
            Log.Error(
                exception,
                """
                Failed to launch diff tool.
                {Exe} {Arguments}
                """,
                exe,
                arguments);
        }
    }
}