class TrackedDelete
{
    public TrackedDelete(string file, string? group)
    {
        File = file;
        Group = group;
        Name = Path.GetFileName(file);
    }

    public string Name { get; }
    public string File { get; }
    public string? Group { get; }

    /// <summary>
    /// Set once a move has written this file while the delete was pending, and from then on no
    /// accept-all carries the delete out: see <see cref="Tracker.WroteItsFile"/>. Cleared when a
    /// test run raises the delete again, which is a statement made after the write.
    /// </summary>
    public bool Written { get; set; }
}
