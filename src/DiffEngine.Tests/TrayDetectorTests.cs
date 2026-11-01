/// <summary>
/// Off Windows a tray is a listener on its piper port, since a named mutex there is one session's
/// and one runtime's. Each test asks about a port it holds itself, so nothing here depends on a
/// tray running on the machine.
/// </summary>
public class TrayDetectorTests
{
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Linux | TUnit.Core.Enums.OS.MacOs)]
    public Task AListenerOnThePiperPortIsATray() =>
        HeldPort.AfterListening(
            IPAddress.Loopback,
            async _ => await Assert.That(TrayDetector.IsRunning(_)).IsTrue(),
            async _ => await Assert.That(TrayDetector.IsRunning(_)).IsFalse());

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
        bool running;
        bool held;
        try
        {
            using (ListenerTable.Answer(port, () => throw new("The table could not be read.")))
            {
                running = TrayDetector.IsRunning(port);
                held = ListenerTable.IsHeld(port);
            }
        }
        finally
        {
            listener.Stop();
        }

        await Assert.That(running).IsFalse();
        // Where a connect is behind the answer, it is still left to decide
        await Assert.That(held).IsTrue();
    }
}
