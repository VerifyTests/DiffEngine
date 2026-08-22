// DiffEngineTray is the obsolete public shim, but its IsRunning is still where the tray check lives.
#pragma warning disable CS0618

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        FileExtensions.AddTextFileConvention(_ => _.EndsWith(".txtConvention".AsSpan()));
        Logging.Enable();
        DiffRunner.Disabled = false;
        DetachFromPendingFileSurfaces();
    }

    /// <summary>
    /// Launching sends a real pending move to whatever owns the queue on this machine. On a
    /// developer box that is the tray, started at login, and an accept or discard from it kills the
    /// diff tool process DiffRunnerTests is asserting on. Being inconclusive when a tray is running
    /// would mean those tests never run locally, so cut both routes instead: no tray, and a viewer
    /// port nothing is listening on.
    /// <para>
    /// A failed move send is the end of the road in <c>PendingFiles.AddMove</c>, and only a delete
    /// launches a viewer, which nothing here adds. So the moves these tests produce go nowhere and
    /// no process outside the test can see them.
    /// </para>
    /// <para>
    /// The cost is that the real piper send is not covered from here. That belongs with a tray
    /// under test control, which is what DiffEngineTray.Tests/DiffRunnerCanKillTest does.
    /// </para>
    /// </summary>
    static void DetachFromPendingFileSurfaces()
    {
        DiffEngineTray.IsRunning = false;

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint) listener.LocalEndpoint).Port;
        listener.Stop();
        Environment.SetEnvironmentVariable(ViewerClient.PortVariable, port.ToString());
    }
}
