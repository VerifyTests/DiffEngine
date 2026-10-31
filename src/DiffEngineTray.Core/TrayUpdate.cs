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
    /// start leaves the tray running, since nothing would start it again, and leaves the viewer
    /// holding the queue alone, since nothing would stop it.
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

        // While the shell waits for this tray, which it does for longer than the viewer is given
        if (OperatingSystem.IsWindows())
        {
            CloseOwningViewer(update, ViewerClient.Port);
        }

        exit();
    }

    /// <summary>
    /// How long a viewer asked to close before an update has to stage its queue and go. Its close
    /// finishes an accept-all under way and gives each of its listeners two seconds to stop.
    /// </summary>
    static readonly TimeSpan closeWait = TimeSpan.FromSeconds(15);

    /// <summary>
    /// On Windows the update stops whatever runs from the store before it updates, and a viewer
    /// started from this tray's own viewer folder runs from there. One that holds the inline queue
    /// holds it in memory and stages it only as it exits, so stopped, its snapshots were gone. So
    /// it is asked to close, which an owning viewer takes as exiting (<c>Quit</c>), and waited for.
    /// A viewer from anywhere else is left alone, since nothing stops it.
    /// </summary>
    /// <returns>Whether a viewer was asked to close.</returns>
    internal static bool CloseOwningViewer(ToolUpdate update, int port)
    {
        if (ListenerTable.Owner(port) is not { } id ||
            id == Environment.ProcessId ||
            !ProcessEx.TryGet(id, out var process))
        {
            return false;
        }

        using (process)
        {
            var image = ProcessEx.ImagePath(process);
            if (!Stops(update, image))
            {
                return false;
            }

            Log.Information("Asking the viewer that holds the queue, {Image}, to stage it and close before the update stops it", image);
            if (!ViewerClient.TrySend(new(ViewerVerb.Quit), out _, port))
            {
                Log.Warning("The viewer that holds the queue did not answer, and the update will stop it");
                return true;
            }

            if (!process.WaitForExit(closeWait))
            {
                Log.Warning("The viewer that holds the queue had not closed after {Wait}, and the update will stop it", closeWait);
            }

            return true;
        }
    }

    /// <summary>
    /// Whether the update stops a process running <paramref name="image" />: one under the store,
    /// or under a folder it was told to stop as well. As the update's script matches them, without
    /// regard to case, and by whole folders.
    /// </summary>
    internal static bool Stops(ToolUpdate update, string? image)
    {
        if (image is null)
        {
            return false;
        }

        foreach (var directory in update.AlsoStopUnder.Prepend(update.StoreDirectory))
        {
            if (image.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
