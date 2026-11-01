/// <summary>
/// A port bound and not listening, for a test that needs one nobody is on. Bound rather than found
/// free and let go, so that nothing else is handed it while the test is using it: a test beside
/// this one binding port zero could otherwise be given the same port, and be told whatever this
/// test had the listener table say about it (<see cref="ListenerTable.Answer"/>), or be found
/// listening there by a test asking whether anybody is.
/// </summary>
sealed class HeldPort : IDisposable
{
    readonly Socket socket;

    public HeldPort() :
        this(IPAddress.Loopback, 0)
    {
    }

    HeldPort(IPAddress address, int port)
    {
        socket = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.Bind(new IPEndPoint(address, port));
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        Port = ((IPEndPoint) socket.LocalEndPoint!).Port;
    }

    /// <summary>
    /// A listener of the test's own on <paramref name="address"/>, asked about by
    /// <paramref name="listening"/> while it listens and by <paramref name="stopped"/> once it has
    /// stopped, with the port held again in between. The tests beside one bind port zero, and
    /// Windows hands a port straight back out: asked about while free, a port was found listened
    /// on by one of them within milliseconds. Where one had it first, this starts again on another.
    /// </summary>
    public static async Task AfterListening(IPAddress address, Func<int, Task> listening, Func<int, Task> stopped)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var listener = new TcpListener(address, 0);
            listener.Start();
            var port = ((IPEndPoint) listener.LocalEndpoint).Port;
            try
            {
                await listening(port);
            }
            finally
            {
                listener.Stop();
            }

            using var held = TryAgain(address, port);
            if (held is null)
            {
                continue;
            }

            await stopped(port);
            return;
        }

        throw new("Every port was handed to something else before it could be held again.");
    }

    static HeldPort? TryAgain(IPAddress address, int port)
    {
        try
        {
            return new(address, port);
        }
        catch (SocketException)
        {
            return null;
        }
    }

    public int Port { get; }

    public void Dispose() =>
        socket.Dispose();
}
