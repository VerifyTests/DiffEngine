/// <summary>
/// Shows a file in the platform's file manager. Best effort: revealing is a convenience beside
/// the review, so a missing file manager or a deleted file degrades to nothing rather than an
/// error under the reviewer.
/// </summary>
static class RevealFile
{
    public static void Show(string path)
    {
        if (Resolve(path) is not var (target, select))
        {
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                var arguments = select ? $"/select,\"{target}\"" : $"\"{target}\"";
                Process.Start(new ProcessStartInfo("explorer.exe", arguments)
                {
                    UseShellExecute = true
                });
                return;
            }

            if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", select ? ["-R", target] : [target]);
                return;
            }

            // No cross-desktop way to select a file, so the directory is the target.
            Process.Start("xdg-open", [select ? Path.GetDirectoryName(target) ?? target : target]);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Could not open a file manager on {target}: {exception.Message}");
        }
    }

    /// <summary>
    /// What to open, and whether the file manager can be asked to select it.
    /// <para>
    /// A path that is not there cannot be selected, and revealing a move used to hand one over
    /// whenever the snapshot was new: the target of the move is where the file is going, not
    /// somewhere it has been. Explorer answers that by opening the default folder - Documents,
    /// nothing to do with the review - and <c>open -R</c> by erroring. Linux happened to work,
    /// having only ever opened the directory.
    /// </para>
    /// <para>
    /// So the directory is what is shown for a path that is not there yet, which is where the
    /// file is about to be written. Null when even that is absent, since there is nothing useful
    /// left to open.
    /// </para>
    /// </summary>
    internal static (string Target, bool Select)? Resolve(string path)
    {
        if (File.Exists(path))
        {
            return (path, true);
        }

        var directory = Path.GetDirectoryName(path);
        if (directory is {Length: > 0} &&
            Directory.Exists(directory))
        {
            return (directory, false);
        }

        return null;
    }
}
