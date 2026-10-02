enum QueueEntryKind
{
    /// <summary>
    /// An inline snapshot, accepted by rewriting the source file.
    /// </summary>
    Inline,

    /// <summary>
    /// A two-file comparison from the command line, accepted by copying left over right.
    /// </summary>
    File,

    /// <summary>
    /// A tray-tracked move, displayed from the two local paths and accepted by forwarding its key
    /// to the tray.
    /// </summary>
    Move,

    /// <summary>
    /// A tray-tracked pending delete: the file's content against nothing.
    /// </summary>
    Delete
}

/// <summary>
/// One reviewable item. Inline entries carry their variants and are accepted by rewriting the
/// source file; file entries are accepted by copying left over right; move and delete entries
/// belong to the tray and are accepted by forwarding their keys.
/// </summary>
/// <param name="LeftImage">
/// Set when this side is a picture rather than text, which makes the whole entry an image
/// comparison. Null on the side of an image comparison that has no file yet.
/// </param>
/// <param name="LeftDocument">
/// Set when this side is a document - a PDF, an Office file or an SVG - read by a viewer with its
/// documents folder, which makes the whole entry a document comparison. Its text, once read, is
/// <paramref name="LeftText"/>, so the text views are any text entry's.
/// </param>
record QueueEntry(
    string Key,
    string Name,
    string LeftHeader,
    string RightHeader,
    string LeftText,
    string RightText,
    QueueEntryKind Kind,
    InlinePatch? Patch,
    string? LeftFile,
    string? TargetFile,
    string? Warning,
    string? Status,
    string? Solution,
    string? TestName,
    IReadOnlyList<InlineVariant> Variants,
    int SelectedVariant,
    FileStamp? LeftStamp,
    FileStamp? RightStamp,
    ImageFile? LeftImage = null,
    ImageFile? RightImage = null,
    DocumentFile? LeftDocument = null,
    DocumentFile? RightDocument = null)
{
    // Computed once, because the diff is a pure function of the two sides and a new entry only
    // arrives by launch or over the socket. A `with` expression copies this field rather than
    // recomputing, so change the content by building a fresh entry, never by `with`.
    readonly (DiffView Full, DiffView Minimal) views = Views(LeftText, RightText, LeftImage, RightImage, LeftDocument, RightDocument);

    // What a document is when its pages are being looked at: the rows describing the two files.
    readonly DiffView? properties = LeftDocument is null && RightDocument is null
        ? null
        : DiffView.Build(DocumentRows.Build(LeftDocument, RightDocument), fold: false).Full;

    static (DiffView Full, DiffView Minimal) Views(
        string leftText,
        string rightText,
        ImageFile? leftImage,
        ImageFile? rightImage,
        DocumentFile? leftDocument,
        DocumentFile? rightDocument)
    {
        if (leftImage is not null ||
            rightImage is not null)
        {
            return DiffView.Build(ImageRows.Build(leftImage, rightImage), fold: false);
        }

        // A document whose text is not there to diff - still being read, or unreadable - shows what
        // the file is instead of diffing nothing against its other side.
        if (leftDocument is { HasText: false } ||
            rightDocument is { HasText: false })
        {
            return DiffView.Build(DocumentRows.Build(leftDocument, rightDocument), fold: false);
        }

        return DiffView.Build(DiffRows.Build(leftText, rightText), fold: true);
    }

    public IReadOnlyList<Row> LeftRows => views.Full.Left;
    public IReadOnlyList<Row> RightRows => views.Full.Right;
    public int TotalRows => views.Full.Count;

    /// <summary>
    /// The rows the panes show: every one, or only the changes and the lines around them. The
    /// entry's own rows stay <see cref="LeftRows"/> and <see cref="RightRows"/> either way.
    /// </summary>
    public DiffView View(bool minimal)
    {
        if (minimal)
        {
            return views.Minimal;
        }

        return views.Full;
    }

    /// <summary>
    /// The rows on screen under a drawing view: a document's properties while its pages are being
    /// looked at, and the text views otherwise.
    /// </summary>
    public DiffView View(bool minimal, DrawingView drawing)
    {
        if (properties is not null &&
            ShowsProperties(drawing))
        {
            return properties;
        }

        return View(minimal);
    }

    /// <summary>
    /// A picture on either side makes the whole entry one, because the two sides of a comparison
    /// are the same file under two names and cannot be a picture and a text file at once.
    /// </summary>
    public bool IsImage =>
        LeftImage is not null ||
        RightImage is not null;

    /// <summary>
    /// A document on either side makes the whole entry one, as a picture does.
    /// </summary>
    public bool IsDocument =>
        LeftDocument is not null ||
        RightDocument is not null;

    /// <summary>
    /// Whether the text is all there to diff: false while either side's is still being read, and
    /// when either side's could not be.
    /// </summary>
    public bool HasText =>
        LeftDocument is not { HasText: false } &&
        RightDocument is not { HasText: false };

    /// <summary>
    /// Whether the panes show what the files are rather than what they say: always for the
    /// pictures alone, and until the text is there to diff otherwise.
    /// </summary>
    public bool ShowsProperties(DrawingView drawing) =>
        IsDocument &&
        (drawing == DrawingView.Picture || !HasText);

    public bool Conflicted => Variants.Count > 1;

    public static string KeyForInline(string sourceFile, int line) =>
        InlineKey.For(sourceFile, line);

    public static QueueEntry ForInline(PendingInline pending, int selectedVariant = 0)
    {
        var selected = Math.Clamp(selectedVariant, 0, pending.Variants.Count - 1);
        var variant = pending.Variants[selected];
        var patch = variant.Patch;
        var (rightHeader, rightText, warning) = Expected(patch);
        return new(
            Key: pending.Key,
            Name: pending.Name,
            // The origin rides the pane header so the reader always knows which framework's
            // content is under the cursor; an unlabeled patch keeps the plain header.
            LeftHeader: variant.Label is null ? "received" : $"received ({variant.Label})",
            RightHeader: rightHeader,
            LeftText: SourceLanguage.NormalizeNewlines(patch.NewContent),
            RightText: rightText,
            Kind: QueueEntryKind.Inline,
            Patch: patch,
            LeftFile: null,
            TargetFile: null,
            Warning: warning,
            Status: pending.Status,
            Solution: SolutionDirectoryFinder.Find(patch.SourceFile),
            TestName: patch.TestName,
            Variants: pending.Variants,
            SelectedVariant: selected,
            LeftStamp: null,
            RightStamp: null);
    }

    public static QueueEntry ForFiles(string leftFile, string rightFile, FileSide left, FileSide right) =>
        new(
            Key: $"{leftFile.ToLowerInvariant()}|{rightFile.ToLowerInvariant()}",
            Name: $"{Path.GetFileName(leftFile)} <> {Path.GetFileName(rightFile)}",
            LeftHeader: Path.GetFileName(leftFile),
            RightHeader: Path.GetFileName(rightFile),
            LeftText: SourceLanguage.NormalizeNewlines(left.Text),
            RightText: SourceLanguage.NormalizeNewlines(right.Text),
            Kind: QueueEntryKind.File,
            Patch: null,
            LeftFile: leftFile,
            TargetFile: rightFile,
            Warning: left.Warning ?? right.Warning,
            Status: null,
            Solution: null,
            TestName: null,
            Variants: [],
            SelectedVariant: 0,
            LeftStamp: left.Stamp,
            RightStamp: right.Stamp,
            LeftImage: left.Image,
            RightImage: right.Image,
            LeftDocument: left.Document,
            RightDocument: right.Document);

    public static QueueEntry ForMove(
        string key,
        string name,
        string? group,
        string temp,
        string target,
        FileSide tempSide,
        FileSide targetSide) =>
        new(
            Key: key,
            Name: name,
            LeftHeader: Path.GetFileName(temp),
            RightHeader: Path.GetFileName(target),
            // Left is what the test produced, right is what is committed — the same sides an
            // inline entry uses for received and expected.
            LeftText: SourceLanguage.NormalizeNewlines(tempSide.Text),
            RightText: SourceLanguage.NormalizeNewlines(targetSide.Text),
            Kind: QueueEntryKind.Move,
            Patch: null,
            LeftFile: temp,
            TargetFile: target,
            Warning: tempSide.Warning ?? targetSide.Warning,
            Status: null,
            Solution: group,
            TestName: null,
            Variants: [],
            SelectedVariant: 0,
            LeftStamp: tempSide.Stamp,
            RightStamp: targetSide.Stamp,
            LeftImage: tempSide.Image,
            RightImage: targetSide.Image,
            LeftDocument: tempSide.Document,
            RightDocument: targetSide.Document);

    public static QueueEntry ForDelete(
        string key,
        string name,
        string? group,
        string file,
        FileSide current) =>
        new(
            Key: key,
            Name: name,
            // Left is the after state, the same direction every other entry reads in — and after
            // accepting a delete there is nothing, so the file's content sits on the right,
            // marked as what goes.
            LeftHeader: "(deleted)",
            RightHeader: Path.GetFileName(file),
            LeftText: "",
            RightText: SourceLanguage.NormalizeNewlines(current.Text),
            Kind: QueueEntryKind.Delete,
            Patch: null,
            LeftFile: file,
            TargetFile: null,
            Warning: current.Warning,
            Status: null,
            Solution: group,
            TestName: null,
            Variants: [],
            SelectedVariant: 0,
            LeftStamp: current.Stamp,
            RightStamp: null,
            // The file on the right is the one that goes, so a picture being deleted is the right
            // side's picture. Nothing is on the left, which is the point of the entry.
            RightImage: current.Image,
            RightDocument: current.Document);

    static (string header, string text, string? warning) Expected(InlinePatch patch)
    {
        if (patch.OriginalExpression is null)
        {
            // A producer whose language has no CallerArgumentExpression - F#, which does not
            // implement it - anchors on the argument's value instead. It is the same snapshot the
            // expression would have parsed to, so it is the same pane; without this every F#
            // entry read as a new snapshot against an empty side, with every received line new.
            if (patch.OriginalValue is null)
            {
                return ("expected (new snapshot)", "", null);
            }

            return ("expected", SourceLanguage.NormalizeNewlines(patch.OriginalValue), null);
        }

        // Read as the language of the file it came out of: an F# literal is not a C# one, and a
        // parse that guessed would show a snapshot's source text where it has a value to show
        if (SourceLanguage.ForFile(patch.SourceFile).TryParse(patch.OriginalExpression, out var value))
        {
            return ("expected", value, null);
        }

        // TryParse rejects interpolated literals and concatenations. Show the raw source text so
        // the change is still reviewable, and say so rather than pretending it is a parsed value.
        return (
            "expected (literal not parsed)",
            SourceLanguage.NormalizeNewlines(patch.OriginalExpression),
            "Existing expected argument is not a plain string literal. Showing its source text.");
    }
}
