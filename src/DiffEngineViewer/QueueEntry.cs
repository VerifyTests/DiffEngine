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
    ImageFile? RightImage = null)
{
    // Computed once, because the diff is a pure function of the two sides and a new entry only
    // arrives on stdin or the socket. A `with` expression copies this field rather than
    // recomputing, so change the content by building a fresh entry, never by `with`.
    readonly (IReadOnlyList<Row> Left, IReadOnlyList<Row> Right) rows =
        LeftImage is null && RightImage is null
            ? DiffRows.Build(LeftText, RightText)
            : ImageRows.Build(LeftImage, RightImage);

    public IReadOnlyList<Row> LeftRows => rows.Left;
    public IReadOnlyList<Row> RightRows => rows.Right;
    public int TotalRows => rows.Left.Count;

    /// <summary>
    /// A picture on either side makes the whole entry one, because the two sides of a comparison
    /// are the same file under two names and cannot be a picture and a text file at once.
    /// </summary>
    public bool IsImage =>
        LeftImage is not null ||
        RightImage is not null;

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
            LeftText: CsStringLiteral.NormalizeNewlines(patch.NewContent),
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
            LeftText: CsStringLiteral.NormalizeNewlines(left.Text),
            RightText: CsStringLiteral.NormalizeNewlines(right.Text),
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
            RightImage: right.Image);

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
            LeftText: CsStringLiteral.NormalizeNewlines(tempSide.Text),
            RightText: CsStringLiteral.NormalizeNewlines(targetSide.Text),
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
            RightImage: targetSide.Image);

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
            RightText: CsStringLiteral.NormalizeNewlines(current.Text),
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
            RightImage: current.Image);

    static (string header, string text, string? warning) Expected(InlinePatch patch)
    {
        if (patch.OriginalExpression is null)
        {
            return ("expected (new snapshot)", "", null);
        }

        if (CsStringLiteral.TryParse(patch.OriginalExpression, out var value))
        {
            return ("expected", value, null);
        }

        // TryParse rejects interpolated literals and concatenations. Show the raw source text so
        // the change is still reviewable, and say so rather than pretending it is a parsed value.
        return (
            "expected (literal not parsed)",
            CsStringLiteral.NormalizeNewlines(patch.OriginalExpression),
            "Existing expected argument is not a plain string literal. Showing its source text.");
    }
}
