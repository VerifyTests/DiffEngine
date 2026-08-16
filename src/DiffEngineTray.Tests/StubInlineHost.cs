/// <summary>
/// A queue of a fixed listing and nothing else, for the tests that are about what the tray makes of
/// what it is handed rather than about how it got there.
/// <para>
/// <see cref="FakeViewer"/> is the other way in and goes over a real socket, publishing its port in
/// an environment variable that every test in the process shares - fine for the tests whose subject
/// is the wire, and a race for the ones that only need a snapshot to exist.
/// </para>
/// </summary>
class StubInlineHost(params PendingSnapshot[] snapshots) :
    IInlineHost
{
    public string Description => "stub";

    public IReadOnlyList<PendingSnapshot> List() =>
        snapshots;

    public IReadOnlyList<PendingInline>? Queued() =>
        null;

    public AcceptOutcome Accept(PendingSnapshot snapshot, out string? message)
    {
        message = null;
        return AcceptOutcome.Applied;
    }

    /// <summary>
    /// Held for as long as a test wants a discard to be in flight, standing in for the socket round
    /// trip a queue in another process makes of it.
    /// </summary>
    public ManualResetEventSlim? DiscardBlock { get; init; }

    /// <summary>
    /// Set once a discard is under way, so a test can act while one is.
    /// </summary>
    public ManualResetEventSlim DiscardStarted { get; } = new();

    public bool Discard(PendingSnapshot snapshot, out string? message)
    {
        message = null;
        DiscardStarted.Set();
        DiscardBlock?.Wait(TimeSpan.FromSeconds(10));
        return true;
    }

    /// <summary>
    /// What a bulk accept reports. False is the shape that matters: everything still pending, and
    /// the summary saying how much of it went unwritten.
    /// </summary>
    public bool AcceptAllSucceeds { get; init; } = true;

    public string? AcceptAllMessage { get; init; }

    public bool AcceptAll(out string? message)
    {
        message = AcceptAllMessage;
        return AcceptAllSucceeds;
    }

    public void DiscardAll()
    {
    }

    public void Focus(PendingSnapshot snapshot)
    {
    }

    public void Close()
    {
    }
}
