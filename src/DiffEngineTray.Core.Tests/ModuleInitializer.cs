public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        DeclineToOpenAnIssue();
        MachineSettings.Ignore();
        KeepEnvironmentWritesInProcess();
        PointAtAClosedPort();
    }

    /// <summary>
    /// The tray follows an error it did not expect by offering to open an issue on GitHub, and a
    /// yes opens a browser. A test that reaches the offer is declined here, with nobody asked, and
    /// what it reached is kept for the tests that are about an error being reported.
    /// </summary>
    static void DeclineToOpenAnIssue() =>
        IssueLauncher.Declined = text =>
        {
            IssuesAsked.Enqueue(text);
            return true;
        };

    /// <summary>
    /// Every question <see cref="DeclineToOpenAnIssue"/> has declined, oldest first.
    /// </summary>
    internal static ConcurrentQueue<string> IssuesAsked { get; } = new();

    /// <summary>
    /// Tests must not write the user environment of the machine running them. The test projects
    /// run as parallel processes over the one registry key, so a capture in one and a restore in
    /// the other race, and the value that loses is gone.
    /// </summary>
    static void KeepEnvironmentWritesInProcess() =>
        EnvironmentHelper.Set = EnvironmentHelper.SetProcessOnly;

    /// <summary>
    /// The tray asks the viewer for pending snapshots. Without this a DiffEngineViewer running on
    /// the developer's machine would answer, and tests that expect nothing pending would see its
    /// queue. FakeViewer overrides this for the tests that do want a viewer.
    /// </summary>
    static void PointAtAClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint) listener.LocalEndpoint).Port;
        listener.Stop();
        Environment.SetEnvironmentVariable(ViewerClient.PortVariable, port.ToString());
    }
}
