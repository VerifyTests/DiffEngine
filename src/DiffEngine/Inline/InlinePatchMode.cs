namespace DiffEngine;

/// <summary>
/// What an <see cref="InlinePatch"/> does to the source.
/// </summary>
public enum InlinePatchMode
{
    /// <summary>
    /// Set the expected argument of an existing Snapshot call: replace
    /// <see cref="InlinePatch.OriginalExpression"/> when it is set, otherwise insert an argument.
    /// </summary>
    Set,

    /// <summary>
    /// Append a Snapshot call after the verify invocation. Used for a snapshot that has never been
    /// accepted, where there is no Snapshot call to set an argument on yet.
    /// </summary>
    Append,

    /// <summary>
    /// Remove the Snapshot call. Used when inline is switched off and the snapshot migrates back
    /// to a file.
    /// </summary>
    Remove
}
