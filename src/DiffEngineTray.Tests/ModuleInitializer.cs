using System.Net;
using System.Net.Sockets;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifyWinForms.Initialize();
        VerifierSettings.UseSsimForPng();
        PointAtAClosedPort();
    }

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
