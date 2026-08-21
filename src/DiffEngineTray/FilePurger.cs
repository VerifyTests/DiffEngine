static class FilePurger
{
    public static void Launch()
    {
        var thread = new Thread(Run);
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    /// <summary>
    /// Nothing above this catches. An unhandled exception on a bare Thread takes the process down,
    /// and the process is the tray - so a purge that threw took every pending move, delete and, if
    /// this tray owns the queue, every pending inline snapshot with it.
    /// </summary>
    static void Run()
    {
        try
        {
            Inner();
        }
        catch (Exception exception)
        {
            ExceptionHandler.Handle("Failed to purge verified files.", exception);
        }
    }

    static void Inner()
    {
        using var dialog = new FolderBrowserDialog();
        var directoryResult = dialog.ShowDialog();

        var path = dialog.SelectedPath;
        if (directoryResult != DialogResult.OK ||
            string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var files = Find(path);

        if (files.Length == 0)
        {
            MessageBox.Show($"No *.verified.* or  *.received.* files found in {path}");
            return;
        }

        if (Confirm(files))
        {
            DeleteFiles(files);
        }
    }

    /// <summary>
    /// SearchOption.AllDirectories is the compatibility enumeration: it does not ignore what it
    /// cannot read, and it follows reparse points. The dialog lets the user pick a profile folder
    /// or a drive root, either of which holds a deny-ACL junction - Application Data,
    /// $Recycle.Bin - so the scan reliably threw UnauthorizedAccessException before it had looked
    /// at a single file.
    /// </summary>
    static readonly EnumerationOptions enumeration = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    internal static string[] Find(string path) =>
        Directory.GetFiles(path, "*.verified.*", enumeration)
            .Concat(Directory.GetFiles(path, "*.received.*", enumeration))
            .ToArray();

    static bool Confirm(string[] files)
    {
        var result = AskQuestion(
            $"""
             Files found: {files.Length}.
             Delete files?
             """,
            "Confirm",
            MessageBoxButtons.OKCancel);
        return result == DialogResult.OK;
    }

    static void DeleteFiles(string[] files)
    {
        for (var index = 0; index < files.Length; index++)
        {
            var file = files[index];
            var result = TryDeleteWithLockKill(file);
            if (result.Deleted)
            {
                continue;
            }

            var failedResult = AskQuestion(
                $"""
                 Could not delete file: {file}
                 Exception: {result.Exception!.Message}
                 """,
                "Delete failed",
                MessageBoxButtons.AbortRetryIgnore);

            if (failedResult == DialogResult.Abort)
            {
                return;
            }

            if (failedResult == DialogResult.Retry)
            {
                index--;
            }
        }
    }

    internal static DeleteResult TryDeleteWithLockKill(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }

            return new(true, null);
        }
        catch (Exception exception)
        {
            if (FileLockKiller.KillLockingProcesses(file))
            {
                try
                {
                    File.Delete(file);
                    return new(true, null);
                }
                catch (Exception retryException)
                {
                    return new(false, retryException);
                }
            }

            return new(false, exception);
        }
    }

    internal record DeleteResult(bool Deleted, Exception? Exception);

    static DialogResult AskQuestion(string text, string caption, MessageBoxButtons buttons) =>
        MessageBox.Show(
            text,
            caption,
            buttons,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1,
            MessageBoxOptions.DefaultDesktopOnly);
}