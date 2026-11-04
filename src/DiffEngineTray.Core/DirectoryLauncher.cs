static class DirectoryLauncher
{
    /// <summary>
    /// A directory, or a file, opened by whatever the desktop opens it with.
    /// </summary>
    public static void Open(string path)
    {
        var info = new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
            Verb = "open"
        };
        try
        {
            using (Process.Start(info))
            {
            }
        }
        catch (Exception exception)
        {
            ExceptionHandler.Handle($"Failed to open: {path}", exception);
        }
    }

    /// <summary>
    /// Shows a file in the platform's file manager, through PlatformHeads'
    /// <see cref="FileManager"/>, as the viewer does.
    /// <para>
    /// A failure is logged and not taken to an issue, as one in the viewer is written to the
    /// console: a file manager that would not start is nothing for DiffEngine to fix, and on
    /// Linux there need not be one.
    /// </para>
    /// </summary>
    public static void Reveal(string file)
    {
        if (!FileManager.TryReveal(file, out var error))
        {
            Log.Warning(error, "Could not open a file manager on {File}", file);
        }
    }
}
