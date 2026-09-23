/// <summary>
/// The gate that keeps a parallel run from starting a viewer per failing snapshot.
/// <para>
/// The ownership probe is supplied rather than the real one, so what is asserted is the gate's own
/// decision making. Arranging the real thing means twenty concurrent connects to a port nothing is
/// listening on, and then reading the answer back out of the operating system's timing; the one
/// test below that does use it is the one where the answer is not in doubt.
/// </para>
/// </summary>
[NotInParallel]
public class ViewerLaunchGateTests
{
    /// <summary>
    /// The whole point: twenty callers, one viewer. The nineteen behind the first find the queue
    /// owned and hand their work over instead of starting anything.
    /// </summary>
    [Test]
    public async Task ManyCallersAtOnceLaunchOnce()
    {
        var viewer = new FakeViewer();

        var outcomes = await Task.WhenAll(
            Enumerable.Range(0, 20)
                .Select(_ => Task.Run(() => ViewerLaunchGate.Launch(
                    retry: () => true,
                    launch: viewer.Start,
                    isOwned: viewer.IsUp,
                    canLaunch: () => true))));

        await Assert.That(viewer.Starts).IsEqualTo(1);
        await Assert.That(outcomes.Count(_ => _ == ViewerLaunchOutcome.Launched)).IsEqualTo(1);
        await Assert.That(outcomes.Count(_ => _ == ViewerLaunchOutcome.Taken)).IsEqualTo(19);
    }

    /// <summary>
    /// A viewer takes most of a second to bind, so the gate has to be held across that wait. A
    /// gate let go the moment the process started would let the next caller in while the port was
    /// still free, and start another.
    /// </summary>
    [Test]
    public async Task TheGateIsHeldUntilTheLaunchedViewerAnswers()
    {
        var viewer = new FakeViewer {BindDelay = TimeSpan.FromMilliseconds(300)};

        var outcomes = await Task.WhenAll(
            Enumerable.Range(0, 10)
                .Select(_ => Task.Run(() => ViewerLaunchGate.Launch(
                    retry: () => true,
                    launch: viewer.Start,
                    isOwned: viewer.IsUp,
                    canLaunch: () => true))));

        await Assert.That(viewer.Starts).IsEqualTo(1);
        await Assert.That(outcomes.Count(_ => _ == ViewerLaunchOutcome.Taken)).IsEqualTo(9);
    }

    /// <summary>
    /// One that never answers costs the caller holding the gate the wait, and then the next caller
    /// is free to try again rather than the queue being stuck behind a viewer that is not there.
    /// </summary>
    [Test]
    public async Task AViewerThatNeverAnswersDoesNotHoldTheGateForever()
    {
        var previous = ViewerLaunchGate.BindWait;
        ViewerLaunchGate.BindWait = TimeSpan.FromMilliseconds(200);
        try
        {
            var starts = 0;

            var outcomes = await Task.WhenAll(
                Enumerable.Range(0, 3)
                    .Select(_ => Task.Run(() => ViewerLaunchGate.Launch(
                        retry: () => true,
                        launch: () =>
                        {
                            Interlocked.Increment(ref starts);
                            return true;
                        },
                        isOwned: () => false,
                        canLaunch: () => true))));

            await Assert.That(starts).IsEqualTo(3);
            await Assert.That(outcomes.All(_ => _ == ViewerLaunchOutcome.Launched)).IsTrue();
        }
        finally
        {
            ViewerLaunchGate.BindWait = previous;
        }
    }

    [Test]
    public async Task ALaunchThatCouldNotStartIsReportedRatherThanWaitedOn()
    {
        var previous = ViewerLaunchGate.BindWait;
        // Long enough that waiting on it would show in this test's duration.
        ViewerLaunchGate.BindWait = TimeSpan.FromSeconds(30);
        try
        {
            var outcome = ViewerLaunchGate.Launch(
                retry: () => true,
                launch: () => false,
                isOwned: () => false,
                canLaunch: () => true);

            await Assert.That(outcome).IsEqualTo(ViewerLaunchOutcome.Failed);
        }
        finally
        {
            ViewerLaunchGate.BindWait = previous;
        }
    }

    /// <summary>
    /// An owner that is there and refuses the payload is not answered by launching another, which
    /// would bind nothing and be refused in its turn.
    /// </summary>
    [Test]
    public async Task ARefusingOwnerIsNotLaunchedOver()
    {
        var launches = 0;

        var outcome = await ViewerLaunchGate.LaunchAsync(
            retry: () => Task.FromResult(false),
            launch: () =>
            {
                launches++;
                return Task.FromResult(true);
            },
            Cancel.None,
            isOwned: () => true);

        await Assert.That(outcome).IsEqualTo(ViewerLaunchOutcome.Failed);
        await Assert.That(launches).IsEqualTo(0);
    }

    /// <summary>
    /// MaxInstancesToLaunch(0) means no window opens, and the viewer is a window. It used to be
    /// exempt on the grounds that it queues rather than opening one per pair, which is true of the
    /// second pair and every one after, and not of the first: that one starts a process.
    /// </summary>
    [Test]
    public async Task NoSlotMeansNoViewerIsStarted()
    {
        var viewer = new FakeViewer();

        var outcome = ViewerLaunchGate.Launch(
            retry: () => true,
            launch: viewer.Start,
            isOwned: () => false,
            canLaunch: () => false);

        await Assert.That(outcome).IsEqualTo(ViewerLaunchOutcome.Capped);
        await Assert.That(viewer.Starts).IsEqualTo(0);
    }

    /// <inheritdoc cref="NoSlotMeansNoViewerIsStarted" />
    [Test]
    public async Task NoSlotMeansNoViewerIsStartedAsync()
    {
        var viewer = new FakeViewer();

        var outcome = await ViewerLaunchGate.LaunchAsync(
            retry: () => Task.FromResult(true),
            launch: () => Task.FromResult(viewer.Start()),
            Cancel.None,
            isOwned: () => false,
            canLaunch: () => false);

        await Assert.That(outcome).IsEqualTo(ViewerLaunchOutcome.Capped);
        await Assert.That(viewer.Starts).IsEqualTo(0);
    }

    /// <summary>
    /// What AddInlineAsync tells its caller about each outcome. Capped is the one that matters:
    /// nothing was started and nothing took the patch, and reporting that as queued meant the
    /// caller staged nothing either, so the snapshot was pending nowhere.
    /// </summary>
    [Test]
    public async Task OnlyALaunchOrAHandoverIsQueued()
    {
        await Assert.That(DiffRunner.InlineResultFor(ViewerLaunchOutcome.Launched)).IsEqualTo(InlineResult.Queued);
        await Assert.That(DiffRunner.InlineResultFor(ViewerLaunchOutcome.Taken)).IsEqualTo(InlineResult.Queued);
        await Assert.That(DiffRunner.InlineResultFor(ViewerLaunchOutcome.Capped)).IsEqualTo(InlineResult.NoViewerFound);
        await Assert.That(DiffRunner.InlineResultFor(ViewerLaunchOutcome.Failed)).IsEqualTo(InlineResult.NoViewerFound);
    }

    /// <summary>
    /// A slot is spent on a window, not on a pair. So the cap is asked only once the ownership
    /// probe has said there is no window - otherwise the nineteen callers that find the one their
    /// sibling started would each be charged for it, and a run of twenty failing snapshots would
    /// exhaust any cap and strand its pairs.
    /// </summary>
    [Test]
    public async Task ForwardingToARunningViewerSpendsNoSlot()
    {
        var viewer = new FakeViewer();
        var asked = 0;

        var outcome = ViewerLaunchGate.Launch(
            retry: () => true,
            launch: viewer.Start,
            isOwned: () => true,
            canLaunch: () =>
            {
                asked++;
                return true;
            });

        await Assert.That(outcome).IsEqualTo(ViewerLaunchOutcome.Taken);
        await Assert.That(viewer.Starts).IsEqualTo(0);
        await Assert.That(asked).IsEqualTo(0);
    }

    /// <summary>
    /// The real cap, so the default the call sites rely on is not only ever exercised through a
    /// stand-in.
    /// </summary>
    [Test]
    public async Task TheDefaultSlotCheckReadsMaxInstance()
    {
        var viewer = new FakeViewer();
        try
        {
            DiffRunner.MaxInstancesToLaunch(0);
            MaxInstance.ResetCount();

            var outcome = ViewerLaunchGate.Launch(
                retry: () => true,
                launch: viewer.Start,
                isOwned: () => false);

            await Assert.That(outcome).IsEqualTo(ViewerLaunchOutcome.Capped);
            await Assert.That(viewer.Starts).IsEqualTo(0);
        }
        finally
        {
            MaxInstance.ResetAppDomainValue();
            MaxInstance.ResetCount();
        }
    }

    /// <summary>
    /// The real probe, against a real bound port, so the default the call sites rely on is not
    /// only ever exercised through a stand-in.
    /// </summary>
    [Test]
    public async Task TheDefaultProbeReadsTheRealPort()
    {
        var previousPort = Environment.GetEnvironmentVariable(ViewerClient.PortVariable);
        try
        {
            if (!ViewerServer.TryBind(0, out var bound))
            {
                throw new("Could not bind an ephemeral port.");
            }

            using var server = bound;
            Environment.SetEnvironmentVariable(ViewerClient.PortVariable, server.Port.ToString());
            var launches = 0;

            var outcome = ViewerLaunchGate.Launch(
                retry: () => true,
                launch: () =>
                {
                    launches++;
                    return true;
                });

            await Assert.That(outcome).IsEqualTo(ViewerLaunchOutcome.Taken);
            await Assert.That(launches).IsEqualTo(0);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ViewerClient.PortVariable, previousPort);
        }
    }

    /// <summary>
    /// Stands in for the process a launch starts: not up until it has been started, and then only
    /// after <see cref="BindDelay" />, which is the gap a real one spends between being started
    /// and answering on the port.
    /// </summary>
    sealed class FakeViewer
    {
        int starts;
        long upAt = long.MaxValue;
        readonly Stopwatch elapsed = Stopwatch.StartNew();

        public TimeSpan BindDelay { get; init; }

        public int Starts => starts;

        public bool Start()
        {
            Interlocked.Increment(ref starts);
            Interlocked.Exchange(ref upAt, (elapsed.Elapsed + BindDelay).Ticks);
            return true;
        }

        public bool IsUp() =>
            elapsed.Elapsed.Ticks >= Interlocked.Read(ref upAt);
    }

    /// <summary>
    /// DiffRunner.AddDeleteAsync's shape and then DiffRunner.AddDelete's, on the only thread a
    /// single threaded context has, which is where xUnit v2 puts a second test once the first is
    /// awaiting. The async launch holds the gate across WaitForBindAsync, whose delay resumes on
    /// the captured context; the sync one blocks that context's thread waiting for the gate.
    /// </summary>
    [Test]
    public Task SyncLaunchBehindAnAsyncDeleteOnTheSameContextFinishes() =>
        SyncBehindAsync(() => Task.FromResult(true));

    /// <summary>
    /// AddInlineAsync's shape: the launch is ViewerLauncher.LaunchAsync, whose stdin write and
    /// flush are awaited on whatever context the caller had. So ConfigureAwait(false) inside the
    /// gate alone does not help it: the gate is held until the launch task completes, and that
    /// needs the context too.
    /// </summary>
    [Test]
    public Task SyncLaunchBehindAnAsyncInlineOnTheSameContextFinishes() =>
        SyncBehindAsync(async () =>
        {
            await Task.Delay(10);
            return true;
        });

    static async Task SyncBehindAsync(Func<Task<bool>> launch)
    {
        var previous = ViewerLaunchGate.BindWait;
        ViewerLaunchGate.BindWait = TimeSpan.FromMilliseconds(300);
        var context = new SingleThreadContext();
        Task<ViewerLaunchOutcome>? asyncLaunch = null;
        ViewerLaunchOutcome? syncOutcome = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            asyncLaunch = ViewerLaunchGate.LaunchAsync(
                retry: () => Task.FromResult(true),
                launch,
                Cancel.None,
                isOwned: () => false,
                canLaunch: () => true);
            syncOutcome = ViewerLaunchGate.Launch(
                retry: () => true,
                launch: () => true,
                isOwned: () => false,
                canLaunch: () => true);
        })
        {
            IsBackground = true
        };

        bool finished;
        try
        {
            thread.Start();
            // Ten bind waits, which is ten times what the two launches need between them
            finished = thread.Join(TimeSpan.FromSeconds(3));
        }
        finally
        {
            // Run what the context queued, from a thread that is not blocked, so the static gate is
            // not left held for whatever runs in this process next
            var pump = new Thread(() => context.Pump(() => !thread.IsAlive, TimeSpan.FromSeconds(10)))
            {
                IsBackground = true
            };
            pump.Start();
            pump.Join();
            ViewerLaunchGate.BindWait = previous;
        }

        await Assert.That(finished).IsTrue();
        await Assert.That(syncOutcome).IsEqualTo(ViewerLaunchOutcome.Launched);
        await Assert.That(asyncLaunch!.IsCompleted).IsTrue();
    }

    /// <summary>
    /// One thread, and a queue of what was posted to it. What xUnit v2's MaxConcurrencySyncContext
    /// is with a single worker, and what a WinForms or WPF thread is.
    /// </summary>
    sealed class SingleThreadContext : SynchronizationContext
    {
        readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> queue = new();

        public override void Post(SendOrPostCallback d, object? state) =>
            queue.Add((d, state));

        public override void Send(SendOrPostCallback d, object? state) =>
            throw new NotSupportedException();

        public void Pump(Func<bool> done, TimeSpan timeout)
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try
            {
                var elapsed = Stopwatch.StartNew();
                while (!done() &&
                       elapsed.Elapsed < timeout)
                {
                    if (queue.TryTake(out var item, TimeSpan.FromMilliseconds(20)))
                    {
                        item.Callback(item.State);
                    }
                }
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }
    }
}
