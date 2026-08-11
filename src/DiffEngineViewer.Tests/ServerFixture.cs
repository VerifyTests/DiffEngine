/// <summary>
/// A real <see cref="ViewerServer"/> on an ephemeral port, driven through a real
/// <see cref="ViewerClient"/>, so the tests exercise the actual socket rather than a stand in.
/// Binding port 0 keeps a live viewer on the machine out of the way.
/// </summary>
sealed class ServerFixture : IDisposable
{
    readonly CancelSource cancel = new();
    readonly Task listening;

    public ServerFixture(ViewerMode mode = ViewerMode.Inline)
    {
        Host = new(SessionState.Start(mode, Fixtures.Columns, Fixtures.Rows));
        if (!ViewerServer.TryBind(0, out var server))
        {
            throw new("Could not bind an ephemeral port.");
        }

        Server = server;
        var actions = new ViewerActions(
            patch =>
            {
                Applied.Add(patch);
                return InlineApplyResult.Applied;
            },
            (_, _) => { },
            _ => { });
        var handler = new MessageHandler(Host, actions, Windows.Add);
        listening = server.Listen(handler.Handle, cancel.Token);
    }

    public SessionHost Host { get; }
    public ViewerServer Server { get; }
    public List<InlinePatch> Applied { get; } = [];
    public List<WindowCommand> Windows { get; } = [];

    public ViewerResponse Send(ViewerMessage message)
    {
        if (!ViewerClient.TrySend(message, out var response, Server.Port))
        {
            throw new($"No response for {message.Verb}.");
        }

        return response;
    }

    public ViewerResponse SendRaw(string payload)
    {
        using var client = new System.Net.Sockets.TcpClient();
        client.Connect(System.Net.IPAddress.Loopback, Server.Port);
        using var stream = client.GetStream();
        var bytes = Encoding.UTF8.GetBytes(payload);
        stream.Write(bytes, 0, bytes.Length);
        client.Client.Shutdown(System.Net.Sockets.SocketShutdown.Send);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        if (!ViewerResponse.TryParse(reader.ReadToEnd(), out var response))
        {
            throw new("Unreadable response.");
        }

        return response;
    }

    public void Dispose()
    {
        cancel.Cancel();
        Server.Dispose();
        try
        {
            listening.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Cancellation unwinds through the listener; nothing to report.
        }

        cancel.Dispose();
    }
}
