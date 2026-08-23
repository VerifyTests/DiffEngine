namespace DiffEngine;

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
    /// Track a pending file move: <c>key</c> is the received file, <c>body</c> the target it
    /// belongs at. From DiffEngine when no tray is running, so the pair still has somewhere to be
    /// pending rather than nowhere at all.
    /// <para>
    /// Never launches an owner. DiffRunner has already opened a diff tool for that file pair, and
    /// a second window competing with it is not an improvement.
    /// </para>
    /// </summary>
    Move,

    /// <summary>
    /// Show a two file comparison, tracking it as a pending move at the same time: <c>key</c> is
    /// the received file, <c>body</c> the target it belongs at. From DiffEngine when the diff tool
    /// it resolved for the pair is the viewer itself.
    /// <para>
    /// <see cref="Move"/> with a window, and the split is the whole reason both exist. A move for
    /// some other tool must not raise anything, because that tool has just opened its own window
    /// for the pair. When the viewer is the tool there is no such window, and one queue entry with
    /// a window raised over it is what replaces the process per pair every other tool needs.
    /// </para>
    /// </summary>
    Diff,

    /// <summary>
    /// Track a pending file delete: <c>key</c> is the file. From DiffEngine when no tray is
    /// running, and unlike <see cref="Move"/> this one does start a viewer when nothing owns the
    /// queue — a delete has no second file to compare against and so no diff tool to open, which
    /// left it with no surface whatsoever.
    /// </summary>
    Delete,

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
