/// <summary>
/// For a head whose toolkit can hold the loop's thread where the loop cannot see it. The loop in
/// <see cref="ViewerProgram" /> runs one frame per <see cref="IViewerWindow.Present" />, and on
/// Windows a present pumps messages - which is where user32 runs its own modal loops, for dragging
/// a scrollbar thumb and for moving or sizing the window, and where a logoff ends the session. The
/// loop's next step never comes in either case, so the head is handed those steps to run itself.
/// <para>
/// Optional, and only WinForms takes it: the native heads pump their own events and have no modal
/// loops to be caught in.
/// </para>
/// </summary>
interface ILoopHooks
{
    /// <summary>
    /// One frame of the loop, for a head to run on a timer from inside a modal loop: polls the
    /// window, applies what it reports, and returns the screen that results for the head to draw.
    /// Without it the panes froze until the thumb was let go, and a resize redrew the old screen
    /// into the new size until the mouse came up.
    /// </summary>
    Func<Screen>? Frame { set; }

    /// <summary>
    /// The session is ending: stop taking arrivals and stage what the queue holds, before
    /// returning. Windows may end the process as soon as a window has answered the message that
    /// says so, which is before the loop would get as far as its own shutdown.
    /// </summary>
    Action? SessionEnding { set; }
}
