namespace DiffEngine;

/// <summary>
/// What became of an accept sent to the queue owner.
/// </summary>
public enum InlineAcceptOutcome
{
    /// <summary>
    /// There is nothing to report. No owner answered, or the one that did holds no entry for that
    /// key — it settled, or another surface got to it first.
    /// <para>
    /// Also the answer when an owner took the accept and then could not be asked what became of
    /// it. That is rare and it is not nothing, but the two outcomes it sits between are worse
    /// guesses: <see cref="Accepted"/> stops a caller offering a snapshot that may still be
    /// pending, and <see cref="Failed"/> invites a retry of one that is probably already applied.
    /// </para>
    /// </summary>
    Unknown,

    /// <summary>
    /// The entry is no longer pending, which is almost always because the snapshot is in the
    /// source file now.
    /// <para>
    /// It also covers a patch the owner dropped as stale, because the wire carries whether the
    /// verb was carried out rather than an apply status, and from here the two are the same
    /// observation: the entry has gone. <see cref="InlineQueueClient.Accept"/> hands back the
    /// owner's own message, which distinguishes them in words - "Applied Sample.cs:42" against
    /// "Sample.cs:42 source changed, re-run the test" - so a surface that shows it is telling the
    /// truth either way.
    /// </para>
    /// </summary>
    Accepted,

    /// <summary>
    /// Still pending, and the message says why: an apply that could not write the file, or a
    /// conflicted entry that a reviewer has to resolve before it can be accepted at all.
    /// Retryable once whatever blocked it is gone.
    /// </summary>
    Failed
}

/// <summary>
/// The pending inline snapshots, for a review surface that is neither DiffEngineViewer nor
/// DiffEngineTray — an IDE plugin, or any other tool that wants to show what a test run left
/// pending and accept it.
/// <para>
/// Everything here is a short loopback exchange with whichever process owns the queue, so a
/// surface built on this is a peer of the tray rather than a fallback for when no viewer could be
/// found. Accepting runs in the owner, which is what keeps one writer per source file and leaves
/// every display agreeing about what is still pending; there is no local apply to settle
/// afterwards.
/// </para>
/// <para>
/// A refused connection means nobody owns the queue: no test run has queued anything, or the
/// process that held it has gone. That is reported rather than thrown, since it is the ordinary
/// state of a machine with no failing snapshots.
/// </para>
/// </summary>
public static class InlineQueueClient
{
    /// <summary>
    /// Sized for the accept, which is the one verb that can legitimately take this long: the owner
    /// applies the patch through <see cref="InlineApplier" />, which waits up to ten seconds on its
    /// cross process mutex. A shorter wait reads a busy owner as an absent one.
    /// </summary>
    static readonly TimeSpan acceptWait = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Every pending inline snapshot the owner holds, with the patches that produced them, so a
    /// caller can render the snapshot and the text it replaces without reading anything from disk.
    /// <para>
    /// False when no owner answered, which is not the same as an empty queue: a surface that wants
    /// to fall back to the files a test run staged needs to tell those apart.
    /// </para>
    /// <para>
    /// Uses the short wait, because this is what an interactive surface calls to decide whether to
    /// offer an action, and an owner that cannot answer a listing in half a second is wedged rather
    /// than slow.
    /// </para>
    /// </summary>
    public static bool TryList(out IReadOnlyList<PendingInline> pending)
    {
        // An owner that answered with an error has not told us what it holds, and an empty item
        // list on an error is not the same statement as an empty queue
        if (!Exchange(new(ViewerVerb.ListFull), ViewerClient.ShortTimeout, out var response) ||
            !response.Ok)
        {
            pending = [];
            return false;
        }

        pending = ViewerListing.Pending(response.Items);
        return true;
    }

    /// <summary>
    /// Which call sites are pending, and nothing else, over the listing that carries no patches.
    /// <para>
    /// For a surface deciding whether to offer an action rather than one about to render a
    /// snapshot — an IDE building a context menu, say, where the payload of every queued patch is
    /// not worth the round trip. <see cref="InlineKey.For" /> builds a key from a source file and
    /// a line, so a caller can ask about its own call sites without matching on anything else.
    /// </para>
    /// </summary>
    public static bool TryListKeys(out IReadOnlyList<string> keys)
    {
        if (!Exchange(new(ViewerVerb.List), ViewerClient.ShortTimeout, out var response) ||
            !response.Ok)
        {
            keys = [];
            return false;
        }

        keys = response.Items.Select(_ => _.Key).ToList();
        return true;
    }

    /// <summary>
    /// The entry for a call site, or null when nothing is pending for it.
    /// <see cref="InlineKey.For" /> builds the key from a source file and a line.
    /// </summary>
    public static PendingInline? Find(string key) =>
        TryList(out var pending)
            ? pending.FirstOrDefault(_ => _.Key == key)
            : null;

    /// <summary>
    /// Asks the owner to apply the patch for a call site and drop it from the queue.
    /// <paramref name="message" /> is the owner's own account of what happened, suitable to show a
    /// user as it stands, and null when it had nothing to say — which is most of the time for
    /// <see cref="InlineAcceptOutcome.Unknown" />, since nothing usually happened.
    /// </summary>
    public static InlineAcceptOutcome Accept(string key, out string? message)
    {
        message = null;
        if (!Exchange(new(ViewerVerb.Accept, key), acceptWait, out var response))
        {
            return InlineAcceptOutcome.Unknown;
        }

        message = Text(response.Message);
        if (!response.Ok)
        {
            // One error shape covers both "no entry for that key" and a refusal on a live one — a
            // conflicted entry — so which it was is asked rather than read out of the text.
            if (StillPending(key) == true)
            {
                return InlineAcceptOutcome.Failed;
            }

            // The owner's phrasing here names a key rather than a snapshot, so it says nothing a
            // caller would want to show.
            message = null;
            return InlineAcceptOutcome.Unknown;
        }

        // Attempted, but attempted is not applied: an owner keeps an entry that failed to write so
        // it can be retried. Whether the entry survived is the answer, and it has to be asked for
        // rather than read off `ok`.
        return StillPending(key) switch
        {
            true => InlineAcceptOutcome.Failed,
            false => InlineAcceptOutcome.Accepted,
            // The owner took the accept and then could not be asked what became of it. Reading
            // that as applied is a guess, and the one that loses a snapshot: a caller told it was
            // accepted stops offering it
            null => InlineAcceptOutcome.Unknown
        };
    }

    /// <summary>
    /// Drops a pending snapshot without applying it. False when no owner answered, or it held
    /// nothing for that key.
    /// </summary>
    public static bool Discard(string key, out string? message)
    {
        message = null;
        if (!Exchange(new(ViewerVerb.Discard, key), ViewerClient.ShortTimeout, out var response))
        {
            return false;
        }

        message = Text(response.Message);
        return response.Ok;
    }

    /// <summary>
    /// Brings the viewer window forward on an entry, starting one when the owner has no window of
    /// its own. For a surface that wants to hand a snapshot over to be reviewed rather than
    /// accepting it outright.
    /// </summary>
    public static bool Focus(string key) =>
        Exchange(new(ViewerVerb.Focus, key), ViewerClient.ShortTimeout, out var response) &&
        response.Ok;

    /// <summary>
    /// Whether the owner still holds the entry, or null when it could not be asked - it went away,
    /// or answered with an error. Three answers rather than two because "it is not pending" and
    /// "there is no telling" lead to opposite reports, and a failed listing used to give the first.
    /// </summary>
    static bool? StillPending(string key) =>
        TryListKeys(out var keys)
            ? keys.Contains(key)
            : null;

    static string? Text(string? message) =>
        message is { Length: > 0 } ? message : null;

    static bool Exchange(
        ViewerMessage message,
        TimeSpan wait,
        [NotNullWhen(true)] out ViewerResponse? response) =>
        ViewerClient.TrySend(message, out response, wait: wait);
}
