namespace DiffEngine;

/// <summary>
/// One pending inline snapshot, as the thing that owns the queue holds it.
/// <para>
/// The variants plus the outcome of the last attempt to apply one, and nothing else. Everything a
/// reviewer sees — the headers, the two texts, the diff, the "literal not parsed" warning — is
/// derived from the patches, so whoever displays the queue can rebuild all of it and none of it
/// has to be stored or sent.
/// </para>
/// <para>
/// Usually one variant. A multi-targeted test run that produces different content per framework
/// holds one variant per distinct content, each labeled with the frameworks that produced it, and
/// the entry is <see cref="Conflicted"/> until a reviewer picks one or a re-run converges.
/// </para>
/// </summary>
public sealed record PendingInline
{
    public PendingInline(InlinePatch patch, string? status = null)
        : this([new(patch, patch.Framework is null ? [] : [patch.Framework])], status)
    {
    }

    public PendingInline(IReadOnlyList<InlineVariant> variants, string? status = null)
    {
        if (variants.Count == 0)
        {
            throw new ArgumentException("A pending snapshot holds at least one variant.", nameof(variants));
        }

        Variants = variants;
        Status = status;
    }

    /// <summary>
    /// Every distinct content for this call site, in arrival order. All variants describe the
    /// same file and line; they differ only in content and in who produced it.
    /// </summary>
    public IReadOnlyList<InlineVariant> Variants { get; init; }

    /// <summary>
    /// Null until an accept fails. A failed entry stays queued so it can be retried, for example
    /// when an IDE holds the file open, and this is what it failed with.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// The primary variant's patch: the first arrival, which keeps the display stable when a
    /// second framework's result lands while the first is being read.
    /// </summary>
    public InlinePatch Patch => Variants[0].Patch;

    /// <summary>
    /// More than one distinct content for this call site. Distinctness is enforced by
    /// <see cref="InlineQueue.Enqueue"/> — identical content merges — so this is just a count.
    /// </summary>
    public bool Conflicted => Variants.Count > 1;

    /// <summary>
    /// Every origin label across the variants, in variant order: "net8.0 / net9.0".
    /// </summary>
    public string OriginsLabel => string.Join(" / ", Variants.SelectMany(_ => _.Origins));

    // The two places a conflict is worded — a refused accept and a listing status — live here so
    // the tray and the viewer cannot phrase them differently.
    internal string ConflictRefusal => $"Conflicting snapshots ({OriginsLabel}), resolve in the viewer";

    internal string ConflictStatus => $"Conflicting snapshots ({OriginsLabel})";

    public string Key => InlineKey.For(Patch.SourceFile, Patch.LineHint);

    public string Name => $"{Path.GetFileName(Patch.SourceFile)}:{Patch.LineHint}";
}
