/// <summary>
/// The tray's Update, on every head: PlatformHeads' <see cref="ToolUpdate" /> for the DiffEngineTray
/// this tray was installed as. It waits for this tray to exit, runs <c>dotnet tool update</c> by the
/// tool path the tray is in, and starts it again. It was a Windows PowerShell line, on Windows only,
/// which did not wait for the tray to go and updated the global install whichever tray asked.
/// </summary>
static class TrayUpdate
{
    const string package = "DiffEngineTray";

    /// <summary>
    /// Beside the settings rather than the logs, which are in the store the update replaces.
    /// </summary>
    public static string OutcomePath { get; } = Path.Combine(Path.GetDirectoryName(SettingsHelper.FilePath)!, "update-outcome.log");

    /// <summary>
    /// For this tray, or null when it is not an installed tool, as a build's output is not, which
    /// has nothing to update.
    /// </summary>
    public static ToolUpdate? Find() =>
        Find(AssemblyLocation.CurrentDirectory);

    internal static ToolUpdate? Find(string directory)
    {
        var shim = ToolUpdate.ShimFor(directory, package);
        if (shim is null)
        {
            return null;
        }

        return new(package, shim, OutcomePath);
    }

    /// <summary>
    /// The menu's Update, for a head with no options window to hold the button, or null where
    /// there is nothing to update.
    /// </summary>
    public static Action? MenuAction(Action exit)
    {
        var update = Find();
        if (update is null)
        {
            return null;
        }

        return () => Start(update, exit);
    }

    /// <summary>
    /// Starts the update, then exits the tray, which the update waits for. A shell that would not
    /// start leaves the tray running, since nothing would start it again.
    /// </summary>
    public static void Start(ToolUpdate update, Action exit)
    {
        try
        {
            Log.Information("Updating {Shim} by {Dotnet}", update.Shim, update.Dotnet);
            update.Start();
        }
        catch (Exception exception)
        {
            ExceptionHandler.Handle("Failed to start the update", exception);
            return;
        }

        exit();
    }

    /// <summary>
    /// What to say about the update that started this tray, or null where none did, which is every
    /// start but the one after an update. Said once, since reading it removes it, and only by a
    /// tray that got past the single instance lock, so one started beside it and leaving at once
    /// does not take it away. The whole of what the update said goes to the log.
    /// </summary>
    public static TrayNotification? Report() =>
        Report(OutcomePath);

    internal static TrayNotification? Report(string path)
    {
        ToolUpdateOutcome? outcome;
        try
        {
            outcome = ToolUpdateOutcome.Take(path);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warning(exception, "Could not read how the update went, at {Path}", path);
            return null;
        }

        if (outcome is null)
        {
            return null;
        }

        if (outcome.Succeeded)
        {
            Log.Information("Updated. dotnet tool update said:\n{Output}", outcome.Output);
            // The version rather than what the tool said: this is the tray the update started, so
            // it is the installed one, whether the update moved it or found it already current
            return new("DiffEngineTray updated", $"Now running {VersionReader.VersionString}.", Kind: TrayNotificationKind.Info);
        }

        Log.Warning("The update failed, so {Version} is still the one installed. dotnet tool update said:\n{Output}", VersionReader.VersionString, outcome.Output);
        return new("DiffEngineTray did not update", outcome.Reason);
    }
}
