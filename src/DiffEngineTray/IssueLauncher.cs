using System.Net;

static class IssueLauncher
{
    static ConcurrentBag<string> recorded = [];
    static string defaultBody;

    static IssueLauncher() =>
        defaultBody = WebUtility.UrlEncode(
            $"""
              * DiffEngineTray Version: {VersionReader.VersionString}
              * OS: {Environment.OSVersion.VersionString}
             """);

    public static void Launch() =>
        LinkLauncher.LaunchUrl($"https://github.com/VerifyTests/DiffEngine/issues/new?title=TODO&body={defaultBody}");

    public static void LaunchForException(string message, Exception exception)
    {
        if (CheckRecorded(message))
        {
            return;
        }

        var text = $"""
                    An error occurred: {message}

                    Logged to: {Logging.LogsDirectory}

                    {exception.GetType().Name}: {exception.Message}

                    Open an issue on GitHub?
                    """;
        if (AskIfOpenIssue(text))
        {
            return;
        }

        var extraBody = WebUtility.UrlEncode(
            $"""

              * Action: {message}
              * Exception:
             ```
             {exception}
             ```
             """);
        var url = $"https://github.com/VerifyTests/DiffEngine/issues/new?title={message}&body={defaultBody}{extraBody}";
        LinkLauncher.LaunchUrl(url);
    }

    public static void LaunchForException(string message)
    {
        if (CheckRecorded(message))
        {
            return;
        }

        var text = $"""
                    An error occurred: {message}

                    Logged to: {Logging.LogsDirectory}

                    Open an issue on GitHub?
                    """;
        if (AskIfOpenIssue(text))
        {
            return;
        }

        var extraBody = WebUtility.UrlEncode(
            $"""

              * Action: {message}
             """);
        var url = $"https://github.com/VerifyTests/DiffEngine/issues/new?title={message}&body={defaultBody}{extraBody}";
        LinkLauncher.LaunchUrl(url);
    }

    static bool AskIfOpenIssue(string text)
    {
        var result = MessageBox.Show(
            text,
            "DiffEngineTray Error",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Error);
        return result == DialogResult.No;
    }

    static bool CheckRecorded(string message)
    {
        if (recorded.Contains(message))
        {
            return true;
        }

        recorded.Add(message);
        return false;
    }
}