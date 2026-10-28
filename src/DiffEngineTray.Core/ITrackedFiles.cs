/// <summary>
/// The tray's tracked moves and deletes, as the inline queue owner reaches them to answer the
/// wire: listed into a full listing, and accepted or discarded by their prefixed keys. Tray only —
/// the interface a tray owner reaches its own tracker through. A viewer that owns the queue holds
/// the equivalent entries in its session instead, which is where DiffEngine's moves, deletes and
/// pairs go when no tray is running.
/// <para>
/// Everything here can run on a listener thread, so nothing behind it may raise UI: a locked move
/// is refused with a message pointing at the tray menu instead of prompting.
/// </para>
/// </summary>
interface ITrackedFiles
{
    IReadOnlyList<ViewerResponseMove> Moves();

    IReadOnlyList<ViewerResponseDelete> Deletes();

    /// <summary>
    /// A number that is another one whenever <see cref="Moves"/> or <see cref="Deletes"/> would
    /// list anything differently from the last time it was asked for, and cheap to ask for when
    /// they would not: the owner asks on every poll of an attached viewer, to tell it nothing
    /// changed without listing anything. It may also move when nothing listed did, which costs one
    /// listing.
    /// </summary>
    long Version();

    bool Has(string key);

    /// <summary>
    /// Same contract as <see cref="IQueueOwner.Accept"/>: false with no message is an unknown
    /// key, false with one is a refusal, true was carried out.
    /// </summary>
    (bool ok, string? message) Accept(string key);

    (bool ok, string? message) Discard(string key);

    /// <summary>
    /// Accept every tracked move, and the deletes named, without prompting. Kept is what stayed
    /// pending — locked moves, undeletable files, and deletes held back.
    /// </summary>
    /// <param name="deleteKeys">
    /// The deletes that were pending when the accept-all began, from <see cref="Deletes"/>, which
    /// are the only ones it may carry out. The owner lists them before it takes its snapshots,
    /// because one that arrives while they are applying belongs to a patch that is not in the
    /// batch, and the file it removes may be the only copy of that snapshot. One that has gone
    /// since is passed over, and neither accepted nor kept.
    /// </param>
    /// <param name="holdDeletes">
    /// Leave every delete pending rather than carrying it out, because a snapshot swept alongside
    /// was not written, and the file a delete removes may be the only copy of it left.
    /// </param>
    /// <param name="advanced">
    /// Called as each file is dealt with, whichever way it went, so the owner can say how far an
    /// accept-all has got while a locked move is still being retried.
    /// </param>
    (int accepted, int kept) AcceptAll(IReadOnlyCollection<string> deleteKeys, bool holdDeletes, Action? advanced = null);

    /// <summary>
    /// Track a pending move or delete that arrived over the viewer port rather than the piper one.
    /// That happens when the sending process saw no tray at startup and this tray started after
    /// it: that check is cached for the life of the sender, so its files come the other way for
    /// good, and dropping them would lose them.
    /// <para>
    /// <paramref name="source" /> is the received file of the pending move this one was derived
    /// from, or null. With no default, as on <see cref="IQueueOwner.TrackMove"/>, so nothing
    /// between the wire and the tracker can leave it behind.
    /// </para>
    /// </summary>
    void AddMove(string temp, string target, string? source);

    /// <inheritdoc cref="AddMove"/>
    void AddDelete(string file, string? source);

    /// <summary>
    /// Drop a tracked move or delete without touching the file, for a test that started passing.
    /// Neither <see cref="Accept"/> nor <see cref="Discard"/>, because both of those act on disk
    /// and DiffEngine has already dealt with the file by the time this arrives. False when the key
    /// was not tracked here, which is the goal state either way.
    /// </summary>
    bool Untrack(string key);

    int DiscardAll();
}
