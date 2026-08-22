/// <summary>
/// The queue belongs to a viewer that bound the port before this tray started, so every call is a
/// short loopback round trip and the tray is a remote control.
/// <para>
/// The listing and the menu verbs use ViewerClient.ShortTimeout. Those run from the 2 second scan
/// timer and from the menu opening, so a slow exchange must not outlast the timer period or block
/// the UI.
/// </para>
/// <para>
/// Accepting does not, for the reason <see cref="acceptWait"/> gives.
/// </para>
/// <para>
/// A refused connection means the viewer has gone, which is the same as nothing pending. The queue
/// went with it, and this tray does not take ownership: it was decided at startup.
/// </para>
/// </summary>
class RemoteInlineHost : IInlineHost
{
    /// <summary>
    /// The one verb that can legitimately take this long: the owner applies through
    /// <see cref="InlineApplier"/>, which waits up to ten seconds on its cross process mutex, and
    /// an owning viewer does that inside its session. The short wait read a busy owner as an
    /// absent one and told the user the viewer was not running while it was in the middle of
    /// writing their source file - and then the snapshot left the menu a scan later, contradicting
    /// the balloon.
    /// <para>
    /// The same wait <see cref="InlineQueueClient"/> and OwnerLink use, and safe here for the same
    /// reason it is there: accepts run on a worker rather than the timer or the UI thread, which
    /// is what <see cref="Tracker.Accept(PendingSnapshot)"/> exists to arrange.
    /// </para>
    /// </summary>
    static readonly TimeSpan acceptWait = TimeSpan.FromSeconds(15);

    public string Description => $"owned by another process on port {ViewerClient.Port}";

    public IReadOnlyList<PendingSnapshot> List() =>
        TryList(out var pending) ? pending : [];

    /// <summary>
    /// False when the owner could not be asked, which <see cref="List"/> flattens to nothing
    /// pending — right for a menu, and wrong for anything reading the answer as a statement about
    /// a particular entry.
    /// </summary>
    static bool TryList(out IReadOnlyList<PendingSnapshot> pending)
    {
        if (!Exchange(new(ViewerVerb.List), ViewerClient.ShortTimeout, out var response) ||
            !response.Ok)
        {
            pending = [];
            return false;
        }

        pending = response.Items
            .Select(_ => new PendingSnapshot(_.Key, _.Name, _.Status))
            .ToList();
        return true;
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
        if (!Send(ViewerVerb.Accept, snapshot.Key, acceptWait, out message))
        {
            return AcceptOutcome.Failed;
        }

        if (!TryList(out var pending))
        {
            // The owner took the accept and then could not be asked what became of it. Applied is
            // a guess, and the one that tells the user a snapshot landed that may not have
            return AcceptOutcome.Unknown;
        }

        return pending.Any(_ => _.Key == snapshot.Key)
            ? AcceptOutcome.Failed
            : AcceptOutcome.Applied;
    }

    /// <summary>
    /// On <see cref="acceptWait"/>, not the short timeout. Discarding is not a clock driven call
    /// - it comes from the menu or a hot key - and the owner answering it may be busy inside
    /// InlineApplier, which waits up to ten seconds on its cross process mutex. Half a second
    /// turned a busy owner into "The snapshot viewer is not running."
    /// </summary>
    public bool Discard(PendingSnapshot snapshot, out string? message) =>
        Send(ViewerVerb.Discard, snapshot.Key, acceptWait, out message);

    /// <summary>
    /// True only when the queue is empty afterwards, for the reason <see cref="Accept"/> gives —
    /// and matching what an owning tray reports, which is also "is anything still pending". A
    /// conflict counts as not accepted, which is right: it is what a reviewer still has to resolve.
    /// </summary>
    // One accept per entry inside a single exchange, so this outlasts a lone accept rather than
    // matching it
    public bool AcceptAll(out string? message) =>
        Send(ViewerVerb.AcceptAll, null, acceptWait, out message) &&
        List().Count == 0;

    /// <summary>
    /// As <see cref="Discard"/>, and the outcome is returned rather than dropped. Discarded on a
    /// busy owner used to do nothing at all while Tracker.Clear went ahead and emptied its own
    /// snapshot list, so "Discard (n)" reported success and everything reappeared on the next
    /// scan two seconds later.
    /// </summary>
    public bool DiscardAll(out string? message) =>
        Send(ViewerVerb.DiscardAll, null, acceptWait, out message);

    public void Focus(PendingSnapshot snapshot) =>
        Send(ViewerVerb.Focus, snapshot.Key, ViewerClient.ShortTimeout, out _);

    /// <summary>
    /// Hidden rather than quit, because this owner is holding the queue. Quit exits the process,
    /// and the queue is in its memory, so one menu item meant "close the window" in the arrangement
    /// where the tray owns the queue and "throw away every pending snapshot, without asking" in
    /// this one - after which they were simply gone from the menu, a refused connection being
    /// indistinguishable from nothing pending.
    /// <para>
    /// Hiding leaves the process serving, which it has to be for the queue to survive at all, and
    /// leaves the user where the other arrangement leaves them: no window, and everything still
    /// pending. Focus brings it back.
    /// </para>
    /// </summary>
    public void Close() =>
        Send(ViewerVerb.Hide, null, ViewerClient.ShortTimeout, out _);

    static bool Send(ViewerVerb verb, string? key, TimeSpan wait, out string? message)
    {
        message = null;
        if (!Exchange(new(verb, key), wait, out var response))
        {
            message = "The snapshot viewer is not running.";
            return false;
        }

        message = response.Message is { Length: > 0 } text ? text : null;
        return response.Ok;
    }

    static bool Exchange(ViewerMessage message, TimeSpan wait, [NotNullWhen(true)] out ViewerResponse? response) =>
        ViewerClient.TrySend(message, out response, wait: wait);
}
