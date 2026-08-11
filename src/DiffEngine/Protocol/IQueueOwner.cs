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
    /// Drop the entry for a key whose test started passing — or, with an origin, just that
    /// framework's variant of it. An unknown key is a no-op, because the entry being gone is the
    /// goal state.
    /// </summary>
    void Settle(string key, string? origin);

    /// <summary>
    /// The whole listing response rather than just its items, because the tray answers with the
    /// window command it has stashed plus its tracked moves and deletes, and the viewer has
    /// nothing to add to the items.
    /// </summary>
    ViewerResponse Listing(bool withPatches);

    bool Has(string key);

    /// <summary>
    /// Ok false with no message means no entry for the key. False with a message is a refusal:
    /// nothing was attempted, and the message says why — a conflicted entry with no origin to
    /// pick, or a locked tracked move. True means attempted, including a retryable apply failure,
    /// whose message says what went wrong while the entry stays pending.
    /// </summary>
    (bool ok, string? message) Accept(string key, string? origin);

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
