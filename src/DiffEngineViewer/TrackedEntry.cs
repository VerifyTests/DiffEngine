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
    public static QueueEntry ForMove(string temp, string target, DocumentPlugin? documents = null) =>
        QueueEntry.ForMove(
            TrackedKeys.ForMove(temp),
            $"{Name(target)} ({Extension(target)})",
            SolutionDirectoryFinder.Find(target),
            temp,
            target,
            FileSide.Read(temp, documents),
            FileSide.Read(target, documents));

    public static QueueEntry ForDelete(string file, DocumentPlugin? documents = null) =>
        QueueEntry.ForDelete(
            TrackedKeys.ForDelete(file),
            Path.GetFileName(file),
            SolutionDirectoryFinder.Find(file),
            file,
            FileSide.Read(file, documents));

    /// <summary>
    /// The entry for a pair whose files have been read again: the queued one with the files' new
    /// stamps when the two sides hold what it shows, and one built from them when they do not.
    /// <para>
    /// Building an entry diffs its two sides, which for a large pair is most of what an arrival
    /// costs, and a test that keeps failing the same way writes its received file again and sends
    /// the pair on every run. So the sides are asked as they were read, before anything is built
    /// from them. A <c>with</c> keeps the rows the queued entry already has.
    /// </para>
    /// </summary>
    public static QueueEntry MoveAgain(QueueEntry queued, string temp, string target, DocumentPlugin? documents = null)
    {
        var tempSide = FileSide.Read(temp, documents);
        var targetSide = FileSide.Read(target, documents);
        if (queued.Kind == QueueEntryKind.Move &&
            queued.LeftFile == temp &&
            queued.TargetFile == target &&
            queued.Warning == (tempSide.Warning ?? targetSide.Warning) &&
            Shows(queued.LeftText, queued.LeftImage, queued.LeftDocument, tempSide) &&
            Shows(queued.RightText, queued.RightImage, queued.RightDocument, targetSide))
        {
            return queued with
            {
                LeftStamp = tempSide.Stamp,
                RightStamp = targetSide.Stamp
            };
        }

        return QueueEntry.ForMove(queued.Key, queued.Name, queued.Solution, temp, target, tempSide, targetSide);
    }

    /// <summary>
    /// As <see cref="MoveAgain"/>, for a pending delete, whose one file is its right side.
    /// </summary>
    public static QueueEntry DeleteAgain(QueueEntry queued, string file, DocumentPlugin? documents = null)
    {
        var current = FileSide.Read(file, documents);
        if (queued.Kind == QueueEntryKind.Delete &&
            queued.LeftFile == file &&
            queued.Warning == current.Warning &&
            Shows(queued.RightText, queued.RightImage, queued.RightDocument, current))
        {
            return queued with { LeftStamp = current.Stamp };
        }

        var fresh = QueueEntry.ForDelete(queued.Key, queued.Name, queued.Solution, file, current);
        if (!queued.Written)
        {
            return fresh;
        }

        // Held because a move wrote this file, which is the very thing that has it read again
        // with something else in it: what it holds is another thing and why it is held is not.
        // A run raising the delete again is what lets go of it (ViewerSession.EnqueueTracked)
        return fresh with
        {
            Written = true,
            Status = queued.Status
        };
    }

    static bool Shows(string text, ImageFile? image, DocumentFile? document, FileSide side) =>
        QueueEntry.SameSide(text, image, document, SourceLanguage.NormalizeNewlines(side.Text), side.Image, side.Document);

    /// <summary>
    /// Twice, because a verified file carries two: <c>Sample.Test.verified.txt</c> is the test
    /// <c>Sample.Test</c>. Exactly what <c>TrackedMove</c> does with the same path.
    /// </summary>
    static string Name(string target) =>
        Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(target));

    static string Extension(string target) =>
        Path.GetExtension(target).TrimStart('.');
}
