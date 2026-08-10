namespace DiffEngine;

/// <summary>
/// One pending inline snapshot, as the thing that owns the queue holds it.
/// <para>
/// The patch plus the outcome of the last attempt to apply it, and nothing else. Everything a
/// reviewer sees — the headers, the two texts, the diff, the "literal not parsed" warning — is
/// derived from the patch, so whoever displays the queue can rebuild all of it and none of it has
/// to be stored or sent.
/// </para>
/// </summary>
/// <param name="Patch">The edit to apply, and the source of everything a reviewer sees.</param>
/// <param name="Status">
/// Null until an accept fails. A failed entry stays queued so it can be retried, for example when
/// an IDE holds the file open, and this is what it failed with.
/// </param>
public sealed record PendingInline(InlinePatch Patch, string? Status = null)
{
    public string Key => InlineKey.For(Patch.SourceFile, Patch.LineHint);

    public string Name => $"{Path.GetFileName(Patch.SourceFile)}:{Patch.LineHint}";
}
