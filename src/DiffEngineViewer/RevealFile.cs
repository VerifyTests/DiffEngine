using System.Diagnostics;

/// <summary>
/// Shows a file in the platform's file manager. Best effort: revealing is a convenience beside
/// the review, so a missing file manager or a deleted file degrades to nothing rather than an
/// error under the reviewer.
/// </summary>
static class RevealFile
{
    public static void Show(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                {
                    UseShellExecute = true
                });
                return;
            }

            if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", ["-R", path]);
                return;
            }

            // No cross-desktop way to select a file, so the directory is the target.
            Process.Start("xdg-open", [Path.GetDirectoryName(path) ?? path]);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Could not open a file manager on {path}: {exception.Message}");
        }
    }
}
