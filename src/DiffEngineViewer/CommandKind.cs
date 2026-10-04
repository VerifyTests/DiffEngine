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

    /// <summary>
    /// Switch the panes between every line and only the changes, each with a few lines either
    /// side. View only, like <see cref="NextVariant"/>: it changes what is being read, never a
    /// file, and applies locally even when the queue belongs to someone else.
    /// </summary>
    ToggleMinimal,

    /// <summary>
    /// Cycle a document between its text with its page under it, its page alone, and its text
    /// alone. View only, like <see cref="ToggleMinimal"/>, and a setting of that kind of document
    /// rather than of the entry: it holds for every one of its kind in the queue, and is
    /// remembered for the next run.
    /// </summary>
    ToggleDrawing,

    /// <summary>
    /// Draw maps in the next <see cref="MapProjection"/>, both sides at once. View only, and a
    /// setting of the window that is remembered between runs.
    /// </summary>
    NextProjection,

    /// <summary>
    /// Enlarge the picture on screen a step, both sides at once, or take it back one, or all the
    /// way to fitted. View only, and the entry's: opening another starts fitted again.
    /// </summary>
    ZoomIn,
    ZoomOut,
    ZoomReset,

    /// <summary>
    /// Show a document's previous or next page, on both sides at once. View only.
    /// </summary>
    PreviousPage,
    NextPage,
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
    /// Show or hide the files derived from the document on screen, which are otherwise beneath it
    /// with no rows of their own. View only, as <see cref="ToggleGroup"/> is: hidden or shown,
    /// they are accepted and discarded with the document.
    /// </summary>
    ToggleDerived,

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
