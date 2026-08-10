/// <summary>
/// Guards the one mutable thing in the process. The render loop reads it every frame while the
/// socket listener writes to it from its own thread, so both go through here.
/// <para>
/// Reads take no lock. <see cref="SessionState" /> is immutable, so a reader only needs the
/// reference, and reading a reference is already atomic; volatile is there to stop the read being
/// hoisted out of the render loop.
/// </para>
/// <para>
/// That is not an optimisation. Accepting runs InlineApplier inside <see cref="Mutate" />, which
/// takes a cross process mutex and can wait ten seconds for it. A reader that queued behind the
/// writer would stall the render loop for that whole time, and a window that stops pumping for
/// five seconds is one Windows paints over with "Not Responding".
/// </para>
/// </summary>
sealed class SessionHost(SessionState initial)
{
    readonly Lock gate = new();
    volatile SessionState state = initial;

    public SessionState State => state;

    /// <summary>
    /// Still serialised, so two writers cannot compute from the same starting state and one lose.
    /// </summary>
    public SessionState Mutate(Func<SessionState, SessionState> change)
    {
        lock (gate)
        {
            state = change(state);
            return state;
        }
    }
}
