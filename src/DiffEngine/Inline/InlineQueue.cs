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

        var (removed, outcome) = Apply(entry, apply);
        message = outcome;
        var items = Items.ToList();
        var index = items.FindIndex(_ => _.Key == key);
        if (removed)
        {
            items.RemoveAt(index);
        }
        else
        {
            items[index] = entry with { Status = outcome };
        }

        return new(items);
    }

    public InlineQueue AcceptAll(Func<InlinePatch, InlineApplyResult> apply, out string message)
    {
        var remaining = new List<PendingInline>();
        var accepted = 0;
        string? failure = null;
        foreach (var entry in Items)
        {
            var (removed, outcome) = Apply(entry, apply);
            if (removed)
            {
                accepted++;
                continue;
            }

            failure = outcome;
            remaining.Add(entry with { Status = outcome });
        }

        message = failure is null
            ? $"Accepted {accepted}"
            : $"Accepted {accepted}, {remaining.Count} failed. {failure}";
        return new(remaining);
    }

    public PendingInline? Find(string key) =>
        Items.FirstOrDefault(_ => _.Key == key);

    static (bool removed, string message) Apply(PendingInline entry, Func<InlinePatch, InlineApplyResult> apply)
    {
        var result = apply(entry.Patch);
        return result.Status switch
        {
            InlineApplyStatus.Applied => (true, $"Applied {entry.Name}"),
            InlineApplyStatus.AlreadyApplied => (true, $"Already applied {entry.Name}"),
            // The patch is stale. A re-run regenerates a fresh one, so drop it rather than
            // leaving an item that can never succeed.
            InlineApplyStatus.NotFound => (true, $"{entry.Name} source changed, re-run the test"),
            _ => (false, result.Message ?? $"Failed to apply {entry.Name}")
        };
    }
}
