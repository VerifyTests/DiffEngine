/// <summary>
/// The whole application state. Immutable; <see cref="ViewerSession"/> maps one state to the next.
/// Nothing here touches IO or the native library, which is what makes every screen snapshottable.
/// </summary>
record SessionState(
    ViewerMode Mode,
    IReadOnlyList<QueueEntry> Queue,
    int Selected,
    int ScrollTop,
    string? Message,
    int Columns,
    int Rows,
    bool Exit,
    // The open context menu. Closed by any other input, and by anything that changes the queue,
    // because its members index the queue it was opened over.
    MenuState? Menu = null)
{
    /// <summary>
    /// The group headers that are folded, by <see cref="QueueItem.GroupKey"/>.
    /// <para>
    /// Keyed by name rather than by position, so a fold survives its members being accepted out
    /// from under it, and a group that empties and comes back comes back folded.
    /// </para>
    /// <para>
    /// A view, never a filter: what is folded away is still queued, still accepted by "accept all",
    /// and still counted by the header that hides it.
    /// </para>
    /// </summary>
    public IReadOnlySet<string> Collapsed { get; init; } = new HashSet<string>();

    public QueueEntry? Current =>
        Selected >= 0 && Selected < Queue.Count ? Queue[Selected] : null;

    public static SessionState Start(ViewerMode mode, int columns = 120, int rows = 40) =>
        new(mode, [], -1, 0, null, columns, rows, false);
}
