/// <summary>
/// The tray's tracked moves and deletes, as the inline queue owner reaches them to answer the
/// wire: listed into a full listing, and accepted or discarded by their prefixed keys. Tray only —
/// a viewer that owns the queue never has any, because DiffEngine only sends moves and deletes to
/// a running tray.
/// <para>
/// Everything here can run on a listener thread, so nothing behind it may raise UI: a locked move
/// is refused with a message pointing at the tray menu instead of prompting.
/// </para>
/// </summary>
interface ITrackedFiles
{
    IReadOnlyList<ViewerResponseMove> Moves();

    IReadOnlyList<ViewerResponseDelete> Deletes();

    bool Has(string key);

    /// <summary>
    /// Same contract as <see cref="IQueueOwner.Accept"/>: false with no message is an unknown
    /// key, false with one is a refusal, true was carried out.
    /// </summary>
    (bool ok, string? message) Accept(string key);

    (bool ok, string? message) Discard(string key);

    /// <summary>
    /// Accept every tracked delete and move without prompting. Kept is what stayed pending —
    /// locked moves, undeletable files.
    /// </summary>
    (int accepted, int kept) AcceptAll();

    int DiscardAll();
}
