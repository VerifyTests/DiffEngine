static class DiffToolLauncher
{
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    public static void Launch(TrackedMove move) =>
        Launch(move.Exe!, move.Arguments!, move.CanKill, move.Process, _ => move.Process = _);

    /// <summary>
    /// The two start flags the tool itself declares, which is what <c>DiffRunner.LaunchProcess</c>
    /// launches it with. Hard coded here before, so a console subsystem tool - the bundled viewer
    /// is one - was started without CreateNoWindow and came up with a console attached, which
    /// DiffEngine's own launch of the same tool does not do.
    /// <para>
    /// Resolved by path rather than carried on the move, because the payload the tray receives has
    /// no room for them: PiperServer's format is frozen, every stable DiffEngine embeds the client
    /// that writes it, and a new field would be read as nothing by all of them.
    /// </para>
    /// <para>
    /// By name when the path misses, which for the bundled viewer it does: the path a move carries
    /// is the sending process's, and that one sits inside that project's package folder, somewhere
    /// this process has never looked. The same tool found somewhere else is still that tool, and
    /// its start flags belong to the executable rather than to where it was installed.
    /// </para>
    /// <para>
    /// An exe that resolves to neither keeps what this always did. ShellExecute is the safe end of
    /// that: a tool started without it inherits the launching process's handles, which is what
    /// <see href="https://github.com/VerifyTests/Verify/issues/1229" /> is about.
    /// </para>
    /// </summary>
    internal static (bool useShellExecute, bool createNoWindow) FlagsFor(string exe)
    {
        if (DiffTools.TryFindByPath(exe, out var tool))
        {
            return (tool.UseShellExecute, tool.CreateNoWindow);
        }

        var name = Path.GetFileName(exe);
        foreach (var candidate in DiffTools.Resolved)
        {
            if (string.Equals(Path.GetFileName(candidate.ExePath), name, StringComparison.OrdinalIgnoreCase))
            {
                return (candidate.UseShellExecute, candidate.CreateNoWindow);
            }
        }

        return (true, false);
    }

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

        var (useShellExecute, createNoWindow) = FlagsFor(exe);
        var startInfo = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = useShellExecute,
            CreateNoWindow = createNoWindow
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