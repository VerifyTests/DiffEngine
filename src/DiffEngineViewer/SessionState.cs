/// <summary>
/// The whole application state. Immutable; <see cref="ViewerSession"/> maps one state to the next.
/// Nothing here touches IO or the native library, which is what makes every screen snapshottable.
/// </summary>
record SessionState(
    ViewerMode Mode,
    IReadOnlyList<QueueEntry> Queue,
    int Selected,
    int ScrollTop,
    string? Message,
    int Columns,
    int Rows,
    bool Exit,
    // The open context menu. Closed by any other input, and by anything that changes the queue,
    // because its members index the queue it was opened over.
    MenuState? Menu = null)
{
    /// <summary>
    /// The user asked to close the window: Q, Escape, or the Close menu item. Consumed by the
    /// loop into the same decision as the window's own close button, so every way of closing
    /// shares one meaning. Deliberately not <see cref="Exit"/>, which leaves unconditionally:
    /// quit-as-exit made the keyboard mean "throw away every pending snapshot, without asking"
    /// while the close button hid the window and kept the queue.
    /// </summary>
    public bool QuitRequested { get; init; }

    /// <summary>
    /// The loop has committed to leaving, so nothing more may join the queue. Set under the host's
    /// lock, which is the point of it: a window leaving because its queue emptied used to read
    /// <see cref="Exit"/> without the lock and keep answering while it went, so a patch or a pair
    /// that arrived in between was acknowledged to its sender and then left with the process.
    /// Arrivals that land before this is set clear <see cref="Exit"/> and keep the window; ones
    /// that land after are refused, and the sender stages what it had rather than believing it
    /// queued.
    /// </summary>
    public bool Closing { get; init; }

    /// <summary>
    /// The group headers that are folded, by <see cref="QueueItem.GroupKey"/>.
    /// <para>
    /// Keyed by name rather than by position, so a fold survives its members being accepted out
    /// from under it, and a group that empties and comes back comes back folded.
    /// </para>
    /// <para>
    /// A view, never a filter: what is folded away is still queued, still accepted by "accept all",
    /// and still counted by the header that hides it.
    /// </para>
    /// </summary>
    public IReadOnlySet<string> Collapsed { get; init; } = new HashSet<string>();

    /// <summary>
    /// The pane text the reader has selected, or null. Carried here rather than in the frame
    /// because a drag survives scrolling, resizing and anything else that rebuilds a
    /// <see cref="Screen"/>, which is every frame.
    /// </summary>
    public TextSelection? Selection { get; init; }

    /// <summary>
    /// Whether the panes show only the changes, each with <see cref="DiffView.Context"/> lines
    /// either side, rather than every line. A view setting like <see cref="Collapsed"/>: it holds
    /// across entries rather than belonging to one, and it is this window's to set even when the
    /// queue belongs to someone else.
    /// </summary>
    public bool Minimal { get; init; }

    /// <summary>
    /// How each kind of document is shown, for the kinds the reader has chosen a view for. A view
    /// setting like <see cref="Minimal"/>, but one per kind rather than one for the window: a
    /// spreadsheet read as its text and a map looked at as its picture are both what their reader
    /// wants, in the same queue. Remembered from one run to the next.
    /// </summary>
    public IReadOnlyDictionary<DocumentFormat, DrawingView> Drawings { get; init; } = new Dictionary<DocumentFormat, DrawingView>();

    /// <summary>
    /// How the document on screen is shown: its text, its text with its page under it, or its page
    /// alone. Whatever was last chosen for its kind, and both at once until something is, since a
    /// document's change is often only visible drawn.
    /// </summary>
    public DrawingView Drawing =>
        Shown is { } format &&
        Drawings.TryGetValue(format, out var view)
            ? view
            : DrawingView.Both;

    /// <summary>
    /// This state with the kind of document on screen shown as <paramref name="view"/>, now and
    /// whenever another of its kind is opened. The identical state when nothing on screen has a
    /// view to choose.
    /// </summary>
    public SessionState Showing(DrawingView view)
    {
        if (Shown is not { } format ||
            Drawing == view)
        {
            return this;
        }

        var drawings = new Dictionary<DocumentFormat, DrawingView>(Drawings)
        {
            [format] = view
        };
        return this with { Drawings = drawings };
    }

    /// <summary>
    /// The kind of document on screen, by whichever side there is: the two sides of a comparison
    /// are one file under two names.
    /// </summary>
    DocumentFormat? Shown =>
        (Current?.LeftDocument ?? Current?.RightDocument)?.Format;

    /// <summary>
    /// How far the picture on screen is enlarged: a <see cref="PictureZoom"/> step, 0 for fitted.
    /// The entry's rather than the window's, unlike <see cref="Minimal"/>: every path that opens
    /// an entry puts it back, since a part of one picture worth looking at closely says nothing
    /// about where to look in the next.
    /// </summary>
    public int Zoom { get; init; }

    /// <summary>
    /// Which part of an enlarged picture is shown. See <see cref="PanPoint"/>.
    /// </summary>
    public PanPoint Pan { get; init; } = PanPoint.Centre;

    /// <summary>
    /// How maps are drawn. A view setting like <see cref="Drawing"/>, and remembered from one run
    /// to the next: a reader who wants every map in one projection chooses it once.
    /// </summary>
    public MapProjection Projection { get; init; }

    /// <summary>
    /// What the queue's documents draw as, by <see cref="DocumentPages.Key"/>: the hash of the
    /// bytes each was drawn from, and for a map the projection too. Filled a page at a time by
    /// <see cref="DocumentWatch"/>, and dropped once no entry has those bytes.
    /// </summary>
    public IReadOnlyDictionary<string, Rendering> Renders { get; init; } = new Dictionary<string, Rendering>();

    /// <summary>
    /// The page of the current document being shown, on both sides at once. Null is the opening
    /// page: the first that differs once that is known, the way an entry opens at its first change,
    /// and the first page until then. Set by the reader turning pages, and cleared by every path
    /// that opens an entry.
    /// </summary>
    public int? Page { get; init; }

    /// <summary>
    /// The current entry's rows as <see cref="Minimal"/> and <see cref="Drawing"/> lay them out.
    /// Everything that scrolls reads these rather than the entry's own, so the scroll, the
    /// scrollbar and change navigation all count the rows that are on screen.
    /// </summary>
    public DiffView? View => Current?.View(Minimal, Drawing);

    /// <summary>
    /// Whether the rows on screen describe the current document rather than its text. Nothing can
    /// be selected in them: a selection is held in rows of the text.
    /// </summary>
    public bool ShowsProperties => Current?.ShowsProperties(Drawing) == true;

    /// <summary>
    /// The selection, but only while it still describes what is on screen. Everything that reads
    /// one goes through this, so a stale selection needs no clearing: the entry it named is gone,
    /// so it stops existing.
    /// </summary>
    public TextSelection? LiveSelection =>
        Selection != null && Selection.Describes(Current) && !ShowsProperties ? Selection : null;

    /// <summary>
    /// The accept-all this process is carrying out over a queue it owns, or null.
    /// </summary>
    public AcceptBatch? Batch { get; init; }

    /// <summary>
    /// The owner's accept-all as its last listing described it, for a viewer displaying someone
    /// else's queue. Null for an owning viewer, whose own is <see cref="Batch"/>.
    /// </summary>
    public AcceptProgress? OwnerProgress { get; init; }

    /// <summary>
    /// How far an accept-all has got, whichever process is running it. What the status line says
    /// while one runs, and what takes the acting buttons away until it has finished.
    /// </summary>
    public AcceptProgress? Progress =>
        Batch?.Progress ?? OwnerProgress;

    /// <summary>
    /// The progress an owner puts on its listings: its accept-all's. Not a bulk discard's, which
    /// the wire has no words for: <see cref="AcceptProgress"/> is read by whoever displays the
    /// queue as an accept under way, and says so on their status line.
    /// </summary>
    public AcceptProgress? ListedProgress =>
        Batch is { Discarding: true } ? null : Progress;

    public QueueEntry? Current =>
        Selected >= 0 && Selected < Queue.Count ? Queue[Selected] : null;

    public static SessionState Start(ViewerMode mode, int columns = 120, int rows = 40) =>
        new(mode, [], -1, 0, null, columns, rows, false);
}
