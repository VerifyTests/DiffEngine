using System.Net.NetworkInformation;

/// <summary>
/// The queue belongs to a viewer that bound the port before this tray started, so every call is a
/// short loopback round trip and the tray is a remote control.
/// <para>
/// The listing and the menu verbs use ViewerClient.ShortTimeout. The listing runs from the 2
/// second scan timer, so a slow exchange must not outlast the timer period. It is no longer what
/// the menu is built from - see <see cref="Tracker.Snapshots"/> - so nothing here is waited on
/// from the UI thread except a verb the user clicked.
/// </para>
/// <para>
/// Accepting does not, for the reason <see cref="acceptWait"/> gives.
/// </para>
/// <para>
/// No owner means the viewer has gone, which is the same as nothing pending. The queue went with
/// it, and this tray does not take ownership: it was decided at startup.
/// </para>
/// <para>
/// "No owner" is asked of the OS rather than found out by connecting - see
/// <see cref="PortIsHeld"/>.
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

    /// <summary>
    /// An accept-all is one exchange holding an apply per entry, so it outlasts a lone accept by as
    /// many entries as the queue holds, and fifteen seconds read a long queue as a missing viewer
    /// partway through accepting it. The owning viewer says how far it has got in its own window;
    /// this only bounds one that took the verb and wedged. On a worker, as every accept here is.
    /// </summary>
    static readonly TimeSpan acceptAllWait = TimeSpan.FromMinutes(5);

    public string Description => $"owned by another process on port {ViewerClient.Port}";

    public IReadOnlyList<PendingSnapshot> List() =>
        TryAsk(out var pending) ? pending : [];

    /// <summary>
    /// No owner is nothing pending here as well: the viewer has gone, and its queue went with it.
    /// An owner that holds the port and did not answer is another matter. It is cold starting, or
    /// wedged, and may be holding a patch whose delete this tray has. "Accept all" read that
    /// silence as an empty queue and carried its deletes out, so the verified file went with the
    /// patch that replaces it never tried.
    /// </summary>
    public bool TryList(out IReadOnlyList<PendingSnapshot> pending)
    {
        if (TryAsk(out pending))
        {
            return true;
        }

        // Decided now rather than from what was known going in, so an owner that exited while it
        // was being asked reads as the absent one it has become
        return !OwnerPresent();
    }

    /// <summary>
    /// False when the owner could not be asked, which <see cref="List"/> flattens to nothing
    /// pending — right for a menu, and wrong for anything reading the answer as a statement about
    /// a particular entry.
    /// </summary>
    static bool TryAsk(out IReadOnlyList<PendingSnapshot> pending)
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

        if (!TryAsk(out var pending))
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
    /// <para>
    /// Refused is read back the same way, out of the full listing that follows, since the wire
    /// carries a message rather than a tally. The owner keeps an entry it could not write and says
    /// why on it, while one that arrived during the batch carries nothing and a conflict is never
    /// tried - so a non-conflicted entry with a status is one this batch refused. An owner that
    /// could not be asked, before or after, counts as refused: what waits on the answer is a
    /// delete, and a delete is the one thing not safe to guess about.
    /// </para>
    /// </summary>
    public bool AcceptAll(out string? message, out bool refused)
    {
        if (!Send(ViewerVerb.AcceptAll, null, acceptAllWait, out message) ||
            !Exchange(new(ViewerVerb.ListFull), ViewerClient.ShortTimeout, out var response) ||
            !response.Ok)
        {
            refused = true;
            return false;
        }

        // A full listing lists a conflicted entry's other variants, and an entry has them exactly
        // when it is conflicted
        refused = response.Items.Any(_ => _.Variants.Count == 0 &&
                                         _.Status is not null);
        return response.Items.Count == 0;
    }

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

    static bool Exchange(ViewerMessage message, TimeSpan wait, [NotNullWhen(true)] out ViewerResponse? response)
    {
        // Only a table that says nothing holds the port skips the round trip. With no table to
        // read, the connect decides as it always did
        if (PortIsHeld() == false)
        {
            response = null;
            return false;
        }

        return ViewerClient.TrySend(message, out response, wait: wait);
    }

    /// <summary>
    /// Whether an owner is there to be asked, for telling one that did not answer from there being
    /// none. Asked once an exchange has failed, which <see cref="Exchange"/> reports the same way
    /// for both.
    /// <para>
    /// The table says when nothing holds the port. When something does, or there is no table to
    /// read, the exchange got as far as connecting and <see cref="ViewerClient"/> recorded what it
    /// found: a refused connection, or a reply that is not this protocol, is no owner. The second
    /// matters because the default port is registered to another program. A tray that could not
    /// bind the port over one drives it remotely for good, and taking that program for a viewer
    /// that does not answer would hold every delete on that machine.
    /// </para>
    /// </summary>
    static bool OwnerPresent() =>
        PortIsHeld() != false &&
        !ViewerClient.FoundUnowned();

    /// <summary>
    /// Whether anything holds the port, asked of the OS rather than found out by connecting to it.
    /// <para>
    /// Connecting to a port nothing is listening on is supposed to be refused at once, and every
    /// caller here was written expecting that. It is not refused at once on every machine: where
    /// the SYN is dropped rather than answered with a reset, the connect runs to its timeout
    /// instead. Once the owning viewer exits, that is the full
    /// <see cref="ViewerClient.ShortTimeout"/> per call - half a second on the two second scan,
    /// and half a second on every menu verb - for the rest of this tray's life, because ownership
    /// is decided at startup and this host is never replaced.
    /// </para>
    /// <para>
    /// The listener table is a local kernel query costing well under a millisecond, and it answers
    /// the only question worth asking first. Matched on the port alone: a listener on any address
    /// accepts a loopback connection, so the round trip is skipped only when nothing at all holds
    /// the port. Racing it is harmless either way - an owner that binds just after the check is
    /// found by the next call, and one that exits just after it costs the timeout exactly as
    /// before.
    /// </para>
    /// <para>
    /// Null when there is no table to read.
    /// </para>
    /// </summary>
    static bool? PortIsHeld()
    {
        var port = ViewerClient.Port;
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Any(_ => _.Port == port);
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }
}
