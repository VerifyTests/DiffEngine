namespace DiffEngine;

/// <summary>
/// How a bulk accept has gone so far, counted an entry at a time.
/// <para>
/// Carried between the entries of a batch that completes each one as it lands, rather than all of
/// them at the end, so what the batch reports once it is done is the sentence
/// <see cref="InlineQueue.AcceptAllMessage"/> has always built for a bulk accept. A value rather
/// than something the batch updates in place, because the viewer keeps it in a session state that
/// is immutable.
/// </para>
/// </summary>
readonly record struct AcceptAllTally(int Accepted, int NotWritten, int Failed, string? Failure)
{
    /// <summary>
    /// A patch in this batch that was not written. What holds a sweep's pending deletes, since one
    /// of them may be the only copy left of a snapshot that never made it into the source.
    /// </summary>
    public bool Refused => NotWritten + Failed > 0;

    /// <summary>
    /// Conflicted entries are counted by the caller at the end rather than here, because a batch
    /// never applies one: they are whatever the queue still holds with more than one variant.
    /// </summary>
    public string Message(int conflicted) =>
        InlineQueue.AcceptAllMessage(Accepted, NotWritten, Failed, conflicted, Failure);
}
