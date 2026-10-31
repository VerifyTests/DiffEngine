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

        var files = VerifiedFiles.Find(path);

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
            var result = VerifiedFiles.TryDelete(file);
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

    static DialogResult AskQuestion(string text, string caption, MessageBoxButtons buttons) =>
        MessageBox.Show(
            text,
            caption,
            buttons,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1,
            MessageBoxOptions.DefaultDesktopOnly);
}
