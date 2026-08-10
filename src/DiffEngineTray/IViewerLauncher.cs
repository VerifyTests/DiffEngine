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
        viewer = ViewerLauncher.LaunchAttached();
        return viewer is not null;
    }
}
