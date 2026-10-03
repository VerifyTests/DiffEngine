static class LinkLauncher
{
    /// <summary>
    /// Logged when it cannot be opened, and nothing more: no browser registered, or one that will
    /// not start, is nothing the tray can do anything about. Thrown, it went onto the UI thread
    /// from a click in the options form, and out of the handler that reports an error, in place
    /// of the error being reported.
    /// </summary>
    public static void LaunchUrl(string url)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = true,
            FileName = url
        };
        try
        {
            using var process = Process.Start(startInfo);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Failed to open {Url}", url);
        }
    }
}
