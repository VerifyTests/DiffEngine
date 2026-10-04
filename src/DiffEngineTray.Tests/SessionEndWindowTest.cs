/// <summary>
/// The two messages a logoff or a shutdown sends every top level window, sent to the one the tray
/// keeps for hearing them.
/// <para>
/// Sent from the thread that made the window, which calls its procedure directly, so no message
/// loop is needed and nothing here awaits between making the window and destroying it.
/// </para>
/// </summary>
public class SessionEndWindowTest
{
    [Test]
    public async Task TheSessionEndingIsHeard()
    {
        var heard = 0;
        IntPtr query;
        using (var window = new SessionEndWindow(() => heard++))
        {
            query = SendMessage(window.Handle, queryEndSession, IntPtr.Zero, logoff);
            SendMessage(window.Handle, endSession, new(1), logoff);
        }

        // Agreed to. Refusing would hold up the logoff, and there is nothing here worth that
        await Assert.That(query).IsEqualTo(new(1));
        await Assert.That(heard).IsEqualTo(1);
    }

    /// <summary>
    /// Another application refused the query, so everything that agreed is told the session goes
    /// on. A tray that staged its queue for that would have it both on disk and in memory.
    /// </summary>
    [Test]
    public async Task ASessionThatDidNotEndAfterAllIsNotHeard()
    {
        var heard = 0;
        using (var window = new SessionEndWindow(() => heard++))
        {
            SendMessage(window.Handle, queryEndSession, IntPtr.Zero, logoff);
            SendMessage(window.Handle, endSession, IntPtr.Zero, logoff);
        }

        await Assert.That(heard).IsEqualTo(0);
    }

    /// <summary>
    /// Wired the way Program wires it, over a queue this process holds. The session ending never
    /// reaches the disposal that stages a queue on a clean exit: the message loop does not return
    /// for it, and the process may be ended once the message is answered. So what was pending has
    /// to be on disk by then, and it was not - the queue went with the process.
    /// </summary>
    [Test]
    public async Task AnOwningTrayStagesItsQueueAsTheSessionEnds()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tray-session-end-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var host = OwnedInlineHost.TryOwn(_ => { }, new FakeLauncher(), 0) ??
                   throw new("Could not bind an ephemeral port.");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "Sample.csproj"), "<Project />");
            var source = Path.Combine(directory, "SampleTests.cs");
            await File.WriteAllTextAsync(source, "// sample");
            host.Start();
            await Assert.That(Send(host, source).Ok).IsTrue();

            EndSession(host);

            // Read before the host is disposed, which would stage it too
            var staging = Path.Combine(directory, "obj", InlineStaging.DirectoryName);
            var patchFile = Directory.GetFiles(staging).Single(_ => _.EndsWith(".inlinepatch"));
            await Assert.That(InlinePatchFile.TryRead(patchFile, out var read)).IsTrue();
            await Assert.That(read!.SourceFile).IsEqualTo(source);
            await Assert.That(read.NewContent).IsEqualTo("new");
        }
        finally
        {
            await host.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The version marker says a tray is running, and a clean exit removes it. A logoff reached
    /// none of that, so the marker outlived the tray: only an owning tray was listening for the
    /// session ending at all, and what it did then was stage its queue and nothing else.
    /// <para>
    /// The removal is handed in, as Program hands in the real one, so nothing here touches the
    /// marker of a tray running on this machine.
    /// </para>
    /// </summary>
    [Test]
    public async Task ATrayThatDoesNotOwnTheQueueRemovesItsMarkerAsTheSessionEnds()
    {
        var removed = 0;
        using (var window = new SessionEndWindow(Program.SessionEnding(null, () => removed++)))
        {
            SendMessage(window.Handle, queryEndSession, IntPtr.Zero, logoff);
            SendMessage(window.Handle, endSession, new(1), logoff);
        }

        await Assert.That(removed).IsEqualTo(1);
    }

    [Test]
    public async Task AnOwningTrayStagesItsQueueAndThenRemovesItsMarker()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tray-session-end-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var host = OwnedInlineHost.TryOwn(_ => { }, new FakeLauncher(), 0) ??
                   throw new("Could not bind an ephemeral port.");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "Sample.csproj"), "<Project />");
            var source = Path.Combine(directory, "SampleTests.cs");
            await File.WriteAllTextAsync(source, "// sample");
            host.Start();
            await Assert.That(Send(host, source).Ok).IsTrue();
            var staging = Path.Combine(directory, "obj", InlineStaging.DirectoryName);
            bool? stagedWhenRemoved = null;

            using (var window = new SessionEndWindow(
                       Program.SessionEnding(
                           host,
                           () => stagedWhenRemoved = Directory.Exists(staging) &&
                                                     Directory.GetFiles(staging).Any(_ => _.EndsWith(".inlinepatch")))))
            {
                SendMessage(window.Handle, queryEndSession, IntPtr.Zero, logoff);
                SendMessage(window.Handle, endSession, new(1), logoff);
            }

            await Assert.That(stagedWhenRemoved).IsTrue();
        }
        finally
        {
            await host.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    static void EndSession(OwnedInlineHost host)
    {
        using var window = new SessionEndWindow(host.SessionEnding);
        SendMessage(window.Handle, queryEndSession, IntPtr.Zero, logoff);
        SendMessage(window.Handle, endSession, new(1), logoff);
    }

    static ViewerResponse Send(OwnedInlineHost host, string source)
    {
        var message = new ViewerMessage(
            ViewerVerb.Inline,
            Body: InlinePatchFile.Build(
                new(source, 42, "\"old\"", "new")
                {
                    Framework = "net10.0",
                    TestName = null
                }));
        if (!ViewerClient.TrySend(message, out var response, host.Port))
        {
            throw new("The owner did not answer.");
        }

        return response;
    }

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    const int queryEndSession = 0x0011;
    const int endSession = 0x0016;
    static readonly IntPtr logoff = new(unchecked((int) 0x80000000));
}
