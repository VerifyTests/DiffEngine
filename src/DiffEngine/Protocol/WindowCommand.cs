namespace DiffEngine;

/// <summary>
/// Window side effects a message asks for, which only a render loop can perform.
/// <para>
/// On the wire because the owner of a queue is not always the process with the window. A tray that
/// owns one has no window at all, so it answers a listing with the command it wants performed and
/// the displaying viewer picks it up on its next refresh.
/// </para>
/// </summary>
enum WindowCommand
{
    Show,
    Hide,
    Focus,

    /// <summary>
    /// Close the window. For a viewer that owns its queue that is the process exiting; for one
    /// that is only displaying, the queue carries on without it.
    /// </summary>
    Close
}
