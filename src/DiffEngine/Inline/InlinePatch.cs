namespace DiffEngine;

/// <summary>
/// Describes a pending inline-snapshot edit to a source file.
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
    /// Full path to the source file. Its extension decides the language the literal is written in
    /// (<see cref="SourceLanguage.ForFile"/>).
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
    /// Display name of the test that produced this patch, supplied by the caller (Verify). The
    /// viewer labels and groups queue entries by it, falling back to the call site without one.
    /// <para>
    /// Required, though still nullable: a patch that never reaches a queue — an
    /// <see cref="InlinePatchMode.Remove"/>, or an apply straight through
    /// <see cref="InlineApplier"/> — has no reviewable identity and says so with an explicit null.
    /// Omission and decision were previously indistinguishable, and a producer that simply never
    /// set it went unnoticed for as long as it did because the viewer's fallback reads as an
    /// unnamed test rather than as a missing field.
    /// </para>
    /// </summary>
    public required string? TestName { get; set; }

    /// <summary>
    /// Short target framework of the test process that produced this patch ("net9.0", "net48").
    /// Stamped by <see cref="DiffRunner.AddInlineAsync"/> in the sending process, never by a
    /// parser or a re-host, so a patch that crosses processes keeps the framework it was born
    /// under. Null means unknown origin, which selects last-writer-wins queue semantics.
    /// </summary>
    public string? Framework { get; set; }

    /// <summary>
    /// The same edit, field for field. Lets a reader tell a patch that arrived again unchanged
    /// from one that actually changed, without giving a settable type value equality and the
    /// broken-key hazard that comes with it.
    /// <para>
    /// Provenance (<see cref="TestName"/>, <see cref="Framework"/>) is deliberately excluded:
    /// two frameworks producing this identical edit are one edit, which is what lets the queue
    /// merge their origins instead of manufacturing a conflict.
    /// </para>
    /// </summary>
    public bool Matches(InlinePatch other) =>
        SourceFile == other.SourceFile &&
        LineHint == other.LineHint &&
        OriginalExpression == other.OriginalExpression &&
        NewContent == other.NewContent &&
        Mode == other.Mode;
}
