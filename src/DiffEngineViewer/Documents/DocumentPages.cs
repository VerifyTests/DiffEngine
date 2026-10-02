/// <summary>
/// The pages of the document on screen, read out of <see cref="SessionState.Renders"/>: how many
/// have been drawn, which differ, and which one is being shown. Pure, so the screen, the buttons and
/// the commands that turn pages cannot disagree about any of it.
/// </summary>
static class DocumentPages
{
    public static Rendering? Of(SessionState state, DocumentFile? side) =>
        side is { Hash: { } hash } &&
        state.Renders.TryGetValue(hash, out var rendering)
            ? rendering
            : null;

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
