/// <summary>
/// Points DiffEngine's bundled viewer lookup at the copy shipped beside this tray.
/// <para>
/// The tray needs one of its own. A tray that owns the queue has to be able to open a window on it,
/// and the copy inside DiffEngine.nupkg is only reachable from a project that references that
/// package: buildTransitive/DiffEngine.targets publishes its path to the consumer, and the tray is
/// not one. Without this, resolution fell through to a globally installed dotnet tool that may not
/// be there.
/// </para>
/// <para>
/// Set rather than resolved directly, because <see cref="BundledViewerDirectory.Key"/> is the same
/// seam the targets use, so DiffTools discovery and <see cref="ViewerLauncher"/> need no special
/// case for the tray.
/// </para>
/// </summary>
static class TrayViewerDirectory
{
    /// <summary>
    /// Before anything reads DiffTools, which caches its discovery on first use.
    /// </summary>
    public static void Register()
    {
        var directory = Path.Combine(AssemblyLocation.CurrentDirectory, "viewer");
        if (Directory.Exists(directory))
        {
            AppContext.SetData(BundledViewerDirectory.Key, directory);
        }
    }
}
