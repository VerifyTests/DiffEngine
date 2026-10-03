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

    /// <summary>
    /// What a queue finds this entry by: <see cref="InlineKey.For" /> of the primary patch's file
    /// and line.
    /// <para>
    /// Built once and kept. Every lookup in a queue asks it of each entry it passes, and building
    /// it is a lowercased copy of the path and a formatted string: with hundreds pending, a run
    /// that enqueues and settles each of them asked hundreds of thousands of times. Kept beside
    /// the file and line it was built from, and built again when the patch no longer holds those,
    /// because a patch's properties can be set and <c>with</c> copies whatever is kept here onto an
    /// entry that may be given other variants.
    /// </para>
    /// </summary>
    public string Key
    {
        get
        {
            var patch = Patch;
            var built = key.Value;
            if (built is null ||
                built.Line != patch.LineHint ||
                !ReferenceEquals(built.SourceFile, patch.SourceFile))
            {
                built = new(patch.SourceFile, patch.LineHint, InlineKey.For(patch.SourceFile, patch.LineHint));
                // One reference, so a reader on another thread sees a whole key or builds its own
                key = new(built);
            }

            return built.Key;
        }
    }

    KeptKey key;

    sealed class BuiltKey(string sourceFile, int line, string key)
    {
        public string SourceFile => sourceFile;
        public int Line => line;
        public string Key => key;
    }

    /// <summary>
    /// The kept key, as a field a record can hold without it counting: the equality a record
    /// generates compares every field, and two entries are not different for one of them having
    /// been asked its key.
    /// </summary>
    readonly struct KeptKey(BuiltKey? value) :
        IEquatable<KeptKey>
    {
        public BuiltKey? Value => value;

        public bool Equals(KeptKey other) => true;

        public override bool Equals(object? other) => other is KeptKey;

        public override int GetHashCode() => 0;
    }

    public string Name => $"{Path.GetFileName(Patch.SourceFile)}:{Patch.LineHint}";
}
