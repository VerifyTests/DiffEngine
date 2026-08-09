/// <summary>
/// Stands in for a running DiffEngineViewer, so the tray's half of the protocol is exercised over
/// a real socket rather than a mocked proxy.
/// <para>
/// Binds an ephemeral port and points DiffEngine_ViewerPort at it, which keeps a viewer that
/// happens to be running on this machine out of the way.
/// </para>
/// </summary>
sealed class FakeViewer : IDisposable
{
    readonly TcpListener listener;
    readonly CancelSource cancel = new();
    readonly string? previousPort;
    readonly Task listening;

    public FakeViewer(params string[] names)
    {
        foreach (var name in names)
        {
            Queue.Add(new($"c:\\repo\\{name.ToLowerInvariant()}|1", name, null));
        }

        listener = new(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint) listener.LocalEndpoint).Port;
        previousPort = Environment.GetEnvironmentVariable(ViewerClient.PortVariable);
        Environment.SetEnvironmentVariable(ViewerClient.PortVariable, port.ToString());
        listening = Task.Run(Listen);
    }

    public List<PendingSnapshot> Queue { get; } = [];
    public List<string> Verbs { get; } = [];

    /// <summary>
    /// When false, every acting verb reports failure, which is how the tray's warning path is
    /// reached without arranging a locked file.
    /// </summary>
    public bool Succeed { get; set; } = true;

    public string? FailureMessage { get; set; } = "the file is locked";

    async Task Listen()
    {
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(cancel.Token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var request = await reader.ReadToEndAsync(cancel.Token);
                var bytes = Encoding.UTF8.GetBytes(Respond(request));
                await stream.WriteAsync(bytes, cancel.Token);
                await stream.FlushAsync(cancel.Token);
            }
            catch (Exception exception)
                when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }
        }
    }

    string Respond(string request)
    {
        var verb = Read(request, "verb");
        var key = Decode(Read(request, "key"));
        Verbs.Add(key == null ? verb : $"{verb}:{key}");

        var builder = new StringBuilder("version: 1\n");
        if (verb is "accept" or "discard" or "acceptall" or "discardall" &&
            !Succeed)
        {
            builder.Append("status: error\n");
            Append(builder, "message", FailureMessage);
            return builder.ToString();
        }

        switch (verb)
        {
            case "accept":
            case "discard":
                Queue.RemoveAll(_ => _.Key == key);
                break;
            case "acceptall":
            case "discardall":
                Queue.Clear();
                break;
        }

        builder.Append("status: ok\n");
        if (verb == "list")
        {
            foreach (var item in Queue)
            {
                var status = item.Status == null ? "" : Encode(item.Status);
                builder.Append($"item: {Encode(item.Key)}|{Encode(item.Name)}|{status}\n");
            }
        }

        return builder.ToString();
    }

    static void Append(StringBuilder builder, string name, string? value)
    {
        if (value != null)
        {
            builder.Append($"{name}: {Encode(value)}\n");
        }
    }

    static string Read(string request, string name)
    {
        foreach (var line in request.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith($"{name}: ", StringComparison.Ordinal))
            {
                return trimmed[(name.Length + 2)..];
            }
        }

        return "";
    }

    static string? Decode(string value) =>
        value.Length == 0 ? null : Encoding.UTF8.GetString(Convert.FromBase64String(value));

    static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    public void Dispose()
    {
        cancel.Cancel();
        listener.Stop();
        try
        {
            listening.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Cancellation unwinds through the listener; nothing to report.
        }

        cancel.Dispose();
        Environment.SetEnvironmentVariable(ViewerClient.PortVariable, previousPort);
    }
}
