/// <summary>
/// When to read a tracked file again that stats but cannot be read: one another process holds
/// open, or one this process may not open. Shared by the two passes that keep tracked entries in
/// step with the disk, <see cref="TrackedWatch" /> for a queue this process owns and
/// <see cref="OwnerLink" /> for one it displays.
/// <para>
/// A failed read leaves an entry with no stamp, which differs from the stat on every pass, so each
/// pass read the file again and re-diffed it, five times a second for as long as whatever held it
/// held it. It is retried at <see cref="Interval" /> instead: rare enough to cost nothing, and soon
/// enough that a file that was only briefly locked is shown shortly after it frees up.
/// </para>
/// </summary>
sealed class ReadRetry
{
    public static TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(2);

    readonly Dictionary<string, DateTime> waiting = [];

    /// <summary>
    /// Whether the entry for <paramref name="key" /> failed a read too recently to try again.
    /// </summary>
    public bool Waiting(string key) =>
        waiting.TryGetValue(key, out var until) &&
        DateTime.UtcNow < until;

    /// <summary>
    /// Notes how a read of an entry's files went, from the entry it produced.
    /// </summary>
    public void Read(QueueEntry entry)
    {
        if (Unreadable(entry.LeftFile, entry.LeftStamp) ||
            Unreadable(entry.TargetFile, entry.RightStamp))
        {
            waiting[entry.Key] = DateTime.UtcNow + Interval;
            return;
        }

        waiting.Remove(entry.Key);
    }

    /// <summary>
    /// There, and not read. A target that is not there at all is a new snapshot, not a failure.
    /// </summary>
    static bool Unreadable(string? path, FileStamp? read) =>
        path is not null &&
        read is null &&
        FileSide.StampOf(path) is not null;
}
