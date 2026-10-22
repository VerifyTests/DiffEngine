static class LinkLauncher
{
    /// <summary>
    /// Logged when it cannot be opened, and nothing more: see <see cref="LinkOpener"/>. Thrown, it
    /// went onto the UI thread from a click in the options form, and out of the handler that
    /// reports an error, in place of the error being reported.
    /// </summary>
    public static void LaunchUrl(string url)
    {
        if (!LinkOpener.TryOpen(url, out var error))
        {
            Log.Error(error, "Failed to open {Url}", url);
        }
    }
}
