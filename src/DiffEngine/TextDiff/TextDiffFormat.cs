namespace DiffEngine;

public enum TextDiffFormat
{
    /// <summary>
    /// Every line, prefixed with <c>+ </c>, <c>- </c> or two spaces.
    /// </summary>
    Full,

    /// <summary>
    /// The changed lines, each unchanged line next to one with its received line number, and
    /// <c>[BOF]</c> or <c>[EOF]</c> where a change touches either end.
    /// </summary>
    Compact,

    /// <summary>
    /// Only the changed lines, prefixed with <c>+ </c> or <c>- </c>.
    /// </summary>
    Minimal
}
