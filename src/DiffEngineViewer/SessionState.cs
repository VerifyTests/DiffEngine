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
    bool Exit)
{
    public QueueEntry? Current =>
        Selected >= 0 && Selected < Queue.Count ? Queue[Selected] : null;

    public static SessionState Start(ViewerMode mode, int columns = 120, int rows = 40) =>
        new(mode, [], -1, 0, null, columns, rows, false);
}
