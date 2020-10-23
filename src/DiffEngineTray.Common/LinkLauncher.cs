using System.Diagnostics;

static class LinkLauncher
{
    public static void LaunchUrl(string url)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = true,
            FileName = url
        };
        using var process = Process.Start(startInfo);
    }
}