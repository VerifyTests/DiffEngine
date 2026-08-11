enum CommandKind
{
    None,
    ScrollUp,
    ScrollDown,
    PageUp,
    PageDown,
    ScrollHome,
    ScrollEnd,
    NextChange,
    PreviousChange,
    NextItem,
    PreviousItem,
    SelectItem,
    Accept,
    AcceptAll,
    Discard,
    DiscardAll,

    /// <summary>
    /// Cycle to the next variant of a conflicted entry. Wraps, so one command covers the two or
    /// three variants a multi-targeted run produces. View state only: it changes what is being
    /// read, never a file, and applies locally even when the queue belongs to someone else.
    /// </summary>
    NextVariant,

    /// <summary>
    /// Accept every member of the group whose header the context menu was opened on, skipping
    /// conflicted entries the way accept-all does.
    /// </summary>
    AcceptGroup,
    DiscardGroup,

    /// <summary>
    /// Show the current entry's file in the platform's file manager. Local IO even when the queue
    /// belongs to someone else, because the protocol never leaves the machine.
    /// </summary>
    RevealSource,
    Quit
}
