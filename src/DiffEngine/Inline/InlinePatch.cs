namespace DiffEngine;

/// <summary>
/// Describes a pending inline-snapshot edit to a C# source file.
/// Mutable POCO so it round-trips through <see cref="InlinePatchFile"/>.
/// </summary>
public sealed class InlinePatch
{
    public InlinePatch()
    {
    }

    public InlinePatch(string sourceFile, int lineHint, string? originalExpression, string newContent)
    {
        SourceFile = sourceFile;
        LineHint = lineHint;
        OriginalExpression = originalExpression;
        NewContent = newContent;
    }

    /// <summary>
    /// Full path to the .cs file.
    /// </summary>
    public string SourceFile { get; set; } = null!;

    /// <summary>
    /// 1 based line of the VerifyInline call. A hint only; content search is the locator.
    /// </summary>
    public int LineHint { get; set; }

    /// <summary>
    /// Verbatim source text of the previous expected argument.
    /// Null when the call had no expected argument (or a bare null argument).
    /// </summary>
    public string? OriginalExpression { get; set; }

    /// <summary>
    /// The new snapshot text. Newlines are \n.
    /// </summary>
    public string NewContent { get; set; } = null!;
}
