/// <summary>
/// For a head whose toolkit can hold the loop's thread where the loop cannot see it. The loop in
/// <see cref="ViewerProgram" /> runs one frame per <see cref="IViewerWindow.Present" />, and on
/// Windows a present pumps messages - which is where user32 runs its own modal loops, for dragging
/// a scrollbar thumb and for moving or sizing the window, and where a logoff ends the session. On
/// Linux a present reads the X server's events, and a logoff ends the process from inside it as the
/// server goes. The loop's next step never comes in any of these, so the head is handed those steps
/// to run itself.
/// <para>
/// Optional. WinForms takes both; the native heads only <see cref="SessionEnding" />, which the
/// macOS one never calls, since a logout's quit there is the app's own. The Linux one has no modal
/// loops to be caught in. AppKit has: a menu, which is deliberate, and a window being resized,
/// which still draws the rows sliced for its old size until the mouse comes up. Handing that head
/// a frame from inside the loop would take a callback in the C ABI.
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
    /// says so, and Xlib ends it as soon as its handler for the lost server returns, both before
    /// the loop would get as far as its own shutdown.
    /// </summary>
    Action? SessionEnding { set; }
}
