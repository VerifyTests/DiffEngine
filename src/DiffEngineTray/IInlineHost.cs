/// <summary>
/// The inline queue as the tray reaches it: held in this process when the bind succeeded, driven
/// over the socket when a viewer got there first.
/// <para>
/// Decided once at startup and never changed, which is what makes handover something that does not
/// have to work rather than something that has to work perfectly.
/// </para>
/// </summary>
interface IInlineHost
{
    IReadOnlyList<PendingSnapshot> List();
    bool Accept(PendingSnapshot snapshot, out string? message);
    bool Discard(PendingSnapshot snapshot, out string? message);
    bool AcceptAll(out string? message);

    /// <summary>
    /// Bring the window forward on this item, launching one if there is none.
    /// </summary>
    void Focus(PendingSnapshot snapshot);

    /// <summary>
    /// Close the window. The queue is unaffected either way: an owning viewer exits with it, and a
    /// tray owned queue simply loses its display until something reopens one.
    /// </summary>
    void Close();
}
