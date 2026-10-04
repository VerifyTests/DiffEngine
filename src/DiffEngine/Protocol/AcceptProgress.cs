namespace DiffEngine;

/// <summary>
/// How far an accept-all has got: <paramref name="Done"/> of the <paramref name="Total"/> entries
/// it set out to deal with, whichever way each of them went.
/// <para>
/// Accepting a long queue takes as long as the queue is long - every snapshot is a read, a parse
/// and a write under a cross process mutex, and every move can be retried for seconds while a diff
/// tool lets go of it - and nothing said so. A window that owned the queue froze for the whole
/// batch, and one displaying someone else's said "Waiting for the queue owner." over a list that
/// did not move until everything went at once.
/// </para>
/// <para>
/// On the wire because the batch runs in whichever process owns the queue and the window may
/// belong to another one. The owner answers a listing taken partway through with this, and the
/// displaying viewer says it in the same words the owning one would.
/// </para>
/// </summary>
record AcceptProgress(int Done, int Total)
{
    /// <summary>
    /// Whether the batch is a bulk discard rather than an accept-all: the same steps, each one
    /// throwing a received file away.
    /// <para>
    /// An owning viewer's discard was on no listing, since the progress line says an accept is
    /// under way to whoever reads it. So a window attached to that viewer saw the queue shrink
    /// with nothing saying why, and refused nothing meanwhile. It is listed now, as the same
    /// counts and a line of its own beside them (<see cref="ViewerResponse"/>). A reader from
    /// before that line skips it and takes the batch for an accept, which is the wrong word and
    /// the right behaviour: it refuses what changes the queue until the batch is done.
    /// </para>
    /// </summary>
    public bool Discarding { get; init; }

    /// <summary>
    /// The entry being worked on rather than the count finished, which is how a progress line
    /// reads: the first entry is "1 of 40" while it is being applied, not "0 of 40".
    /// </summary>
    public string Describe() =>
        $"{(Discarding ? "Discarding" : "Accepting")} {Math.Min(Done + 1, Total)} of {Total}";

    /// <summary>
    /// One more entry dealt with, however it went.
    /// </summary>
    public AcceptProgress Advance() =>
        this with { Done = Done + 1 };

    public string Build() =>
        $"{Done}|{Total}";

    public static bool TryParse(string value, [NotNullWhen(true)] out AcceptProgress? progress)
    {
        progress = null;
        var parts = value.Split('|');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var done) ||
            !int.TryParse(parts[1], out var total))
        {
            return false;
        }

        progress = new(done, total);
        return true;
    }
}
