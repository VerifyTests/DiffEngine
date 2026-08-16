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

    public bool Discard(PendingSnapshot snapshot, out string? message)
    {
        message = null;
        return true;
    }

    public bool AcceptAll(out string? message)
    {
        message = null;
        return true;
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
