/// <summary>
/// Off Windows a tray is a listener on its piper port, since a named mutex there is one session's
/// and one runtime's. Each test asks about a port it holds itself, so nothing here depends on a
/// tray running on the machine.
/// </summary>
public class TrayDetectorTests
{
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Linux | TUnit.Core.Enums.OS.MacOs)]
    public async Task AListenerOnThePiperPortIsATray()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint) listener.LocalEndpoint).Port;
        bool running;
        try
        {
            running = TrayDetector.IsRunning(port);
        }
        finally
        {
            listener.Stop();
        }

        await Assert.That(running).IsTrue();
        await Assert.That(TrayDetector.IsRunning(port)).IsFalse();
    }

    /// <summary>
    /// No tray, where the piper's send would have taken it as one and let the connect decide:
    /// a viewer told a tray is running hides its window on close, and with no tray nothing shows
    /// it again.
    /// </summary>
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Linux | TUnit.Core.Enums.OS.MacOs)]
    public async Task ATableThatCannotBeReadIsNoTray()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint) listener.LocalEndpoint).Port;
        var previous = ListenerTable.Lookup;
        // Every other port goes through untouched, since the tests beside this one are asking
        // about theirs at the same time
        ListenerTable.Lookup = _ => _ == port ? throw new("The table could not be read.") : previous(_);
        bool running;
        bool held;
        try
        {
            running = TrayDetector.IsRunning(port);
            held = ListenerTable.IsHeld(port);
        }
        finally
        {
            ListenerTable.Lookup = previous;
            listener.Stop();
        }

        await Assert.That(running).IsFalse();
        // Where a connect is behind the answer, it is still left to decide
        await Assert.That(held).IsTrue();
    }
}
