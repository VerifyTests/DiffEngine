/// <summary>
/// Windows' table of listeners, read by a call of this library's own rather than out of what .NET
/// lists. The rows are picked apart by hand, so what is held to here is that they are picked
/// apart correctly: a listener of either family is found on its port, and a port with no
/// listener is answered no, rather than not answered.
/// <para>
/// Each test asks about a port it holds itself, so nothing here depends on what else the machine
/// or the tests beside it are listening on.
/// </para>
/// </summary>
public class ListenerTableTests
{
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Windows)]
    public async Task AListenerIsFoundOnItsPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint) listener.LocalEndpoint).Port;
        try
        {
            await Assert.That(ListenerTable.Listeners(port)).IsTrue();
            await Assert.That(ListenerTable.Enumerated(port)).IsTrue();
            await Assert.That(ListenerTable.IsHeld(port)).IsTrue();
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// The second table, whose rows are laid out another way. A viewer listens on IPv4's loopback,
    /// but the table is asked by port alone, and something else on the port is a reason to connect
    /// and find out what.
    /// </summary>
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Windows)]
    public async Task AnIPv6ListenerIsFoundOnItsPort()
    {
        if (!Socket.OSSupportsIPv6)
        {
            return;
        }

        var listener = new TcpListener(IPAddress.IPv6Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint) listener.LocalEndpoint).Port;
        try
        {
            await Assert.That(ListenerTable.Listeners(port)).IsTrue();
            await Assert.That(ListenerTable.IsHeld(port)).IsTrue();
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// A port that is bound and not listening has nobody to accept a connection, which is the
    /// answer the caller is spared two seconds by, and it takes both tables being read to their
    /// ends to give it. Bound rather than free, so that nothing else can start listening on it
    /// while the test asks.
    /// </summary>
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Windows)]
    public async Task ABoundPortThatIsNotListeningIsNotHeld()
    {
        using var holder = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            ExclusiveAddressUse = true
        };
        holder.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint) holder.LocalEndPoint!).Port;

        await Assert.That(ListenerTable.Listeners(port)).IsFalse();
        await Assert.That(ListenerTable.Enumerated(port)).IsFalse();
        await Assert.That(ListenerTable.IsHeld(port)).IsFalse();
    }

    /// <summary>
    /// More listeners than the first buffer has room for, which is the path that asks again with
    /// the size it was told. The last one opened is the one asked about, since a table cut short
    /// would be missing its end.
    /// </summary>
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Windows)]
    public async Task ATableLargerThanTheFirstBufferIsReadWhole()
    {
        var listeners = new List<TcpListener>();
        try
        {
            // 4096 bytes are 170 rows of 24
            for (var index = 0; index < 200; index++)
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                listeners.Add(listener);
            }

            foreach (var listener in listeners)
            {
                var port = ((IPEndPoint) listener.LocalEndpoint).Port;
                await Assert.That(ListenerTable.Listeners(port)).IsTrue();
            }
        }
        finally
        {
            foreach (var listener in listeners)
            {
                listener.Stop();
            }
        }
    }
}
