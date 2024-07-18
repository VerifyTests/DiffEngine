static class FilePurger
{
    public static void Launch()
    {
        var thread = new Thread(Inner);
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
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

        var files = Directory.GetFiles(path, "*.verified.*", SearchOption.AllDirectories);

        if (files.Length == 0)
        {
            MessageBox.Show($"No *.verified.* files found in {path}");
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
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (Exception exception)
            {
                var failedResult = AskQuestion(
                    $"""
                     Could not delete file: {file}
                     Exception: {exception.Message}
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