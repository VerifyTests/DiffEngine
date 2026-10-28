static class DirectoryLauncher
{
    public static void Open(string directory)
    {
        var info = new ProcessStartInfo
        {
            FileName = directory,
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
            ExceptionHandler.Handle($"Failed to open directory: {directory}", exception);
        }
    }

    /// <summary>
    /// Shows a file in the platform's file manager, through the viewer's <see cref="RevealFile"/>,
    /// which this project links. It was <c>explorer.exe /select</c>, so on Windows only, and for a
    /// file not there yet it opened Documents, which the viewer had already stopped doing.
    /// <para>
    /// A failure is logged and not taken to an issue, as one in the viewer is written to the
    /// console: a file manager that would not start is nothing for DiffEngine to fix, and on
    /// Linux there need not be one.
    /// </para>
    /// </summary>
    public static void Reveal(string file) =>
        RevealFile.Show(file, Failed);

    static void Failed(string target, Exception exception) =>
        Log.Warning(exception, "Could not open a file manager on {Target}", target);
}
