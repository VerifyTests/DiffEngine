enum CommandKind
{
    None,
    ScrollUp,
    ScrollDown,
    PageUp,
    PageDown,
    ScrollHome,
    ScrollEnd,

    /// <summary>
    /// Scroll to an absolute row, carried in <see cref="Command.Index"/>. What a scrollbar thumb
    /// reports, as opposed to the notches a wheel does. Clamped like every other scroll.
    /// </summary>
    ScrollTo,
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
    /// Fold or unfold the group whose header the context menu was opened on. View only: what is
    /// folded away is still queued and still swept by <see cref="AcceptAll"/>.
    /// </summary>
    ToggleGroup,

    /// <summary>
    /// Select every line of one pane, the side of whatever is already selected. View only, and
    /// applied locally even when the queue belongs to someone else: what is on screen is this
    /// process's to read however it likes.
    /// </summary>
    SelectAll,

    /// <summary>
    /// Put the selected pane text on the clipboard. Nothing selected copies nothing and says so,
    /// rather than guessing at a range the reader did not ask for.
    /// </summary>
    Copy,

    /// <summary>
    /// Both sides whole, for a reader who wants the text rather than a range of it. Filler rows
    /// are left out, so what lands on the clipboard is the file's lines rather than the diff's
    /// padding.
    /// </summary>
    CopyLeft,
    CopyRight,

    /// <summary>
    /// Show the current entry's file in the platform's file manager. Local IO even when the queue
    /// belongs to someone else, because the protocol never leaves the machine.
    /// </summary>
    RevealSource,
    Quit
}
