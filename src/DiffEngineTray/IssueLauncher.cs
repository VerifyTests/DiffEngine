using System.Net;

static class IssueLauncher
{
    static ConcurrentBag<string> recorded = new();
    static string defaultBody;

    static IssueLauncher()
    {
        defaultBody = WebUtility.UrlEncode($@" * DiffEngineTray Version: {VersionReader.VersionString}
 * OS: {Environment.OSVersion.VersionString}");
    }

    public static void Launch()
    {
        LinkLauncher.LaunchUrl($"https://github.com/VerifyTests/DiffEngine/issues/new?title=TODO&body={defaultBody}");
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

Logged to: {Logging.LogsDirectory}

{exception.GetType().Name}: {exception.Message}

Open an issue on GitHub?",
            "DiffEngineTray Error",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Error);
        if (result == DialogResult.No)
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
        LinkLauncher.LaunchUrl(url);
    }
}