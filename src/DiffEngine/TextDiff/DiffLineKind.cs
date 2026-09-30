namespace DiffEngine;

public enum DiffLineKind
{
    /// <summary>
    /// On both sides.
    /// </summary>
    Unchanged,

    /// <summary>
    /// Only in the expected text.
    /// </summary>
    Removed,

    /// <summary>
    /// Only in the received text.
    /// </summary>
    Added
}
