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
    /// Adds an item, or replaces the one with the same key, so a re-run of the same test updates
    /// its entry rather than appending a duplicate.
    /// </summary>
    public InlineQueue Enqueue(InlinePatch patch)
    {
        var entry = new PendingInline(patch);
        var items = Items.ToList();
        var existing = items.FindIndex(_ => _.Key == entry.Key);
        if (existing >= 0)
        {
            items[existing] = entry;
        }
        else
        {
            items.Add(entry);
        }

        return new(items);
    }

    /// <summary>
    /// Drops the item for a key, used when a previously failing test starts passing. Returns this
    /// same queue when the key is not here, so a caller can tell nothing happened.
    /// </summary>
    public InlineQueue Settle(string key)
    {
        var items = Items.Where(_ => _.Key != key).ToList();
        if (items.Count == Items.Count)
        {
            return this;
        }

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
    /// retried once whatever blocked it is out of the way.
    /// </summary>
    public InlineQueue Accept(string key, Func<InlinePatch, InlineApplyResult> apply, out string? message)
    {
        var entry = Find(key);
        if (entry is null)
        {
            message = null;
            return this;
        }

        return Accept(entry, apply(entry.Patch), out message);
    }

    /// <summary>
    /// The second half of an accept whose patch was applied outside the host's lock. Applying can
    /// wait ten seconds on a cross process mutex, and a host that held its lock for that long
    /// would stall every listing behind one file operation.
    /// <para>
    /// Ignored, returning this same queue, when the entry was replaced or removed while the patch
    /// was applying: the outcome describes a patch that is no longer here, and says nothing about
    /// the one that is.
    /// </para>
    /// </summary>
    public InlineQueue Accept(PendingInline entry, InlineApplyResult result, out string? message)
    {
        var current = Find(entry.Key);
        if (current is null ||
            !ReferenceEquals(current.Patch, entry.Patch))
        {
            message = null;
            return this;
        }

        var (removed, outcome) = Outcome(current, result);
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

    public InlineQueue AcceptAll(Func<InlinePatch, InlineApplyResult> apply, out string message) =>
        AcceptAll(Items.Select(_ => (_, apply(_.Patch))).ToList(), out message);

    /// <summary>
    /// The batch counterpart of <see cref="Accept(PendingInline, InlineApplyResult, out string)"/>.
    /// An item with no outcome, or whose patch was replaced while the batch was applying, was not
    /// part of this accept and is kept untouched rather than counted as a failure.
    /// </summary>
    public InlineQueue AcceptAll(
        IReadOnlyList<(PendingInline Entry, InlineApplyResult Result)> outcomes,
        out string message)
    {
        var remaining = new List<PendingInline>();
        var accepted = 0;
        var failed = 0;
        string? failure = null;
        foreach (var entry in Items)
        {
            var outcome = outcomes.FirstOrDefault(_ => ReferenceEquals(_.Entry.Patch, entry.Patch));
            if (outcome.Entry is null)
            {
                remaining.Add(entry);
                continue;
            }

            var (removed, text) = Outcome(entry, outcome.Result);
            if (removed)
            {
                accepted++;
                continue;
            }

            failed++;
            failure = text;
            remaining.Add(entry with { Status = text });
        }

        message = failure is null
            ? $"Accepted {accepted}"
            : $"Accepted {accepted}, {failed} failed. {failure}";
        return new(remaining);
    }

    public PendingInline? Find(string key) =>
        Items.FirstOrDefault(_ => _.Key == key);

    static (bool removed, string message) Outcome(PendingInline entry, InlineApplyResult result) =>
        result.Status switch
        {
            InlineApplyStatus.Applied => (true, $"Applied {entry.Name}"),
            InlineApplyStatus.AlreadyApplied => (true, $"Already applied {entry.Name}"),
            // The patch is stale. A re-run regenerates a fresh one, so drop it rather than
            // leaving an item that can never succeed.
            InlineApplyStatus.NotFound => (true, $"{entry.Name} source changed, re-run the test"),
            _ => (false, result.Message ?? $"Failed to apply {entry.Name}")
        };
}
