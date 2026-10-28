/// <summary>
/// An applier that holds one apply until the test lets it go, so an accept-all can be looked at
/// part way through: what a listing taken then says, and what a viewer displaying the queue shows.
/// </summary>
/// <param name="hold">Which apply to hold, counting from one.</param>
sealed class HeldApply(int hold) : IDisposable
{
    readonly ManualResetEventSlim reached = new();
    readonly ManualResetEventSlim released = new();
    int count;

    public InlineApplyResult Apply(InlinePatch patch)
    {
        if (Interlocked.Increment(ref count) == hold)
        {
            reached.Set();
            released.Wait(TimeSpan.FromSeconds(30));
        }

        return InlineApplyResult.Applied;
    }

    /// <summary>
    /// Blocks until the held apply has started, which is the batch at the point the test is about.
    /// </summary>
    public void WaitUntilHeld()
    {
        if (!reached.Wait(TimeSpan.FromSeconds(30)))
        {
            throw new("The held apply was never reached.");
        }
    }

    public void Release() =>
        released.Set();

    /// <summary>
    /// Never leaves a batch blocked behind a test that failed before letting it go. The events are
    /// left for the collector rather than disposed, since the apply being released may not have
    /// woken yet.
    /// </summary>
    public void Dispose() =>
        released.Set();
}
