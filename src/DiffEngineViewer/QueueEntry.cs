/// <summary>
/// One reviewable item. Inline entries carry a <see cref="InlinePatch"/> and are accepted by
/// rewriting the source file. File entries carry a target path and are accepted by copying
/// left over right.
/// </summary>
record QueueEntry(
    string Key,
    string Name,
    string LeftHeader,
    string RightHeader,
    string LeftText,
    string RightText,
    InlinePatch? Patch,
    string? LeftFile,
    string? TargetFile,
    string? Warning,
    string? Status)
{
    // Computed once, because the diff is a pure function of the two texts and a new entry only
    // arrives on stdin or the socket. A `with` expression copies this field rather than
    // recomputing, so change the text by building a fresh entry, never by `with`.
    readonly (IReadOnlyList<Row> Left, IReadOnlyList<Row> Right) rows = DiffRows.Build(LeftText, RightText);

    public IReadOnlyList<Row> LeftRows => rows.Left;
    public IReadOnlyList<Row> RightRows => rows.Right;
    public int TotalRows => rows.Left.Count;

    /// <summary>
    /// Identity for replace-in-place and settle. A re-run of the same test produces the same key.
    /// </summary>
    public static string KeyForInline(string sourceFile, int line) =>
        $"{sourceFile.ToLowerInvariant()}|{line}";

    public static QueueEntry ForInline(InlinePatch patch)
    {
        var (rightHeader, rightText, warning) = Expected(patch);
        return new(
            Key: KeyForInline(patch.SourceFile, patch.LineHint),
            Name: $"{Path.GetFileName(patch.SourceFile)}:{patch.LineHint}",
            LeftHeader: "received",
            RightHeader: rightHeader,
            LeftText: CsStringLiteral.NormalizeNewlines(patch.NewContent),
            RightText: rightText,
            Patch: patch,
            LeftFile: null,
            TargetFile: null,
            Warning: warning,
            Status: null);
    }

    public static QueueEntry ForFiles(string leftFile, string rightFile, string leftText, string rightText) =>
        new(
            Key: $"{leftFile.ToLowerInvariant()}|{rightFile.ToLowerInvariant()}",
            Name: $"{Path.GetFileName(leftFile)} <> {Path.GetFileName(rightFile)}",
            LeftHeader: Path.GetFileName(leftFile),
            RightHeader: Path.GetFileName(rightFile),
            LeftText: CsStringLiteral.NormalizeNewlines(leftText),
            RightText: CsStringLiteral.NormalizeNewlines(rightText),
            Patch: null,
            LeftFile: leftFile,
            TargetFile: rightFile,
            Warning: null,
            Status: null);

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
