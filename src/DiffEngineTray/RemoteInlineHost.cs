/// <summary>
/// The queue belongs to a viewer that bound the port before this tray started, so every call is a
/// short loopback round trip and the tray is a remote control.
/// <para>
/// All of them use ViewerClient.ShortTimeout. These run from the 2 second scan timer and from the
/// menu opening, so a slow exchange must not outlast the timer period or block the UI.
/// </para>
/// <para>
/// A refused connection means the viewer has gone, which is the same as nothing pending. The queue
/// went with it, and this tray does not take ownership: it was decided at startup.
/// </para>
/// </summary>
class RemoteInlineHost : IInlineHost
{
    public string Description => $"owned by another process on port {ViewerClient.Port}";

    public IReadOnlyList<PendingSnapshot> List()
    {
        if (!Exchange(new(ViewerVerb.List), out var response) ||
            !response.Ok)
        {
            return [];
        }

        return response.Items
            .Select(_ => new PendingSnapshot(_.Key, _.Name, _.Status))
            .ToList();
    }

    public IReadOnlyList<PendingInline>? Queued() =>
        null;

    /// <summary>
    /// Applied or failed, decided by whether the entry is still there afterwards rather than by
    /// <c>ok</c>.
    /// <para>
    /// The wire carries <c>ok</c> and a message, not an apply status, and every owner keeps a
    /// failed entry pending so it can be retried — an accept that could not write the file is
    /// still an accept that was attempted. Taking <c>ok</c> at face value reported that snapshot
    /// as applied while the viewer was still showing it, and the menu offered it again on the next
    /// scan. A tray that owns the queue has never had that problem, because it reads the outcome
    /// out of its own <see cref="InlineQueue"/>, so the two arrangements disagreed about the same
    /// click.
    /// </para>
    /// <para>
    /// A stale patch still reads as applied: it is dropped rather than kept, and from here that is
    /// indistinguishable. It costs nothing, because the owner is a viewer and it is showing that
    /// message in its own footer.
    /// </para>
    /// </summary>
    public AcceptOutcome Accept(PendingSnapshot snapshot, out string? message)
    {
        if (!Send(ViewerVerb.Accept, snapshot.Key, out message))
        {
            return AcceptOutcome.Failed;
        }

        return List().Any(_ => _.Key == snapshot.Key)
            ? AcceptOutcome.Failed
            : AcceptOutcome.Applied;
    }

    public bool Discard(PendingSnapshot snapshot, out string? message) =>
        Send(ViewerVerb.Discard, snapshot.Key, out message);

    /// <summary>
    /// True only when the queue is empty afterwards, for the reason <see cref="Accept"/> gives —
    /// and matching what an owning tray reports, which is also "is anything still pending". A
    /// conflict counts as not accepted, which is right: it is what a reviewer still has to resolve.
    /// </summary>
    public bool AcceptAll(out string? message) =>
        Send(ViewerVerb.AcceptAll, null, out message) &&
        List().Count == 0;

    public void DiscardAll() =>
        Send(ViewerVerb.DiscardAll, null, out _);

    public void Focus(PendingSnapshot snapshot) =>
        Send(ViewerVerb.Focus, snapshot.Key, out _);

    public void Close() =>
        Send(ViewerVerb.Quit, null, out _);

    static bool Send(ViewerVerb verb, string? key, out string? message)
    {
        message = null;
        if (!Exchange(new(verb, key), out var response))
        {
            message = "The snapshot viewer is not running.";
            return false;
        }

        message = response.Message is { Length: > 0 } text ? text : null;
        return response.Ok;
    }

    static bool Exchange(ViewerMessage message, [NotNullWhen(true)] out ViewerResponse? response) =>
        ViewerClient.TrySend(message, out response, wait: ViewerClient.ShortTimeout);
}
