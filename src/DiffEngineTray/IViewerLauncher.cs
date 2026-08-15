/// <summary>
/// The viewer an owning tray starts to display its queue, behind an interface so a test can watch
/// the launches without a window appearing on the screen of whoever is running them.
/// </summary>
interface IViewerLauncher
{
    /// <summary>
    /// True when a viewer started by this owner is still up. Tracked by process rather than by
    /// probing the port, because the port is this process.
    /// </summary>
    bool Running { get; }

    bool Launch();
}

sealed class ProcessViewerLauncher : IViewerLauncher
{
    Process? viewer;

    public bool Running =>
        viewer is {HasExited: false};

    public bool Launch()
    {
        // The one it replaces has exited - Launch is only reached when Running said so - but the
        // handle it was read through has not gone anywhere. The tray runs for weeks, and every
        // relaunch over that time used to leave one behind for a finaliser to get to eventually
        var previous = viewer;
        viewer = ViewerLauncher.LaunchAttached();
        previous?.Dispose();
        return viewer is not null;
    }
}
