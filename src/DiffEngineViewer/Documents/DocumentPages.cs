/// <summary>
/// The pages of the document on screen, read out of <see cref="SessionState.Renders"/>: how many
/// have been drawn, which differ, and which one is being shown. Pure, so the screen, the buttons and
/// the commands that turn pages cannot disagree about any of it.
/// </summary>
static class DocumentPages
{
    public static Rendering? Of(SessionState state, DocumentFile? side) =>
        Key(side, state.Projection) is { } key &&
        state.Renders.TryGetValue(key, out var rendering)
            ? rendering
            : null;

    /// <summary>
    /// What a side's pages are kept under in <see cref="SessionState.Renders"/>: the hash of its
    /// bytes, and for a map drawn in a projection the reader chose, that as well. The same bytes
    /// draw differently in each, and keeping them apart is what makes switching back to one
    /// already drawn cost nothing.
    /// <para>
    /// <see cref="MapProjection.Auto"/> is the bare hash, as every other document is, so a map
    /// nobody has switched is kept exactly where it always was. Null for a side whose bytes could
    /// not be read.
    /// </para>
    /// </summary>
    public static string? Key(DocumentFile? side, MapProjection projection)
    {
        if (side is not { Hash: { } hash } document)
        {
            return null;
        }

        if (document.IsMap &&
            projection != MapProjection.Auto)
        {
            return $"{hash}{separator}{projection}";
        }

        return hash;
    }

    /// <summary>
    /// The content hash a key was made from, which is what says whether the queue still holds the
    /// document it was drawn from.
    /// </summary>
    public static string HashOf(string key)
    {
        var end = key.IndexOf(separator);
        return end < 0 ? key : key[..end];
    }

    // Not a character a hex hash can contain
    const char separator = '.';

    public static (Rendering? Left, Rendering? Right) Of(SessionState state, QueueEntry entry) =>
        (Of(state, entry.LeftDocument), Of(state, entry.RightDocument));

    /// <summary>
    /// The most pages either side has drawn so far.
    /// </summary>
    public static int Count(Rendering? left, Rendering? right) =>
        Math.Max(left?.Pages.Count ?? 0, right?.Pages.Count ?? 0);

    /// <summary>
    /// The pages that differ, as far as drawing has got: both sides have drawn the page and it came
    /// out differently, or one side finished without it. A page only one side has drawn so far, with
    /// the other still drawing, is not known yet and not listed.
    /// <para>
    /// None for a pair whose bytes match, for a pair with only one side, which has nothing to
    /// differ from, and for one that could not be drawn, whose missing pages would otherwise all
    /// read as differing.
    /// </para>
    /// </summary>
    public static IReadOnlyList<int> Differing(QueueEntry entry, Rendering? left, Rendering? right)
    {
        if (entry.LeftDocument is not { Hash: { } leftHash } ||
            entry.RightDocument is not { Hash: { } rightHash } ||
            leftHash == rightHash ||
            left is not { Failure: null } ||
            right is not { Failure: null })
        {
            return [];
        }

        var differing = new List<int>();
        var count = Count(left, right);
        for (var index = 0; index < count; index++)
        {
            var leftPage = index < left.Pages.Count ? left.Pages[index] : null;
            var rightPage = index < right.Pages.Count ? right.Pages[index] : null;
            if (leftPage is not null &&
                rightPage is not null)
            {
                if (leftPage.Hash != rightPage.Hash)
                {
                    differing.Add(index);
                }

                continue;
            }

            var missing = leftPage is null ? left : right;
            if (missing.Complete)
            {
                differing.Add(index);
            }
        }

        return differing;
    }

    /// <summary>
    /// The page on screen: the one the reader turned to, or the opening page, kept inside the pages
    /// there are.
    /// </summary>
    public static int Current(SessionState state)
    {
        if (state.Current is not { IsDocument: true } entry)
        {
            return 0;
        }

        var (left, right) = Of(state, entry);
        var page = state.Page ?? Opening(entry, left, right);
        return Math.Clamp(page, 0, Math.Max(0, Count(left, right) - 1));
    }

    /// <summary>
    /// The first page that differs, the way an entry opens at its first change, or the first page.
    /// </summary>
    static int Opening(QueueEntry entry, Rendering? left, Rendering? right)
    {
        if (Differing(entry, left, right) is [var first, ..])
        {
            return first;
        }

        return 0;
    }
}
