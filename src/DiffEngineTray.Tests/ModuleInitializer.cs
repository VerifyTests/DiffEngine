public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        ThrowRatherThanAsk();
        DeclineToOpenAnIssue();
        MachineSettings.Ignore();
        VerifyWinForms.Initialize();
        VerifierSettings.UseSsimForPng(PngSsimThreshold);
        KeepEnvironmentWritesInProcess();
        PointAtAClosedPort();
    }

    /// <summary>
    /// An exception thrown inside a window procedure is, by default, caught by WinForms and put
    /// to whoever is at the machine in a dialog with Continue and Quit on it. A test that throws
    /// there then waits on a click, on the desktop of someone doing something else. Thrown, it
    /// fails the test that caused it.
    /// <para>
    /// For every thread rather than this one, since a test builds its controls on whichever thread
    /// it is given, and first here because the mode cannot be changed once a window exists.
    /// </para>
    /// </summary>
    static void ThrowRatherThanAsk() =>
        System.Windows.Forms.Application.SetUnhandledExceptionMode(
            System.Windows.Forms.UnhandledExceptionMode.ThrowException,
            threadScope: false);

    /// <summary>
    /// The tray follows an error it did not expect with a modal "Open an issue on GitHub?" box,
    /// and a yes opens a browser. A test that reaches one is declined here, with nobody asked, and
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
    /// Effectively "the same pixels", rather than Verify's 0.98 default. These screens are mostly
    /// flat background, so 0.98 is far looser than it sounds on them: a whole missing row of text
    /// still scores about 0.998, which the default would pass. The remaining slack is for float
    /// dust and PNG encoder differences, not for anything visible.
    /// </summary>
    const double PngSsimThreshold = 0.9999;

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
