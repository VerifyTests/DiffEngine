/// <summary>
/// The two questions a purge asks, put by a program the desktop has, for a head with no toolkit
/// of its own to ask with: AppleScript's dialogs on macOS, and zenity's or kdialog's on Linux, as
/// <see cref="DesktopNotification" /> pops a balloon. Each waits for as long as the question is
/// left unanswered, so neither is asked on the thread the tray's menu runs on.
/// </summary>
sealed class DesktopDialog(
    string program,
    IReadOnlyList<string> folderArguments,
    Func<string, IReadOnlyList<string>> confirmArguments)
{
    const string title = "Purge verified files";
    const string prompt = "The folder to delete every *.verified.* and *.received.* file under";

    public string Program => program;
    public IReadOnlyList<string> FolderArguments => folderArguments;
    public IReadOnlyList<string> ConfirmArguments(string message) => confirmArguments(message);

    /// <summary>
    /// Arguments go to the script as argv rather than into its text, so a path or a message with a
    /// quote in it needs no escaping.
    /// </summary>
    public static DesktopDialog AppleScript { get; } = new(
        "osascript",
        [
            "-e", "on run argv",
            "-e", "return POSIX path of (choose folder with prompt (item 1 of argv))",
            "-e", "end run",
            prompt
        ],
        message =>
        [
            "-e", "on run argv",
            "-e", """display dialog (item 1 of argv) with title (item 2 of argv) buttons {"Cancel", "Delete"} default button "Delete" cancel button "Cancel" with icon caution""",
            "-e", "end run",
            message,
            title
        ]);

    /// <summary>
    /// zenity reads a question's text as Pango markup, in which a path with an ampersand or an
    /// angle bracket in it is an error, so markup is turned off.
    /// </summary>
    public static DesktopDialog Zenity { get; } = new(
        "zenity",
        ["--file-selection", "--directory", $"--title={prompt}"],
        message =>
        [
            "--question",
            "--no-markup",
            $"--title={title}",
            $"--text={message}",
            "--ok-label=Delete",
            "--cancel-label=Cancel"
        ]);

    public static DesktopDialog Kdialog { get; } = new(
        "kdialog",
        ["--title", prompt, "--getexistingdirectory", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)],
        message =>
        [
            "--title", title,
            "--yes-label", "Delete",
            "--no-label", "Cancel",
            "--yesno", message
        ]);

    /// <summary>
    /// The one this platform has, or null for none to ask with. osascript is part of macOS.
    /// </summary>
    public static DesktopDialog? ForPlatform()
    {
        if (OperatingSystem.IsMacOS())
        {
            return AppleScript;
        }

        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        return ForLinux(
            Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"),
            _ => OsSettingsResolver.EnvPaths.Any(directory => File.Exists(Path.Combine(directory, _))));
    }

    /// <summary>
    /// KDE's own on KDE, where zenity's would be a GTK window among Qt ones, and zenity's
    /// everywhere else, either standing in for the other where only one is installed.
    /// <c>XDG_CURRENT_DESKTOP</c> is a list, such as <c>ubuntu:GNOME</c>.
    /// </summary>
    internal static DesktopDialog? ForLinux(string? desktop, Func<string, bool> installed)
    {
        var kde = desktop?
            .Split(':')
            .Contains("KDE", StringComparer.OrdinalIgnoreCase) == true;
        DesktopDialog[] order = kde ? [Kdialog, Zenity] : [Zenity, Kdialog];
        return order.FirstOrDefault(_ => installed(_.Program));
    }

    /// <summary>
    /// The folder chosen, or null for none. All three exit with something other than 0 for a
    /// cancel.
    /// </summary>
    public string? PickFolder()
    {
        var (code, output) = Run(FolderArguments);
        if (code != 0)
        {
            return null;
        }

        return FolderFrom(output);
    }

    public bool Confirm(string message) =>
        Run(ConfirmArguments(message)).Code == 0;

    /// <summary>
    /// The path as printed, a line of its own, and from AppleScript with a separator after it.
    /// </summary>
    internal static string? FolderFrom(string output)
    {
        var path = output.TrimEnd('\r', '\n');
        if (path.Length == 0)
        {
            return null;
        }

        return Path.TrimEndingDirectorySeparator(path);
    }

    /// <summary>
    /// With no timeout, since the person is choosing. Standard error is read as well, though
    /// nothing is done with it: zenity writes GTK's warnings there, and a pipe left full would
    /// stop it before it answered.
    /// </summary>
    (int Code, string Output) Run(IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ??
                            throw new InvalidOperationException($"Could not start {program}");
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        error.Wait();
        return (process.ExitCode, output);
    }
}
