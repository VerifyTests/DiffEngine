using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Windows.Forms;

static class IssueLauncher
{
    static ConcurrentBag<string> recorded = new ConcurrentBag<string>();
    static string defaultBody;

    static IssueLauncher()
    {
        defaultBody = WebUtility.UrlEncode($@" * DiffEngineTray Version: {VersionReader.VersionString}
 * Windows Version: {Environment.OSVersion.VersionString}");
    }

    public static void Launch()
    {
        LaunchUrl($"https://github.com/VerifyTests/DiffEngine/issues/new?title=TODO&body={defaultBody}");
    }

    static void LaunchUrl(string url)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = true,
            FileName = url
        };
        using var process = Process.Start(startInfo);
    }

    public static void LaunchForException(string message, Exception exception)
    {
        if (recorded.Contains(message))
        {
            return;
        }
        recorded.Add(message);

        var result = MessageBox.Show(
            $@"An error occurred: {message}

Logged to: {Logging.Directory}

{exception.GetType().Name}: {exception.Message}

Open an issue on GitHub?",
            "DiffEngineTray Error",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Error);
        if (result != DialogResult.Yes)
        {
            return;
        }

        var extraBody = WebUtility.UrlEncode($@"
 * Action: {message}
 * Exception:
```
{exception}
```");
        var url = $"https://github.com/VerifyTests/DiffEngine/issues/new?title={message}&body={defaultBody}{extraBody}";
        LaunchUrl(url);
    }
}