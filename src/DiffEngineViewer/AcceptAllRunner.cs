/// <summary>
/// Carries out an accept-all over a queue this process owns: claims an entry under the session's
/// lock, applies it outside, records it under the lock again, and repeats until nothing is left.
/// <para>
/// Applying is where the time goes. A snapshot waits on InlineApplier's cross process mutex before
/// it rewrites its source, and a move can fail and be retried, so the lock is never held across
/// one: the render loop takes it every frame, and holding it for a whole batch was the window
/// freezing for a whole batch. Between entries the window draws the queue as it now is, with
/// <see cref="SessionState.Progress"/> in its status line.
/// </para>
/// <para>
/// One per owning process, shared by the two places an accept-all starts: the window, which hands
/// it to <see cref="Start"/> because its own thread has to keep drawing, and the socket, whose
/// listener thread runs <see cref="Drive"/> itself because the caller is waiting for the answer.
/// Two drives take turns rather than claiming the same entries, and the second finds nothing left.
/// </para>
/// </summary>
sealed class AcceptAllRunner(SessionHost host, ViewerActions actions)
{
    readonly Lock driving = new();
    Task? background;
    volatile bool busy;

    /// <summary>
    /// Whatever batch the session holds, carried out on a worker. From the render loop only, which
    /// is what makes <see cref="background"/> safe to read and write without a lock. A no-op while
    /// a drive is already under way, whichever thread it is on.
    /// </summary>
    public void Start()
    {
        if (busy ||
            background is { IsCompleted: false })
        {
            return;
        }

        background = Task.Run(Drive, Cancel.None);
    }

    /// <summary>
    /// Runs the session's batch to the end. Returns what the batch said once it finished: the
    /// message of the transition that finished it, rather than whatever the state holds by the
    /// time the caller reads it, since a settle arriving in between clears the status line.
    /// </summary>
    public string? Drive()
    {
        lock (driving)
        {
            busy = true;
            try
            {
                while (true)
                {
                    var claimed = host.Mutate(ViewerSession.ClaimNext);
                    if (claimed.Batch?.Current is not { } entry)
                    {
                        return claimed.Message;
                    }

                    Func<SessionState, SessionState> record;
                    try
                    {
                        record = ViewerSession.ApplyClaimed(entry, actions);
                    }
                    catch (Exception exception)
                    {
                        record = ViewerSession.FailClaimed(entry, exception.Message);
                    }

                    host.Mutate(record);
                }
            }
            finally
            {
                busy = false;
            }
        }
    }

    /// <summary>
    /// Waits out a batch in flight, for an owner on its way out: what it stages to disk afterwards
    /// should be the queue the batch left, not one it was part way through. The window has gone by
    /// then, so this is only ever waiting on file operations that are each bounded.
    /// </summary>
    public void Finish()
    {
        try
        {
            background?.Wait();
        }
        catch (AggregateException)
        {
            // A drive that threw has nothing more to wait for, and the owner still has a queue to
            // stage on its way out
        }

        lock (driving)
        {
            // Taken only to wait for a listener thread's drive to let go
        }
    }
}
