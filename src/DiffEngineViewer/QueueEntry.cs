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
    FileStamp? RightStamp)
{
    // Computed once, because the diff is a pure function of the two texts and a new entry only
    // arrives on stdin or the socket. A `with` expression copies this field rather than
    // recomputing, so change the text by building a fresh entry, never by `with`.
    readonly (IReadOnlyList<Row> Left, IReadOnlyList<Row> Right) rows = DiffRows.Build(LeftText, RightText);

    public IReadOnlyList<Row> LeftRows => rows.Left;
    public IReadOnlyList<Row> RightRows => rows.Right;
    public int TotalRows => rows.Left.Count;

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

    public static QueueEntry ForFiles(string leftFile, string rightFile, string leftText, string rightText) =>
        new(
            Key: $"{leftFile.ToLowerInvariant()}|{rightFile.ToLowerInvariant()}",
            Name: $"{Path.GetFileName(leftFile)} <> {Path.GetFileName(rightFile)}",
            LeftHeader: Path.GetFileName(leftFile),
            RightHeader: Path.GetFileName(rightFile),
            LeftText: CsStringLiteral.NormalizeNewlines(leftText),
            RightText: CsStringLiteral.NormalizeNewlines(rightText),
            Kind: QueueEntryKind.File,
            Patch: null,
            LeftFile: leftFile,
            TargetFile: rightFile,
            Warning: null,
            Status: null,
            Solution: null,
            TestName: null,
            Variants: [],
            SelectedVariant: 0,
            LeftStamp: null,
            RightStamp: null);

    public static QueueEntry ForMove(
        string key,
        string name,
        string? group,
        string temp,
        string target,
        FileText tempText,
        FileText targetText) =>
        new(
            Key: key,
            Name: name,
            LeftHeader: Path.GetFileName(temp),
            RightHeader: Path.GetFileName(target),
            // Left is what the test produced, right is what is committed — the same sides an
            // inline entry uses for received and expected.
            LeftText: CsStringLiteral.NormalizeNewlines(tempText.Text),
            RightText: CsStringLiteral.NormalizeNewlines(targetText.Text),
            Kind: QueueEntryKind.Move,
            Patch: null,
            LeftFile: temp,
            TargetFile: target,
            Warning: tempText.Warning ?? targetText.Warning,
            Status: null,
            Solution: group,
            TestName: null,
            Variants: [],
            SelectedVariant: 0,
            LeftStamp: tempText.Stamp,
            RightStamp: targetText.Stamp);

    public static QueueEntry ForDelete(
        string key,
        string name,
        string? group,
        string file,
        FileText current) =>
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
            RightStamp: null);

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
