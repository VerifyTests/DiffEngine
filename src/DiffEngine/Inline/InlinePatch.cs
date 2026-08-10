namespace DiffEngine;

/// <summary>
/// Describes a pending inline-snapshot edit to a C# source file.
/// <para>
/// Settable properties so it round-trips through <see cref="InlinePatchFile"/>, but no
/// parameterless constructor: a patch with no source file or no content is not a patch, and
/// letting one be built only moved the problem to a null check further along.
/// </para>
/// </summary>
public sealed class InlinePatch
{
    public InlinePatch(
        string sourceFile,
        int lineHint,
        string? originalExpression,
        string newContent,
        InlinePatchMode mode = InlinePatchMode.Set)
    {
        SourceFile = sourceFile;
        LineHint = lineHint;
        OriginalExpression = originalExpression;
        NewContent = newContent;
        Mode = mode;
    }

    /// <summary>
    /// Full path to the .cs file.
    /// </summary>
    public string SourceFile { get; set; }

    /// <summary>
    /// 1 based line of the verify or Snapshot call. A hint only; content search is the locator.
    /// </summary>
    public int LineHint { get; set; }

    /// <summary>
    /// Verbatim source text of the previous expected argument.
    /// Null when the call had no expected argument (or a bare null argument).
    /// </summary>
    public string? OriginalExpression { get; set; }

    /// <summary>
    /// The new snapshot text. Newlines are \n. Empty for
    /// <see cref="InlinePatchMode.Remove"/>, which deletes a call rather than writing one.
    /// </summary>
    public string NewContent { get; set; }

    public InlinePatchMode Mode { get; set; }

    /// <summary>
    /// The same edit, field for field. Lets a reader tell a patch that arrived again unchanged
    /// from one that actually changed, without giving a settable type value equality and the
    /// broken-key hazard that comes with it.
    /// </summary>
    public bool Matches(InlinePatch other) =>
        SourceFile == other.SourceFile &&
        LineHint == other.LineHint &&
        OriginalExpression == other.OriginalExpression &&
        NewContent == other.NewContent &&
        Mode == other.Mode;
}
