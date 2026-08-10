/// <summary>
/// A pending inline snapshot, as the menu shows it.
/// <para>
/// The queue behind it is held by whichever process bound the port: this tray when it started
/// first, which is the usual case, and a viewer otherwise. Either way there is one implementation
/// of it, <see cref="InlineQueue"/> in DiffEngine, so the two hosts cannot differ on what
/// accepting or settling means.
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
