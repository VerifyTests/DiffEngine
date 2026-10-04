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
    /// The keys a group's batch is over, conflicted members included, or null for a batch over the
    /// whole queue. "Accept all in" a header is the same batch with fewer entries in it, and what
    /// it reports as still needing review is its own members and nobody else's.
    /// </summary>
    public IReadOnlySet<string>? Only { get; init; }

    public bool Covers(string key) =>
        Only is null ||
        Only.Contains(key);

    /// <summary>
    /// The name of the document this batch is the accept or the discard of, with the files derived
    /// from it, or null for any other batch: see <see cref="ViewerSession.BeginAcceptWithDerived"/>.
    /// <para>
    /// It changes nothing about how the batch goes, only what it says when it is done. To the
    /// reviewer that was one accept of one document, and "Accepted 0, plus 7 files" is the answer
    /// to a question they did not ask.
    /// </para>
    /// </summary>
    public string? Cascade { get; init; }

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
    /// Whether any of <see cref="Kept"/> is a delete left because of a move onto its file
    /// (<see cref="ViewerSession.HeldReason"/>), for the batch to say so when it is done: nothing
    /// failed, and a count of what was kept reads as though something had.
    /// </summary>
    public bool KeptForAMove { get; init; }

    /// <summary>
    /// The entry claimed and being applied outside the session's lock, as it was when claimed,
    /// which is what recording the outcome checks the queue against. Null between entries.
    /// </summary>
    public QueueEntry? Current { get; init; }

    /// <summary>
    /// The other snapshots claimed with <see cref="Current"/>: every one the batch still had to
    /// do in the same source file, to be written with it in one write.
    /// <para>
    /// A snapshot applied on its own reads, patches and rewrites its whole source file, and the
    /// rewrite is what costs: a file written a moment ago is scanned by whatever watches the drive
    /// before the next thing can open it, so five hundred snapshots in one file were half a
    /// minute of writes around a second of patching. Claimed together they are one read and one
    /// write (<see cref="InlineApplier.ApplyAll"/>), each still with an outcome of its own. So a
    /// batch goes a file at a time where its snapshots are, and an entry at a time otherwise.
    /// </para>
    /// </summary>
    public IReadOnlyList<QueueEntry> Together { get; init; } = [];

    /// <summary>
    /// Whether this is a bulk discard rather than an accept: the same steps, over the moves a
    /// discard-all or a header's discard covers, each step throwing a received file away.
    /// <para>
    /// A discard waits on nothing, which is why it was left one transition when accept-all
    /// stopped being one. But it deletes a file an entry, under the lock every arrival waits on
    /// and on the thread that draws, and a thousand pending pairs are a thousand deletes on a
    /// drive something is watching. The snapshots and the pending deletes of a discard touch no
    /// file, so they go in the transition that begins it, and only the moves are left to claim.
    /// </para>
    /// </summary>
    public bool Discarding { get; init; }

    /// <summary>
    /// For a discard, what its beginning said of the snapshots: the first half of what the batch
    /// says when it is done, as <see cref="Tally"/> is for an accept.
    /// </summary>
    public string Said { get; init; } = "";

    public AcceptProgress Progress =>
        new(Total - Remaining.Count - (Current is null ? 0 : 1 + Together.Count), Total);

    /// <summary>
    /// What the status line says while the batch runs. An accept's is
    /// <see cref="AcceptProgress.Describe"/>, the words a window displaying someone else's batch
    /// uses too, as it uses a discard's, which is on a listing as the same counts.
    /// </summary>
    public string Describe()
    {
        var progress = Progress;
        if (Discarding)
        {
            return $"Discarding {Math.Min(progress.Done + 1, progress.Total)} of {progress.Total}";
        }

        return progress.Describe();
    }
}
