/// <summary>
/// The screen for the session's state, built once per state rather than once per frame.
/// <para>
/// The loop presents sixty times a second, and a state changes when something happens, which for a
/// window somebody is reading is now and then. <see cref="SessionState"/> is immutable and only
/// ever replaced, so the same reference is the same screen. Building it again every frame was the
/// whole queue's labels, groups and tooltips, and a walk over every selected row, to arrive at the
/// screen already in hand: 2,000 pending entries were two megabytes of garbage a frame, from a
/// window nobody was touching.
/// </para>
/// <para>
/// Handing back the same <see cref="Screen"/> is also how a head learns that nothing changed
/// without comparing two of them or encoding one: the WinForms head and
/// <see cref="ScreenPayload"/> both stop at the reference.
/// </para>
/// <para>
/// One thread's. The loop and a head's modal loop both run on the thread that owns the window.
/// </para>
/// </summary>
sealed class ScreenCache
{
    SessionState? state;
    Screen? screen;

    public Screen For(SessionState current)
    {
        if (screen is null ||
            !ReferenceEquals(state, current))
        {
            screen = ScreenBuilder.Build(current);
            state = current;
        }

        return screen;
    }
}
