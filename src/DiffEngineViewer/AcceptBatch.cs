/// <summary>
/// An accept-all in flight over a queue this process owns.
/// <para>
/// Carried out an entry at a time rather than in one transition. Accepting a snapshot reads,
/// patches and rewrites its source under a cross process mutex, and one transition over the whole
/// queue ran all of that on the render thread: the window stopped painting for as long as the
/// queue was long, and then emptied all at once. Now each entry is claimed under the session's
/// lock, applied outside it, and recorded under it again, so between entries the window draws the
/// queue shrinking and says how far the batch has got.
/// </para>
/// </summary>
/// <param name="Remaining">
/// The keys still to do, in the order they will be done: every snapshot before any file, because
/// whether a delete is held turns on how the snapshots went.
/// </param>
/// <param name="Total">How many the batch set out with, for the progress it reports.</param>
record AcceptBatch(IReadOnlyList<string> Remaining, int Total)
{
    /// <summary>
    /// How the snapshots have gone, which is the first half of what the batch says when it is done.
    /// </summary>
    public AcceptAllTally Tally { get; init; }

    /// <summary>
    /// Moves and deletes carried out.
    /// </summary>
    public int Swept { get; init; }

    /// <summary>
    /// Moves and deletes still pending afterwards: failed, or held because a snapshot was not
    /// written.
    /// </summary>
    public int Kept { get; init; }

    /// <summary>
    /// The entry claimed and being applied outside the session's lock, as it was when claimed,
    /// which is what recording the outcome checks the queue against. Null between entries.
    /// </summary>
    public QueueEntry? Current { get; init; }

    public AcceptProgress Progress =>
        new(Total - Remaining.Count - (Current is null ? 0 : 1), Total);
}
