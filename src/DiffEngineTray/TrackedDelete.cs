class TrackedDelete
{
    public TrackedDelete(string file, string? group, string? source = null)
    {
        File = file;
        Group = group;
        Source = source;
        Name = Path.GetFileName(file);
    }

    public string Name { get; }
    public string File { get; }
    public string? Group { get; }

    /// <summary>
    /// The received file of the pending move this delete was derived from, or null: a page a
    /// document no longer has, whose document is pending. See <see cref="TrackedMove.Source"/>.
    /// <para>
    /// Fixed, as everything else a listing carries of a delete is, so a delete raised again under
    /// another source is another object and <see cref="ITrackedFiles.Version"/> sees it.
    /// </para>
    /// </summary>
    public string? Source { get; }

    /// <summary>
    /// Set once a move has written this file while the delete was pending, and from then on no
    /// accept-all carries the delete out: see <see cref="Tracker.WroteItsFile"/>. Cleared when a
    /// test run raises the delete again, which is a statement made after the write.
    /// <para>
    /// The one thing here that changes, and a listing carries what follows from it, so whoever
    /// changes it has to say so to <see cref="ITrackedFiles.Version"/>: see the tracker's count of
    /// restores.
    /// </para>
    /// </summary>
    public bool Written { get; set; }
}
