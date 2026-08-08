/// <summary>
/// A pending inline snapshot, as reported by the running viewer.
/// <para>
/// Unlike <see cref="TrackedMove"/> the tray does not own this: the viewer holds the queue, and
/// the tray is a remote control over the same socket. That keeps one queue and one set of
/// semantics on every platform, rather than a Windows-only copy that can drift.
/// </para>
/// </summary>
record PendingSnapshot(string Key, string Name, string? Status)
{
    /// <summary>
    /// The source file the snapshot will be spliced into, recovered from the key.
    /// </summary>
    public string Source
    {
        get
        {
            var separator = Key.LastIndexOf('|');
            return separator < 0 ? Key : Key[..separator];
        }
    }

    /// <summary>
    /// Solution directory, so snapshots group alongside moves and deletes in the menu.
    /// <para>
    /// The source path arrives from another process and is not guaranteed to exist here, so a
    /// missing directory means ungrouped rather than a crash while building the menu.
    /// </para>
    /// </summary>
    public string? Group
    {
        get
        {
            try
            {
                return SolutionDirectoryFinder.Find(Source);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return null;
            }
        }
    }
}
