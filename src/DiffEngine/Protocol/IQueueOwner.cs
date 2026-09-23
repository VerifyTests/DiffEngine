/// <summary>
/// The queue half of the wire protocol, implemented by whoever owns the inline queue: the tray
/// over its <see cref="InlineQueue"/>, the viewer over its session. <see cref="ViewerMessageHandler"/>
/// maps the verbs onto this, so validation and the wire's error and success shapes are written
/// once and the two hosts cannot drift — the same argument that put one queue implementation
/// behind both.
/// </summary>
interface IQueueOwner
{
    /// <summary>
    /// Add, or fold into the entry for the same call site, returning how many are now pending.
    /// </summary>
    int Enqueue(InlinePatch patch);

    /// <summary>
    /// Drop the entry for a key whose test started passing, or is no longer an inline snapshot at
    /// all — or, with an origin, just that framework's variant of it. An unknown key falls back to
    /// <paramref name="member" />, narrowed by <paramref name="value" /> where one is sent, and is
    /// otherwise a no-op, because the entry being gone is the goal state.
    /// </summary>
    void Settle(string key, string? origin, string? member, string? value);

    /// <summary>
    /// Track a pending file move, replacing the entry for the same received file — a re-run
    /// produces the same pair, and a second entry for it is a duplicate rather than news.
    /// <para>
    /// Only reaches an owner when no tray was running in the sending process, so it is normally
    /// the viewer that answers this. A tray owner routes it into the same tracked moves the piper
    /// port fills, which is what a tray started after the test process needs: that process's
    /// tray check is cached, so its moves come here for the rest of its life.
    /// </para>
    /// </summary>
    void TrackMove(string temp, string target);

    /// <inheritdoc cref="TrackMove"/>
    void TrackDelete(string file);

    /// <summary>
    /// The whole listing response rather than just its items, because an owner answers with its
    /// tracked moves and deletes beside the queue, and a tray adds the window command it has
    /// stashed.
    /// </summary>
    ViewerResponse Listing(bool withPatches);

    /// <summary>
    /// Something that changes whenever the full listing would, and is cheap to take: the listing
    /// is every patch serialized, and an attached viewer asks for it five times a second. Unique
    /// to this owner, so a tag from an owner that has since restarted can never match. Null to
    /// always answer in full.
    /// </summary>
    string? ListingTag();

    bool Has(string key);

    /// <summary>
    /// Ok false with no message means no entry for the key. False with a message is a refusal:
    /// nothing was attempted, and the message says why — a conflicted entry with no origin to
    /// pick, or a locked tracked move. True means attempted, including a retryable apply failure,
    /// whose message says what went wrong while the entry stays pending.
    /// <para>
    /// Written is whether a snapshot is in the source now: applied, or found already there. False
    /// for anything else, a tracked file included, since that has no snapshot. It is what a surface
    /// accepting a group from someone else's queue waits on before sending the group's deletes,
    /// because ok cannot say it: a patch whose call site moved is attempted, and dropped unwritten.
    /// </para>
    /// </summary>
    (bool ok, string? message, bool written) Accept(string key, string? origin);

    (bool ok, string? message) Discard(string key);

    string? AcceptAll();

    string? DiscardAll();

    /// <summary>
    /// Do this to the window, wherever the window is: directly for a viewer that holds one,
    /// stashed for the next listing by a tray that does not. <paramref name="key"/> is the entry
    /// to select while doing it, and only a focus carries one.
    /// </summary>
    void Window(WindowCommand command, string? key);
}
