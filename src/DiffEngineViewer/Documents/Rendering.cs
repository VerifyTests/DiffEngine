/// <summary>
/// How a document is shown. A view setting like <see cref="SessionState.Minimal"/>: it holds across
/// entries rather than belonging to one, and it is this window's to set even when the queue belongs
/// to someone else.
/// </summary>
enum DrawingView
{
    /// <summary>
    /// The text in the top half of each pane, and the page being read drawn under it.
    /// </summary>
    Both,

    /// <summary>
    /// The page alone, under the rows that describe the file.
    /// </summary>
    Picture,

    /// <summary>
    /// The text alone, as any text file is shown.
    /// </summary>
    Text
}

/// <summary>
/// What one document draws as, a png per page, kept in <see cref="SessionState.Renders"/> under the
/// hash of the bytes it was drawn from.
/// <para>
/// In the state rather than in the entry, because pages land one at a time and an entry is built
/// once (<see cref="QueueEntry"/>'s views are computed in its constructor): keeping them here means a
/// page arriving never rebuilds an entry, never closes the open menu, and is never undone by
/// <see cref="ViewerSession.Sync"/> handing back the entry an owner's listing described.
/// </para>
/// </summary>
/// <param name="Pages">The pages that have landed, in order.</param>
/// <param name="Complete">Every page has landed, or drawing stopped for good.</param>
/// <param name="Failure">Why drawing stopped short. The pages that landed before it are kept.</param>
sealed record Rendering(IReadOnlyList<RenderedPage> Pages, bool Complete, string? Failure = null)
{
    public static Rendering Started { get; } = new([], false);
}

/// <summary>
/// One drawn page: a png on this machine, which every head already knows how to draw from a path.
/// </summary>
/// <param name="Hash">
/// Of the png. Drawing is deterministic, so this is also how two pages are compared: the same page
/// draws to the same bytes.
/// </param>
record RenderedPage(string Path, int Width, int Height, string Hash);
