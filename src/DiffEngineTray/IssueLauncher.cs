static class IssueLauncher
{
    static readonly IssueLink link = new("https://github.com/VerifyTests/DiffEngine");

    static readonly string defaultBody =
        $"""
          * DiffEngineTray Version: {VersionReader.VersionString}
          * OS: {Environment.OSVersion.VersionString}
         """;

    public static void Launch() =>
        LinkLauncher.LaunchUrl(BuildUrl("TODO"));

    /// <summary>
    /// Marks <paramref name="message"/> as already offered, so a test that makes it happen is not
    /// left waiting on the modal box nobody is there to answer.
    /// </summary>
    internal static void AlreadyRaised(string message) =>
        link.FirstTime(message);

    /// <summary>
    /// The title and body are encoded by <see cref="IssueLink"/>, so a path's '#' or '&amp;' in
    /// the title neither drops the body nor cuts the title short. <paramref name="extraBody"/> is
    /// plain text.
    /// </summary>
    internal static string BuildUrl(string title, string extraBody = "") =>
        link.Url(title, defaultBody + extraBody);

    public static void LaunchForException(string message, Exception exception)
    {
        if (!link.FirstTime(message))
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

        var extraBody =
            $"""

              * Action: {message}
              * Exception:
             ```
             {exception}
             ```
             """;
        LinkLauncher.LaunchUrl(BuildUrl(message, extraBody));
    }

    public static void LaunchForException(string message)
    {
        if (!link.FirstTime(message))
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

        var extraBody =
            $"""

              * Action: {message}
             """;
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
}