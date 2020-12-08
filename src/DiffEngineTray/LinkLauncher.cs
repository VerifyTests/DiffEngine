using System.Diagnostics;

static class LinkLauncher
{
    public static void LaunchUrl(string url)
    {
        ProcessStartInfo startInfo = new()
        {
            UseShellExecute = true,
            FileName = url
        };
        using var process = Process.Start(startInfo);
    }
}