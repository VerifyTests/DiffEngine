/// <summary>
/// Guards the one mutable thing in the process. The render loop reads it every frame while the
/// socket listener writes to it from its own thread, so both go through here.
/// </summary>
sealed class SessionHost(SessionState initial)
{
    readonly Lock gate = new();
    SessionState state = initial;

    public SessionState State
    {
        get
        {
            lock (gate)
            {
                return state;
            }
        }
    }

    public SessionState Mutate(Func<SessionState, SessionState> change)
    {
        lock (gate)
        {
            state = change(state);
            return state;
        }
    }
}
