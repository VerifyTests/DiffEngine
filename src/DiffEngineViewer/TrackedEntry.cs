/// <summary>
/// The queue entry for a pending move or delete this viewer owns.
/// <para>
/// Derives the same key, name and group DiffEngineTray derives for the ones it owns, so a file
/// pending here is indistinguishable in the window from one pending there — which matters because
/// which process is tracking it depends only on whether a tray happened to be running.
/// </para>
/// <para>
/// Reads the files, so this is called on the listener thread rather than inside
/// <see cref="ViewerSession"/>, the same seam <see cref="OwnerLink"/> uses for the tray's.
/// </para>
/// </summary>
static class TrackedEntry
{
    public static QueueEntry ForMove(string temp, string target) =>
        QueueEntry.ForMove(
            TrackedKeys.ForMove(temp),
            $"{Name(target)} ({Extension(target)})",
            SolutionDirectoryFinder.Find(target),
            temp,
            target,
            FileSide.Read(temp),
            FileSide.Read(target));

    public static QueueEntry ForDelete(string file) =>
        QueueEntry.ForDelete(
            TrackedKeys.ForDelete(file),
            Path.GetFileName(file),
            SolutionDirectoryFinder.Find(file),
            file,
            FileSide.Read(file));

    /// <summary>
    /// Twice, because a verified file carries two: <c>Sample.Test.verified.txt</c> is the test
    /// <c>Sample.Test</c>. Exactly what <c>TrackedMove</c> does with the same path.
    /// </summary>
    static string Name(string target) =>
        Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(target));

    static string Extension(string target) =>
        Path.GetExtension(target).TrimStart('.');
}
