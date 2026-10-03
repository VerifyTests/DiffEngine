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
        LinkLauncher.LaunchUrl(BuildUrl("TODO"));

    /// <summary>
    /// The title is encoded like the body is. It is built from a message carrying a file path, and
    /// a '#' in one started a fragment - dropping the body, and everything of the title after it -
    /// while an '&amp;' started a parameter GitHub does not have, truncating the title there.
    /// </summary>
    internal static string BuildUrl(string title, string extraBody = "") =>
        $"https://github.com/VerifyTests/DiffEngine/issues/new?title={WebUtility.UrlEncode(title)}&body={defaultBody}{extraBody}";

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
        if (Declined(text))
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
        LinkLauncher.LaunchUrl(BuildUrl(message, extraBody));
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
        if (Declined(text))
        {
            return;
        }

        var extraBody = WebUtility.UrlEncode(
            $"""

              * Action: {message}
             """);
        LinkLauncher.LaunchUrl(BuildUrl(message, extraBody));
    }

    /// <summary>
    /// Puts the question, and answers whether it was declined. A field so that a test process can
    /// decline without anybody being asked: the box is modal, on the desktop of whoever is at the
    /// machine, and a test run that reached it waited there for a click.
    /// </summary>
    internal static Func<string, bool> Declined = AskIfOpenIssue;

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