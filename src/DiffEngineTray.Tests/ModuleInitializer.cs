public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifyWinForms.Initialize();
        VerifierSettings.UseSsimForPng(PngSsimThreshold);
        PointAtAClosedPort();
    }

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
