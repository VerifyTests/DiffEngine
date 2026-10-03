/// <summary>
/// Holds the viewer port and never answers: a queue owner that is cold starting, or wedged.
/// <see cref="FakeViewer"/> is the owner that does answer.
/// <para>
/// Listening is all it takes. The system completes a connection to a listening port on its own, so
/// a client connects, sends, and waits out its timeout for a reply nothing here will ever write,
/// which is what a process that is not getting round to its accept loop looks like from outside.
/// </para>
/// <para>
/// Points DiffEngine_ViewerPort at itself, as <see cref="FakeViewer"/> does.
/// </para>
/// </summary>
sealed class SilentOwner : IDisposable
{
    readonly TcpListener listener = new(IPAddress.Loopback, 0);
    readonly string? previousPort;

    public SilentOwner()
    {
        listener.Start();
        var port = ((IPEndPoint) listener.LocalEndpoint).Port;
        previousPort = Environment.GetEnvironmentVariable(ViewerClient.PortVariable);
        Environment.SetEnvironmentVariable(ViewerClient.PortVariable, port.ToString());
    }

    public void Dispose()
    {
        listener.Stop();
        Environment.SetEnvironmentVariable(ViewerClient.PortVariable, previousPort);
    }
}
