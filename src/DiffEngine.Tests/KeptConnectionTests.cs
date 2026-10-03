/// <summary>
/// The connection the telling sends share, where the owner keeps one.
/// <para>
/// A settle was a connection of its own, and the client is the side that closes first, so each
/// left a port in TIME_WAIT: one a passing inline verification, for two minutes, out of the
/// 16,000 or so Windows gives out. What is asserted here is the count of connections an owner
/// accepted, and that nothing a send used to do is lost to sharing one: an owner that predates
/// it is still sent a connection each, and an owner that goes is still found gone.
/// </para>
/// </summary>
// The connection is one for the process, as the memory beside it is
[NotInParallel]
public class KeptConnectionTests
{
    static readonly ViewerMessage settle = new(ViewerVerb.Settle, InlineKey.For("Tests.cs", 1));

    [Before(Test)]
    public void Forget() =>
        ViewerClient.ForgetUnowned();

    [After(Test)]
    public void Restore() =>
        ViewerClient.ForgetUnowned();

    /// <summary>
    /// The first is an ordinary exchange, since its reply is what says the owner keeps one. The
    /// second connection is the kept one, and everything after goes down it.
    /// </summary>
    [Test]
    public async Task SendsAfterTheFirstShareAConnection()
    {
        using var owner = new Owner();

        for (var index = 0; index < 20; index++)
        {
            await Assert.That(ViewerClient.Tell(settle, owner.Port)).IsTrue();
        }

        await Assert.That(owner.Heard.Count).IsEqualTo(20);
        await Assert.That(owner.Accepted).IsEqualTo(2);
    }

    /// <summary>
    /// What the owner said is still what the send reports, a refusal included.
    /// </summary>
    [Test]
    public async Task ARefusalComesBackDownTheKeptConnection()
    {
        using var owner = new Owner(_ => _.Key == "refuse" ? ViewerResponse.Error("no") : ViewerResponse.Success());

        await Assert.That(ViewerClient.Tell(settle, owner.Port)).IsTrue();
        await Assert.That(ViewerClient.Tell(new(ViewerVerb.Settle, "refuse"), owner.Port)).IsFalse();
        await Assert.That(ViewerClient.Tell(settle, owner.Port)).IsTrue();

        await Assert.That(owner.Accepted).IsEqualTo(2);
    }

    /// <summary>
    /// Everything a message carries crosses the kept connection as it crosses its own.
    /// </summary>
    [Test]
    public async Task AMessageArrivesWhole()
    {
        using var owner = new Owner();
        var message = new ViewerMessage(ViewerVerb.Settle, "key\nwith a break", "net10.0", "Member", "a value\n\nwith an empty line");

        ViewerClient.Tell(settle, owner.Port);
        await Assert.That(ViewerClient.Tell(message, owner.Port)).IsTrue();

        await Assert.That(owner.Heard[1]).IsEqualTo(message);
    }

    /// <summary>
    /// A parallel run's settles take turns at the one connection, and each is answered with its
    /// own answer. Several may go out as ordinary exchanges before the first of them has opened
    /// it, so the bound is on the order of the threads rather than of the sends.
    /// </summary>
    [Test]
    public async Task SendsFromManyThreadsAreEachAnswered()
    {
        using var owner = new Owner(_ => _.Key!.EndsWith("odd") ? ViewerResponse.Error("odd") : ViewerResponse.Success());
        var wrong = 0;

        // Threads of their own rather than the pool's. A send blocks its thread until it is
        // answered, and this owner answers from the pool, being in the same process: on a machine
        // of two or four cores eight blocked senders were every thread the pool had, the owner
        // could not answer, and a send timed out. A real owner is another process.
        var threads = Enumerable.Range(0, 8)
            .Select(_ => Task.Factory.StartNew(
                () =>
                {
                    for (var index = 0; index < 50; index++)
                    {
                        var odd = (_ + index) % 2 == 1;
                        var sent = ViewerClient.Tell(new(ViewerVerb.Settle, odd ? "odd" : "even"), owner.Port);
                        if (sent == odd)
                        {
                            Interlocked.Increment(ref wrong);
                        }
                    }
                },
                Cancel.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default));
        await Task.WhenAll(threads);

        await Assert.That(wrong).IsEqualTo(0);
        await Assert.That(owner.Heard.Count).IsEqualTo(400);
        await Assert.That(owner.Accepted).IsLessThan(20);
    }

    /// <summary>
    /// An owner from before any of this reads a request until its sender closes, so it cannot be
    /// kept a connection, and is not: its replies do not say it keeps one.
    /// </summary>
    [Test]
    public async Task AnOwnerThatPredatesItIsSentAConnectionEach()
    {
        using var owner = new OlderOwner();

        for (var index = 0; index < 5; index++)
        {
            await Assert.That(ViewerClient.Tell(settle, owner.Port)).IsTrue();
        }

        var requests = owner.Requests;
        await Assert.That(requests.Count).IsEqualTo(5);
        foreach (var request in requests)
        {
            await Assert.That(request).IsEqualTo(settle.Build());
        }
    }

    /// <summary>
    /// And a client from before it reads past the line that says so, as it reads past any name
    /// it does not know.
    /// </summary>
    [Test]
    public async Task AnOlderClientReadsPastTheLine()
    {
        var text = $"{ViewerResponse.Success("done").Build()}{ViewerServer.Keeps}\n";

        await Assert.That(ViewerResponse.TryParse(text, out var response)).IsTrue();
        await Assert.That(response!.Ok).IsTrue();
        await Assert.That(response.Message).IsEqualTo("done");
    }

    /// <summary>
    /// An owner that stops closes what it kept, so the next send finds the port as it is now:
    /// with nobody on it.
    /// </summary>
    [Test]
    public async Task AnOwnerThatHasGoneIsFoundGone()
    {
        int port;
        using (var owner = new Owner())
        {
            port = owner.Port;
            ViewerClient.Tell(settle, port);
            await Assert.That(ViewerClient.Tell(settle, port)).IsTrue();
        }

        await Assert.That(ViewerClient.Tell(settle, port)).IsFalse();
        await Assert.That(ViewerClient.FoundUnowned(port)).IsTrue();
    }

    /// <summary>
    /// Or with the next owner on it, which the send reaches rather than failing on the last one's
    /// connection.
    /// </summary>
    [Test]
    public async Task TheNextOwnerOfThePortIsReached()
    {
        int port;
        using (var first = new Owner())
        {
            port = first.Port;
            ViewerClient.Tell(settle, port);
            await Assert.That(ViewerClient.Tell(settle, port)).IsTrue();
        }

        using var next = new Owner(port: port);

        await Assert.That(ViewerClient.Tell(settle, port)).IsTrue();
        await Assert.That(ViewerClient.Tell(settle, port)).IsTrue();
        await Assert.That(next.Heard.Count).IsEqualTo(2);
    }

    /// <summary>
    /// A send to another port leaves the one connection for that port's owner, and the first
    /// owner is still reached afterwards.
    /// </summary>
    [Test]
    public async Task EachPortIsSentItsOwn()
    {
        using var first = new Owner();
        using var second = new Owner();

        for (var index = 0; index < 3; index++)
        {
            await Assert.That(ViewerClient.Tell(settle, first.Port)).IsTrue();
            await Assert.That(ViewerClient.Tell(settle, second.Port)).IsTrue();
        }

        await Assert.That(first.Heard.Count).IsEqualTo(3);
        await Assert.That(second.Heard.Count).IsEqualTo(3);
    }

    /// <summary>
    /// An owner that is there and too busy to answer inside the timeout. The send is given up
    /// on, as one on a connection of its own is, and is not then sent a second time to wait
    /// again; the send after it starts over and is answered.
    /// </summary>
    [Test]
    public async Task AnOwnerTooBusyToAnswerIsNotWaitedOnTwice()
    {
        using var release = new ManualResetEventSlim();
        using var owner = new Owner(_ =>
        {
            if (_.Key == "slow")
            {
                release.Wait(TimeSpan.FromSeconds(30));
            }

            return ViewerResponse.Success();
        });
        ViewerClient.Tell(settle, owner.Port);

        var watch = Stopwatch.StartNew();
        var sent = ViewerClient.Tell(new(ViewerVerb.Settle, "slow"), owner.Port);
        watch.Stop();
        release.Set();

        await Assert.That(sent).IsFalse();
        // The client's timeout is three seconds, and twice that is what sending it again cost
        await Assert.That(watch.Elapsed).IsLessThan(TimeSpan.FromSeconds(5.5));
        await Assert.That(owner.Heard.Count(_ => _.Key == "slow")).IsEqualTo(1);
        await Assert.That(ViewerClient.Tell(settle, owner.Port)).IsTrue();
    }

    static readonly ViewerMessage list = new(ViewerVerb.ListFull);

    /// <summary>
    /// A window showing an owner's queue lists five times a second, and each was a connection.
    /// They share one as the settles do, and it is not the settles' one: four connections for
    /// twenty of each, the first of each kind being the ordinary exchange whose reply says the
    /// owner keeps one.
    /// </summary>
    [Test]
    public async Task ListingsShareAConnectionOfTheirOwn()
    {
        using var owner = new Owner();

        for (var index = 0; index < 20; index++)
        {
            await Assert.That(ViewerClient.TrySend(list, out _, owner.Port)).IsTrue();
            await Assert.That(ViewerClient.Tell(settle, owner.Port)).IsTrue();
        }

        await Assert.That(owner.Heard.Count).IsEqualTo(40);
        await Assert.That(owner.Accepted).IsEqualTo(4);
    }

    /// <summary>
    /// What a listing carries is the whole queue, and it arrives down the kept connection as it
    /// does down its own, whatever is in it.
    /// </summary>
    [Test]
    public async Task AListingArrivesWhole()
    {
        var patch = "a patch\n\nwith an empty line";
        using var owner = new Owner(_ => ViewerResponse.Listing(
            [new("key\nwith a break", "name", "status", patch), new("second", "name", null, patch)]));

        await Assert.That(ViewerClient.TrySend(list, out var first, owner.Port)).IsTrue();
        await Assert.That(ViewerClient.TrySend(list, out _, owner.Port)).IsTrue();
        await Assert.That(ViewerClient.TrySend(list, out var second, owner.Port)).IsTrue();

        // The first on a connection of its own, and the one it opened for the two after it
        await Assert.That(owner.Accepted).IsEqualTo(2);
        await Assert.That(second!.Build()).IsEqualTo(first!.Build());
        await Assert.That(second.Items.Count).IsEqualTo(2);
        await Assert.That(second.Items[0].Key).IsEqualTo("key\nwith a break");
        await Assert.That(second.Items[1].Patch).IsEqualTo(patch);
    }

    /// <summary>
    /// An owner answers the requests of one connection in turn, so a listing it is slow over
    /// must not be on the connection the settles use, and is not: a settle sent while a listing
    /// is still waiting for its answer is answered. And a second listing sent then does not wait
    /// for the first either. It goes on a connection of its own, as every listing used to.
    /// <para>
    /// Nothing is timed. The listing is held by the owner until the settle and the second
    /// listing have both come back, so either of them waiting behind it would be this test
    /// hanging until the listing's own wait ran out, and the listing then failing.
    /// </para>
    /// </summary>
    [Test]
    public async Task ASlowListingHoldsUpNeitherASettleNorAnotherListing()
    {
        using var release = new ManualResetEventSlim();
        using var arrived = new ManualResetEventSlim();
        using var owner = new Owner(_ =>
        {
            if (_ is { Verb: ViewerVerb.List, Key: "slow" })
            {
                arrived.Set();
                release.Wait(TimeSpan.FromMinutes(2));
            }

            return ViewerResponse.Success();
        });
        ViewerClient.Tell(settle, owner.Port);
        ViewerClient.Tell(settle, owner.Port);
        ViewerClient.TrySend(list, out _, owner.Port);
        ViewerClient.TrySend(list, out _, owner.Port);
        await Assert.That(owner.Accepted).IsEqualTo(4);

        // A thread of its own: it blocks until the owner answers, and the owner answers from the
        // pool, being in this process
        var slow = Task.Factory.StartNew(
            () => ViewerClient.TrySend(new(ViewerVerb.List, "slow"), out _, owner.Port, TimeSpan.FromMinutes(1)),
            Cancel.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        try
        {
            await Assert.That(arrived.Wait(TimeSpan.FromMinutes(1))).IsTrue();
            // Down the kept connection, or there would be a fifth
            await Assert.That(owner.Accepted).IsEqualTo(4);

            await Assert.That(ViewerClient.Tell(settle, owner.Port)).IsTrue();
            await Assert.That(ViewerClient.TrySend(list, out _, owner.Port)).IsTrue();

            await Assert.That(slow.IsCompleted).IsFalse();
            await Assert.That(owner.Accepted).IsEqualTo(5);
        }
        finally
        {
            release.Set();
        }

        await Assert.That(await slow).IsTrue();
    }

    /// <summary>
    /// Only the listings. An accept written to a kept connection whose owner had just gone
    /// would have to be sent again without knowing whether the first was carried out, so
    /// whatever changes the queue is still a connection each.
    /// </summary>
    [Test]
    public async Task ACommandIsStillAConnectionEach()
    {
        using var owner = new Owner();
        ViewerClient.TrySend(list, out _, owner.Port);
        ViewerClient.TrySend(list, out _, owner.Port);

        for (var index = 0; index < 3; index++)
        {
            await Assert.That(ViewerClient.TrySend(new(ViewerVerb.Accept, "key"), out _, owner.Port)).IsTrue();
            await Assert.That(ViewerClient.TrySend(new(ViewerVerb.Discard, "key"), out _, owner.Port)).IsTrue();
        }

        await Assert.That(owner.Accepted).IsEqualTo(8);
    }

    /// <summary>
    /// An owner from before any of this is listed a connection each, as it is told.
    /// </summary>
    [Test]
    public async Task AnOwnerThatPredatesItIsListedAConnectionEach()
    {
        using var owner = new OlderOwner();

        for (var index = 0; index < 5; index++)
        {
            await Assert.That(ViewerClient.TrySend(list, out _, owner.Port)).IsTrue();
        }

        var requests = owner.Requests;
        await Assert.That(requests.Count).IsEqualTo(5);
        foreach (var request in requests)
        {
            await Assert.That(request).IsEqualTo(list.Build());
        }
    }

    /// <summary>
    /// A listing is how a window learns its owner has gone, so one sent down a connection the
    /// owner closed has to come back saying so, and the one after an owner has taken the port
    /// again has to reach it.
    /// </summary>
    [Test]
    public async Task AListingFindsItsOwnerGoneAndTheNextOne()
    {
        int port;
        using (var owner = new Owner())
        {
            port = owner.Port;
            ViewerClient.TrySend(list, out _, port);
            await Assert.That(ViewerClient.TrySend(list, out _, port)).IsTrue();
        }

        await Assert.That(ViewerClient.TrySend(list, out _, port)).IsFalse();

        using var next = new Owner(port: port);
        await Assert.That(ViewerClient.TrySend(list, out _, port)).IsTrue();
        await Assert.That(ViewerClient.TrySend(list, out _, port)).IsTrue();
        await Assert.That(next.Heard.Count).IsEqualTo(2);
    }

    /// <summary>
    /// The kept connection is this process's alone. On .NET Framework a socket is inheritable and
    /// a process started without ShellExecute is handed every inheritable handle, so a child a
    /// test started held the connection too, and the owner went on keeping it until that child
    /// exited, long after the test host had.
    /// <para>
    /// What is asked is whether the handle is one a child would be given. What a child does with
    /// it only shows once the host has gone without closing anything, which a test cannot do to
    /// the process it runs in: closing the connection here shuts it down, and that reaches the
    /// owner whoever else holds the handle.
    /// </para>
    /// </summary>
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Windows)]
    public async Task TheKeptConnectionIsNotOneAChildProcessIsGiven()
    {
        using var owner = new Owner();
        ViewerClient.Tell(settle, owner.Port);
        await Assert.That(ViewerClient.Tell(settle, owner.Port)).IsTrue();

        var handle = ViewerClient.KeptHandle;
        await Assert.That(handle).IsNotNull();
        await Assert.That(GetHandleInformation(handle!.Value, out var flags)).IsTrue();
        // HANDLE_FLAG_INHERIT
        await Assert.That(flags & 1).IsEqualTo(0);
    }

    [DllImport("kernel32.dll")]
    static extern bool GetHandleInformation(IntPtr handle, out int flags);

    /// <summary>
    /// A queue owner on a port of the test's choosing, recording what it was sent.
    /// </summary>
    sealed class Owner : IDisposable
    {
        readonly ViewerServer server;
        readonly CancelSource cancel = new();
        readonly Task listening;
        readonly List<ViewerMessage> heard = [];

        public Owner(Func<ViewerMessage, ViewerResponse>? answer = null, int port = 0)
        {
            if (!ViewerServer.TryBind(port, out var bound))
            {
                throw new($"Could not bind port {port}.");
            }

            server = bound;
            listening = server.Listen(
                _ =>
                {
                    lock (heard)
                    {
                        heard.Add(_);
                    }

                    return answer?.Invoke(_) ?? ViewerResponse.Success();
                },
                cancel.Token);
        }

        public int Port => server.Port;

        public int Accepted => server.Accepted;

        public IReadOnlyList<ViewerMessage> Heard
        {
            get
            {
                lock (heard)
                {
                    return heard.ToList();
                }
            }
        }

        public void Dispose()
        {
            cancel.Cancel();
            server.Dispose();
            try
            {
                listening.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // Cancellation unwinding through the listener; nothing to report
            }

            cancel.Dispose();
        }
    }

    /// <summary>
    /// The owner every released tray and viewer is: one request a connection, read until the
    /// client closes its half, answered, and closed.
    /// </summary>
    sealed class OlderOwner : IDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        readonly List<string> requests = [];

        public OlderOwner()
        {
            listener.Start();
            Port = ((IPEndPoint) listener.LocalEndpoint).Port;
            _ = Task.Run(Serve);
        }

        public int Port { get; }

        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (requests)
                {
                    return requests.ToList();
                }
            }
        }

        async Task Serve()
        {
            try
            {
                while (true)
                {
                    using var client = await listener.AcceptTcpClientAsync();
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    var request = await reader.ReadToEndAsync();
                    lock (requests)
                    {
                        requests.Add(request);
                    }

                    var reply = Encoding.UTF8.GetBytes(ViewerResponse.Success().Build());
                    await stream.WriteAsync(reply, 0, reply.Length);
                    await stream.FlushAsync();
                }
            }
            catch (Exception exception)
                when (exception is SocketException or ObjectDisposedException or InvalidOperationException or IOException)
            {
                // Stopped
            }
        }

        public void Dispose() =>
            listener.Stop();
    }
}
