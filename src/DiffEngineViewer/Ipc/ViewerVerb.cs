enum ViewerVerb
{
    /// <summary>
    /// Queue a patch, or replace the existing entry with the same key. From DiffEngine.
    /// </summary>
    Inline,

    /// <summary>
    /// Drop the entry for a key, because a previously failing test now passes. From DiffEngine.
    /// </summary>
    Settle,

    /// <summary>
    /// Return the pending entries. From the tray.
    /// </summary>
    List,

    Accept,
    AcceptAll,
    Discard,
    DiscardAll,

    /// <summary>
    /// Select an entry, unhide and raise the window.
    /// </summary>
    Focus,

    Show,
    Hide,
    Quit
}
