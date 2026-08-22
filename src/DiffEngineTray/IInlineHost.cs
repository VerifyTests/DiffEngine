/// <summary>
/// The inline queue as the tray reaches it: held in this process when the bind succeeded, driven
/// over the socket when a viewer got there first.
/// <para>
/// Decided once at startup and never changed, which is what makes handover something that does not
/// have to work rather than something that has to work perfectly.
/// </para>
/// </summary>
interface IInlineHost
{
    /// <summary>
    /// Which process holds the queue, and on which port, for the debug view.
    /// </summary>
    string Description { get; }

    IReadOnlyList<PendingSnapshot> List();

    /// <summary>
    /// The queued patches themselves, for the debug view, and null when the queue is not in this
    /// process. What the tray holds then is the listing and nothing more: the patch is in the
    /// owning process's memory, and that process has its own view of it.
    /// </summary>
    IReadOnlyList<PendingInline>? Queued();

    AcceptOutcome Accept(PendingSnapshot snapshot, out string? message);

    bool Discard(PendingSnapshot snapshot, out string? message);
    bool AcceptAll(out string? message);
    /// <summary>
    /// False when the queue owner could not be asked, so a caller clearing its own state knows not
    /// to.
    /// </summary>
    bool DiscardAll(out string? message);

    /// <summary>
    /// Bring the window forward on this item, launching one if there is none.
    /// </summary>
    void Focus(PendingSnapshot snapshot);

    /// <summary>
    /// Close the window. The queue is unaffected either way: a tray owned queue loses its display
    /// until something reopens one, and an owning viewer is asked to hide rather than to quit,
    /// since quitting would take the queue it holds with it.
    /// </summary>
    void Close();
}

/// <summary>
/// What became of an accept. <see cref="Stale"/> exists because "no longer pending" and "in the
/// source now" are not the same thing: a patch whose call site has moved can never apply, so it is
/// dropped rather than left to fail forever, and the user has to be told to re-run rather than
/// left believing it was accepted.
/// </summary>
enum AcceptOutcome
{
    /// <summary>
    /// No entry for that key. It settled, or another surface got to it first.
    /// </summary>
    Unknown,

    /// <summary>
    /// The snapshot is in the source file now.
    /// </summary>
    Applied,

    /// <summary>
    /// Dropped without applying, because the source moved on since the test run.
    /// </summary>
    Stale,

    /// <summary>
    /// Still pending, and the message says why. Retryable once whatever blocked it is gone.
    /// </summary>
    Failed
}
