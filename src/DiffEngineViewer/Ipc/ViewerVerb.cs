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
    /// Return the pending entries as key, name and status. Enough to build a menu. From the tray.
    /// </summary>
    List,

    /// <summary>
    /// Return the pending entries carrying their patches, which is everything needed to rebuild
    /// the whole display: the two texts, the diff, the headers and the scroll bounds. From a
    /// viewer showing a queue it does not own.
    /// </summary>
    ListFull,

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
