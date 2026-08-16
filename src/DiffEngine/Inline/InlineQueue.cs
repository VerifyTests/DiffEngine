namespace DiffEngine;

/// <summary>
/// The pending inline snapshots, and every operation that changes them.
/// <para>
/// One implementation, hosted by whichever process owns the queue: DiffEngineTray when it is
/// running, DiffEngineViewer otherwise. Extracted rather than reimplemented, so the two hosts
/// cannot drift in what accepting or settling means.
/// </para>
/// <para>
/// Immutable. Every operation returns a new queue, which is what lets a host hand one out to a
/// render loop without locking.
/// </para>
/// </summary>
public sealed class InlineQueue
{
    public static readonly InlineQueue Empty = new([]);

    InlineQueue(IReadOnlyList<PendingInline> items) =>
        Items = items;

    /// <summary>
    /// A queue over items a host already holds, used when the pending list is derived from
    /// something else: the viewer's display list, or a listing read back over the socket.
    /// </summary>
    public static InlineQueue From(IEnumerable<PendingInline> items) =>
        new(items.ToList());

    public IReadOnlyList<PendingInline> Items { get; }

    public int Count => Items.Count;

    /// <summary>
    /// Adds an item, or folds the patch into the one with the same key, so a re-run of the same
    /// test updates its entry rather than appending a duplicate.
    /// <para>
    /// Folding is origin aware: the same framework re-running replaces its own content, identical
    /// content from another framework merges into one variant, and different content from another
    /// framework is kept beside the existing variants as a conflict for a reviewer to pick from.
    /// An unlabeled patch cannot be told apart from a re-run, so it replaces the whole entry, which
    /// is also the pre-variant behaviour.
    /// </para>
    /// </summary>
    public InlineQueue Enqueue(InlinePatch patch)
    {
        var key = InlineKey.For(patch.SourceFile, patch.LineHint);
        var items = Items.ToList();
        var existing = items.FindIndex(_ => _.Key == key);
        if (existing >= 0)
        {
            items[existing] = Fold(items[existing], patch);
        }
        else
        {
            items.Add(new(patch));
        }

        return new(items);
    }

    /// <summary>
    /// What a bulk accept reports. Both surfaces say this - the tray out of its own queue, the
    /// viewer out of its session - and each used to build the sentence itself, so a change to the
    /// wording of one left the other saying the old thing. The whole reason the queue was
    /// extracted was to stop the two drifting, and this is part of what they agree on.
    /// </summary>
    internal static string AcceptAllMessage(int accepted, int notWritten, int failed, int conflicted, string? failure)
    {
        var builder = new StringBuilder($"Accepted {accepted}");
        if (notWritten > 0)
        {
            builder.Append($", {notWritten} not written");
        }

        if (failed > 0)
        {
            builder.Append($", {failed} failed");
        }

        if (conflicted > 0)
        {
            builder.Append(conflicted == 1
                ? ", 1 conflict needs review"
                : $", {conflicted} conflicts need review");
        }

        // Only when it speaks for the whole batch. `failure` is whichever entry went wrong last,
        // which is worth saying when it is the only one and misleading when it is one of thirteen -
        // a summary that names a single file reads as the extent of the damage, and it arrives at
        // the length of a paragraph in a bar one line high. Every entry carries its own reason, and
        // that is where a reader with thirteen of them has to look anyway.
        if (failure is not null &&
            notWritten + failed == 1)
        {
            builder.Append($". {failure}");
        }

        return builder.ToString();
    }

    /// <summary>
    /// A fold that added nothing. The variants are handed back as they are, so an accept applying
    /// against this entry still recognises it as the one it started on; only the status of the
    /// last attempt goes, which is what rebuilding the entry used to do anyway - the content
    /// arrived again, and what failed before has not been retried.
    /// </summary>
    static PendingInline Unchanged(PendingInline entry) =>
        entry.Status is null ? entry : entry with { Status = null };

    static PendingInline Fold(PendingInline entry, InlinePatch patch)
    {
        var origin = patch.Framework;
        // An unlabeled arrival cannot be told apart from a re-run, and a labeled arrival into an
        // unlabeled entry cannot be presented as an honest conflict. Both collapse to the newest
        // content winning outright.
        if (origin is null ||
            entry.Variants.All(_ => _.Origins.Count == 0))
        {
            // A re-run that repeats itself has changed nothing, and saying so matters: Accept
            // throws away the completion of an accept whose entry changed identity while the
            // patch was applying, and a still failing test re-sending the same patch is exactly
            // what happens during those ten seconds
            if (entry.Variants is [var only] &&
                only.Origins.Count == 0 &&
                only.Patch.Matches(patch))
            {
                return Unchanged(entry);
            }

            return new(patch);
        }

        var variants = entry.Variants.ToList();

        // The same edit from another framework, or a re-run repeating itself: merge the origin
        // into the variant that already holds this content, and take the label off any variant it
        // previously produced. A re-run that converged is exactly what clears a conflict.
        var matching = variants.FindIndex(_ => _.Patch.Matches(patch));
        if (matching >= 0)
        {
            var folded = new List<InlineVariant>();
            // Nothing to say when this framework already held this content and no other variant
            // gave a label up, which is the shape a re-run repeating itself arrives in
            var changed = false;
            for (var index = 0; index < variants.Count; index++)
            {
                var variant = variants[index];
                if (index == matching)
                {
                    // The existing patch instance, deliberately: the content is identical, and a
                    // reader caching per patch keeps its work.
                    if (variant.Origins.Contains(origin))
                    {
                        folded.Add(variant);
                    }
                    else
                    {
                        folded.Add(variant with { Origins = [.. variant.Origins, origin] });
                        changed = true;
                    }

                    continue;
                }

                var stripped = variant.Origins.Where(_ => _ != origin).ToList();
                if (stripped.Count == 0)
                {
                    changed = true;
                    continue;
                }

                if (stripped.Count == variant.Origins.Count)
                {
                    folded.Add(variant);
                }
                else
                {
                    folded.Add(variant with { Origins = stripped });
                    changed = true;
                }
            }

            return changed ? new(folded) : Unchanged(entry);
        }

        // This framework previously produced different content: its variant updates in place when
        // it was alone on it, or splits off when it had merged with others and now diverges.
        var owning = variants.FindIndex(_ => _.Origins.Contains(origin));
        if (owning >= 0)
        {
            var variant = variants[owning];
            if (variant.Origins.Count == 1)
            {
                variants[owning] = new(patch, [origin]);
            }
            else
            {
                variants[owning] = variant with { Origins = variant.Origins.Where(_ => _ != origin).ToList() };
                variants.Add(new(patch, [origin]));
            }

            return new(variants);
        }

        // A framework this call site has not reported before, with content matching nothing: a
        // genuine conflict.
        variants.Add(new(patch, [origin]));
        return new(variants);
    }

    /// <summary>
    /// Drops the item for a key, used when a previously failing test starts passing. Returns this
    /// same queue when the key is not here, so a caller can tell nothing happened.
    /// </summary>
    public InlineQueue Settle(string key) =>
        Settle(key, null);

    /// <summary>
    /// Origin-scoped settle. A framework that starts passing removes only its own label; a variant
    /// with no labels left is dropped, and the entry goes when its last variant does, so the other
    /// framework's still-failing content stays reviewable. A null origin, or an entry whose
    /// variants are all unlabeled, settles the whole entry.
    /// </summary>
    public InlineQueue Settle(string key, string? origin)
    {
        var items = Items.ToList();
        var index = items.FindIndex(_ => _.Key == key);
        if (index < 0)
        {
            return this;
        }

        var entry = items[index];
        if (origin is null ||
            entry.Variants.All(_ => _.Origins.Count == 0))
        {
            items.RemoveAt(index);
            return new(items);
        }

        var variants = new List<InlineVariant>();
        var changed = false;
        foreach (var variant in entry.Variants)
        {
            if (!variant.Origins.Contains(origin))
            {
                variants.Add(variant);
                continue;
            }

            changed = true;
            var stripped = variant.Origins.Where(_ => _ != origin).ToList();
            if (stripped.Count > 0)
            {
                variants.Add(variant with { Origins = stripped });
            }
        }

        if (!changed)
        {
            return this;
        }

        if (variants.Count == 0)
        {
            items.RemoveAt(index);
            return new(items);
        }

        items[index] = new(variants, entry.Status);
        return new(items);
    }

    public InlineQueue Discard(string key, out string? message)
    {
        var entry = Find(key);
        if (entry is null)
        {
            message = null;
            return this;
        }

        message = $"Discarded {entry.Name}";
        return new(Items.Where(_ => _.Key != key).ToList());
    }

    public InlineQueue DiscardAll(out string message)
    {
        message = $"Discarded {Count}";
        return Empty;
    }

    /// <summary>
    /// Applies one patch. A failure keeps the entry, carrying what went wrong, so it can be
    /// retried once whatever blocked it is out of the way. A conflicted entry is refused without
    /// applying anything: an un-targeted accept has no honest way to pick a side.
    /// </summary>
    public InlineQueue Accept(string key, Func<InlinePatch, InlineApplyResult> apply, out string? message)
    {
        var entry = Find(key);
        if (entry is null)
        {
            message = null;
            return this;
        }

        if (entry.Conflicted)
        {
            message = entry.ConflictRefusal;
            return this;
        }

        return Accept(entry, apply(entry.Patch), out message);
    }

    /// <summary>
    /// Applies the variant a reviewer chose, by one of its origin labels. Accepting any variant
    /// resolves the whole call site — the losing content is dropped, and a framework that still
    /// disagrees will re-report on its next run.
    /// </summary>
    public InlineQueue Accept(string key, string origin, Func<InlinePatch, InlineApplyResult> apply, out string? message)
    {
        var entry = Find(key);
        if (entry is null)
        {
            message = null;
            return this;
        }

        var variant = entry.Variants.FirstOrDefault(_ => _.Origins.Contains(origin));
        if (variant is null)
        {
            message = $"No {origin} variant for {entry.Name}";
            return this;
        }

        return Accept(entry, apply(variant.Patch), out message);
    }

    /// <summary>
    /// The second half of an accept whose patch was applied outside the host's lock. Applying can
    /// wait ten seconds on a cross process mutex, and a host that held its lock for that long
    /// would stall every listing behind one file operation.
    /// <para>
    /// Ignored, returning this same queue, when the entry changed in any way while the patch was
    /// applying — replaced, removed, or grown a variant: the outcome describes an entry that is no
    /// longer here, and says nothing about the one that is.
    /// </para>
    /// </summary>
    public InlineQueue Accept(PendingInline entry, InlineApplyResult result, out string? message)
    {
        var current = Find(entry.Key);
        if (current is null ||
            !ReferenceEquals(current.Variants, entry.Variants))
        {
            message = null;
            return this;
        }

        var (removed, _, outcome) = Outcome(current, result);
        message = outcome;
        var items = Items.ToList();
        var index = items.FindIndex(_ => _.Key == entry.Key);
        if (removed)
        {
            items.RemoveAt(index);
        }
        else
        {
            items[index] = current with { Status = outcome };
        }

        return new(items);
    }

    /// <summary>
    /// Applies every un-conflicted patch. Conflicted entries are skipped and counted into the
    /// message, so a bulk accept never picks sides silently.
    /// </summary>
    public InlineQueue AcceptAll(Func<InlinePatch, InlineApplyResult> apply, out string message) =>
        AcceptAll(
            Items
                .Where(_ => !_.Conflicted)
                .Select(_ => (_, apply(_.Patch)))
                .ToList(),
            out message);

    /// <summary>
    /// The batch counterpart of <see cref="Accept(PendingInline, InlineApplyResult, out string)"/>.
    /// An item with no outcome, or that changed while the batch was applying, was not part of this
    /// accept and is kept untouched rather than counted as a failure — unless it is conflicted,
    /// which is worth counting: it is what a reviewer still has to resolve.
    /// </summary>
    public InlineQueue AcceptAll(
        IReadOnlyList<(PendingInline Entry, InlineApplyResult Result)> outcomes,
        out string message)
    {
        var remaining = new List<PendingInline>();
        var accepted = 0;
        var notWritten = 0;
        var failed = 0;
        var conflicted = 0;
        string? failure = null;
        foreach (var entry in Items)
        {
            var outcome = outcomes.FirstOrDefault(_ => ReferenceEquals(_.Entry.Variants, entry.Variants));
            if (outcome.Entry is null)
            {
                if (entry.Conflicted)
                {
                    conflicted++;
                }

                remaining.Add(entry);
                continue;
            }

            var (removed, stale, text) = Outcome(entry, outcome.Result);
            // Dropped on its own, an entry the reader was watching and got an answer about. Dropped
            // out of a batch of thirty, an entry nobody saw go: no literal written, nothing left in
            // the queue to say so, and a count of accepts that included it. So it stays, carrying
            // what the applier said, the way every other unwritten snapshot in the batch does. A
            // re-run brings the patch back and the arrival clears the status.
            if (stale)
            {
                notWritten++;
                failure = text;
                remaining.Add(entry with { Status = text });
                continue;
            }

            if (removed)
            {
                accepted++;
                continue;
            }

            failed++;
            failure = text;
            remaining.Add(entry with { Status = text });
        }

        message = AcceptAllMessage(accepted, notWritten, failed, conflicted, failure);
        return new(remaining);
    }

    public PendingInline? Find(string key) =>
        Items.FirstOrDefault(_ => _.Key == key);

    /// <summary>
    /// What the applier reported, rather than the cause it used to be read as. "Source changed" is
    /// one reason a call site is not there and it was the only one stated, which left the reader of
    /// a call site that never could host a snapshot - the entry point reached through a helper of
    /// their own - re-running a test forever on the strength of it.
    /// </summary>
    static string StaleMessage(PendingInline entry, InlineApplyResult result) =>
        result.Message is { } reason
            ? $"{entry.Name} not written. {reason}"
            : $"{entry.Name} source changed, re-run the test";

    static (bool removed, bool stale, string message) Outcome(PendingInline entry, InlineApplyResult result) =>
        result.Status switch
        {
            InlineApplyStatus.Applied => (true, false, $"Applied {entry.Name}"),
            InlineApplyStatus.AlreadyApplied => (true, false, $"Already applied {entry.Name}"),
            // The patch is stale. A re-run regenerates a fresh one, so drop it rather than
            // leaving an item that can never succeed.
            InlineApplyStatus.NotFound => (true, true, StaleMessage(entry, result)),
            _ => (false, false, result.Message ?? $"Failed to apply {entry.Name}")
        };
}
