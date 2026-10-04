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
    /// <param name="temp">The received file.</param>
    /// <param name="target">The file it belongs at.</param>
    /// <param name="documents">The viewer's documents folder, which the files are read with.</param>
    /// <param name="source">
    /// The received file of the pending move this one was derived from, as the sender said it, or
    /// null: see <see cref="QueueEntry.SourceKey"/>.
    /// </param>
    public static QueueEntry ForMove(string temp, string target, DocumentPlugin? documents = null, string? source = null) =>
        QueueEntry.ForMove(
            TrackedKeys.ForMove(temp),
            $"{Name(target)} ({Extension(target)})",
            SolutionDirectoryFinder.Find(target),
            KeyOfSource(source),
            temp,
            target,
            FileSide.Read(temp, documents),
            FileSide.Read(target, documents));

    /// <inheritdoc cref="ForMove"/>
    public static QueueEntry ForDelete(string file, DocumentPlugin? documents = null, string? source = null) =>
        QueueEntry.ForDelete(
            TrackedKeys.ForDelete(file),
            Path.GetFileName(file),
            SolutionDirectoryFinder.Find(file),
            KeyOfSource(source),
            file,
            FileSide.Read(file, documents));

    /// <summary>
    /// The key the source is queued under, which is the key <see cref="ForMove"/> gives a move
    /// for that received file. The same thing a tray's listing says, so an entry reads the same
    /// whichever process is holding it.
    /// </summary>
    static string? KeyOfSource(string? source)
    {
        if (source is null)
        {
            return null;
        }

        return TrackedKeys.ForMove(source);
    }

    /// <summary>
    /// What an entry that arrived again was derived from. An arrival that names a source says so.
    /// One that names none keeps what the queued entry had, because naming none is also what a
    /// pair handed on by something that was never told looks like - a viewer started by hand on
    /// the two files - and a source that has stopped being pending costs nothing to remember: an
    /// entry is shown beneath its source only while the source is in the queue.
    /// </summary>
    static string? KeyOfSource(QueueEntry queued, string? source) =>
        KeyOfSource(source) ?? queued.SourceKey;

    /// <summary>
    /// The entry for a pair whose files have been read again: the queued one with the files' new
    /// stamps when the two sides hold what it shows, and one built from them when they do not.
    /// <para>
    /// Building an entry diffs its two sides, which for a large pair is most of what an arrival
    /// costs, and a test that keeps failing the same way writes its received file again and sends
    /// the pair on every run. So the sides are asked as they were read, before anything is built
    /// from them. A <c>with</c> keeps the rows the queued entry already has.
    /// </para>
    /// <para>
    /// <paramref name="source" /> is what an arrival said the pair was derived from. The watch,
    /// which is reading a file again and has been told nothing, passes none.
    /// </para>
    /// </summary>
    public static QueueEntry MoveAgain(QueueEntry queued, string temp, string target, DocumentPlugin? documents = null, string? source = null)
    {
        var sourceKey = KeyOfSource(queued, source);
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
                RightStamp = targetSide.Stamp,
                SourceKey = sourceKey
            };
        }

        return QueueEntry.ForMove(queued.Key, queued.Name, queued.Solution, sourceKey, temp, target, tempSide, targetSide);
    }

    /// <summary>
    /// As <see cref="MoveAgain"/>, for a pending delete, whose one file is its right side.
    /// </summary>
    public static QueueEntry DeleteAgain(QueueEntry queued, string file, DocumentPlugin? documents = null, string? source = null)
    {
        var sourceKey = KeyOfSource(queued, source);
        var current = FileSide.Read(file, documents);
        if (queued.Kind == QueueEntryKind.Delete &&
            queued.LeftFile == file &&
            queued.Warning == current.Warning &&
            Shows(queued.RightText, queued.RightImage, queued.RightDocument, current))
        {
            return queued with
            {
                LeftStamp = current.Stamp,
                SourceKey = sourceKey
            };
        }

        var fresh = QueueEntry.ForDelete(queued.Key, queued.Name, queued.Solution, sourceKey, file, current);
        if (!queued.Written)
        {
            return fresh;
        }

        // Held because a move wrote this file, which is the very thing that has it read again
        // with something else in it: what it holds is another thing and why it is held is not.
        // Whether a delete raised again lets go of it is asked where it is queued
        // (ViewerSession.EnqueueTracked)
        return fresh with
        {
            Written = true,
            WrittenAs = queued.WrittenAs,
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
