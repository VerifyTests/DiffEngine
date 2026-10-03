/// <summary>
/// Hears that the Windows session is ending, which is how a tray started at login usually stops.
/// <para>
/// Nothing else here would. A logoff or a shutdown sends every top level window
/// WM_QUERYENDSESSION and then WM_ENDSESSION, and Windows may end the process as soon as those are
/// answered. A Form turns them into a close, which is how the viewer hears of it. The tray has no
/// Form, only the notify icon's window, which agrees to the query and does nothing more: so
/// <c>Application.Run()</c> never returns, nothing after it in Program runs, and the queue an
/// owning tray holds in memory went with the process where a clean exit stages it.
/// </para>
/// <para>
/// A window of its own, because the two messages are sent rather than posted, which a message
/// filter never sees. Top level, because a message only window is not sent them. Never shown.
/// </para>
/// <para>
/// On WM_ENDSESSION rather than on the query before it. Another application can still refuse the
/// query, and a session that then carries on would have a queue both staged on disk and held here.
/// </para>
/// </summary>
sealed class SessionEndWindow :
    NativeWindow,
    IDisposable
{
    const int endSession = 0x0016;

    readonly Action ending;

    /// <param name="ending">
    /// Run inside the message, on the thread this is created on, so whatever it does is done by
    /// the time the message is answered: there may be no time after that.
    /// </param>
    public SessionEndWindow(Action ending)
    {
        this.ending = ending;
        CreateHandle(new());
    }

    protected override void WndProc(ref Message message)
    {
        // WParam is zero when the session is not ending after all, which everything that agreed
        // to the query is told once something else has refused it
        if (message.Msg == endSession &&
            message.WParam != IntPtr.Zero)
        {
            try
            {
                ending();
            }
            catch (Exception exception)
            {
                // Logged and nothing more. Thrown out of here it reaches WinForms' exception
                // dialog, which during a logoff is a window holding the session open with nobody
                // left to answer it
                Log.Error(exception, "Failed while the session was ending");
            }
        }

        base.WndProc(ref message);
    }

    public void Dispose() =>
        DestroyHandle();
}
