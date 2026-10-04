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
    /// <para>
    /// The key is the line, and a line stops naming a call site as soon as an accept above it
    /// inserts a literal: the re-run reports every later call site in that file a few lines
    /// further down. Folding by key alone queued each of those a second time beside the entry it
    /// should have updated, and where the run's content had changed, a bulk accept applied the
    /// stale one and then refused the fresh. So a patch whose key names nothing looks for the
    /// entry of its own call site (<see cref="FindMoved" />) and takes that one with it to the
    /// line it is at now, and a patch whose key names another member's entry does not fold into it.
    /// </para>
    /// </summary>
    public InlineQueue Enqueue(InlinePatch patch)
    {
        var key = InlineKey.For(patch.SourceFile, patch.LineHint);
        var items = Items.ToList();
        var atKey = items.FindIndex(_ => _.Key == key);
        if (atKey >= 0 &&
            !items[atKey].Patch.IsAnotherMembers(patch.MemberName))
        {
            items[atKey] = Fold(items[atKey], patch);
            return new(items);
        }

        var moved = FindMoved(items, patch);
        if (moved >= 0)
        {
            // Where the key is free the entry takes it, and is back to being named by the line
            // its call site is on. Where another call site's entry is still sitting there, left
            // behind by the same move, this one keeps the key it has: a queue holds one entry to a
            // key, and both entries are still found by their members, here and when they settle
            items[moved] = atKey < 0
                ? Fold(At(items[moved], patch.LineHint), patch)
                : Fold(items[moved], patch.At(items[moved].Patch.LineHint));
            return new(items);
        }

        if (atKey >= 0)
        {
            // Another member's entry, under a line that is this patch's now. Folding the two
            // presented one call site's snapshot as a variant of another's, or swapped it in under
            // the other's name. Its own call site is elsewhere in the file, and the run that finds
            // it still failing queues it again from there
            items[atKey] = new(patch);
        }
        else
        {
            items.Add(new(patch));
        }

        return new(items);
    }

    /// <summary>
    /// The entry for the call site a patch came from, where that entry is queued under another
    /// line: the call site has moved since.
    /// <para>
    /// Recognised by what a call site keeps when it moves (<see cref="InlinePatch.IsSameCallSite" />),
    /// and only in an entry this patch's framework already has content in. A test stops at the
    /// first verification that fails, so a framework reporting a call site has nothing else
    /// failing in that member: an entry of its own there under another line is this call site
    /// before it moved, or an earlier one it has since passed, which is no loss either. Another
    /// framework's entry is not held to that. It can have stopped at an earlier call in the same
    /// test, one this framework passed, and two calls holding the same literal read alike.
    /// </para>
    /// <para>
    /// And only when exactly one entry answers to it. With more than one, nothing here says which
    /// of them moved, and folding into the wrong one replaces a snapshot that is still pending,
    /// so the patch is queued beside them, as it always was.
    /// </para>
    /// <para>
    /// What this gives up is a test that carries on past a failed verification and has two call
    /// sites nothing but the line tells apart, both new or both holding the same literal: the
    /// second is taken for the first one moved, and the queue holds whichever reported last.
    /// </para>
    /// </summary>
    static int FindMoved(List<PendingInline> items, InlinePatch patch)
    {
        var found = -1;
        for (var index = 0; index < items.Count; index++)
        {
            var entry = items[index];
            if (!entry.Patch.IsSameCallSite(patch) ||
                !HasOrigin(entry, patch.Framework))
            {
                continue;
            }

            if (found >= 0)
            {
                return -1;
            }

            found = index;
        }

        return found;
    }

    /// <summary>
    /// Whether an entry already holds content from <paramref name="origin" />. An unlabeled entry
    /// or an unlabeled arrival counts, on the reasoning <see cref="Fold" /> gives: neither can be
    /// told apart from a re-run.
    /// </summary>
    static bool HasOrigin(PendingInline entry, string? origin) =>
        origin is null ||
        entry.Variants.All(_ => _.Origins.Count == 0) ||
        entry.Variants.Any(_ => _.Origins.Contains(origin));

    /// <summary>
    /// An entry at another line. Every variant goes, since they are one call site, and as copies:
    /// see <see cref="InlinePatch.At" />.
    /// </summary>
    static PendingInline At(PendingInline entry, int line) =>
        new(entry.Variants.Select(_ => _ with { Patch = _.Patch.At(line) }).ToList(), entry.Status);

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
            if (entry.Variants is [{Origins.Count: 0} only] &&
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

    public InlineQueue Settle(string key, string? origin) =>
        Settle(key, origin, null);

    // The 20.4.0 signature, kept for binary compatibility with callers compiled against it.
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public InlineQueue Settle(string key, string? origin, string? member) =>
        Settle(key, origin, member, null);

    /// <summary>
    /// Origin-scoped settle. A framework that starts passing removes only its own label; a variant
    /// with no labels left is dropped, and the entry goes when its last variant does, so the other
    /// framework's still-failing content stays reviewable. A null origin, or an entry whose
    /// variants are all unlabeled, settles the whole entry.
    /// </summary>
    /// <param name="key">The entry's key, naming its source file and recorded line.</param>
    /// <param name="origin">The framework moniker of the run that started passing.</param>
    /// <param name="member">
    /// The member the settled call site sits in. It finds the entry when <paramref name="key" />
    /// matches nothing (<see cref="FindByMember" />), and it is what says the entry the key does
    /// match belongs to some other call site.
    /// </param>
    /// <param name="value">
    /// What the settling call's expected argument holds, which narrows <paramref name="member" />
    /// to the entry it settles. See <see cref="FindByMember" />.
    /// </param>
    public InlineQueue Settle(string key, string? origin, string? member, string? value = null)
    {
        var items = Items.ToList();
        var index = items.FindIndex(_ => _.Key == key);
        // The entry under the key is not always the settling call's. After an accept higher in
        // the file a passing call sits on a line a later call site was queued under, and its
        // settle took that entry, a snapshot still failing and then pending nowhere. An entry
        // from another member is another call site's, unless the value says otherwise: a test
        // renamed since it was queued passes with the content its entry was waiting to become
        if (index >= 0 &&
            items[index].Patch.IsAnotherMembers(member) &&
            !IsSettledBy(items[index], value))
        {
            index = -1;
        }

        if (index < 0)
        {
            index = FindByMember(items, key, member, value);
        }

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

    /// <summary>
    /// The entry a settle was for when its key names no entry, found by the member instead.
    /// </summary>
    /// <remarks>
    /// A key names a line, and a line stops being true the moment an accept inserts a literal
    /// above it — a snapshot is several lines of source, so accepting one call site moves every
    /// later one in the file. The entries left behind could then never be settled by any later
    /// run: the run reports the line as it is now, and the entry still holds the line it was
    /// queued at. The member survives that, which is why a patch carries it and why the patcher
    /// already locates call sites by it.
    /// <para>
    /// Only when the member names exactly one entry in that file. A member holding several inline
    /// snapshots cannot say which of them a settle was for, and dropping the wrong one loses a
    /// pending snapshot outright — so an ambiguous member settles nothing, leaving the entries as
    /// they were.
    /// </para>
    /// <para>
    /// With a <paramref name="value" />, only an entry that value settles is a candidate
    /// (<see cref="InlinePatch.IsSettledBy" />). A member with one entry is not one call site: the
    /// sibling beside a failing call can pass, and its settle took the failing one's entry. Without
    /// one - a producer that predates it - the member alone still decides, as it did.
    /// </para>
    /// </remarks>
    static int FindByMember(List<PendingInline> items, string key, string? member, string? value)
    {
        if (string.IsNullOrEmpty(member))
        {
            return -1;
        }

        var file = FileOf(key);
        var found = -1;
        for (var index = 0; index < items.Count; index++)
        {
            var entry = items[index];
            if (entry.Patch.MemberName != member ||
                FileOf(entry.Key) != file)
            {
                continue;
            }

            if (value is not null &&
                !IsSettledBy(entry, value))
            {
                continue;
            }

            if (found >= 0)
            {
                return -1;
            }

            found = index;
        }

        return found;
    }

    /// <summary>
    /// Whether a passing call holding <paramref name="value" /> settles any of an entry's
    /// variants. Never with no value, which is a producer that sends none.
    /// </summary>
    static bool IsSettledBy(PendingInline entry, string? value) =>
        value is not null &&
        entry.Variants.Any(_ => _.Patch.IsSettledBy(value));

    /// <summary>
    /// The file half of a key. Taken off the key rather than off the patch, so both sides are
    /// case folded the way <see cref="InlineKey.For" /> folds them for this platform.
    /// </summary>
    static string FileOf(string key)
    {
        var separator = key.LastIndexOf('|');
        return separator < 0 ? key : key[..separator];
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
    /// Applies every un-conflicted patch, handing them to <paramref name="apply"/> together.
    /// <para>
    /// For an applier that does better with the whole batch than with a patch at a time, which
    /// <see cref="InlineApplier.ApplyAll(IReadOnlyList{InlinePatch})"/> does: it reads and writes a
    /// source file once for all the snapshots in it, where the overload above has no choice but
    /// to rewrite the file for each. What comes back is one result for each patch handed over, in
    /// the same order.
    /// </para>
    /// </summary>
    public InlineQueue AcceptAll(
        Func<IReadOnlyList<InlinePatch>, IReadOnlyList<InlineApplyResult>> apply,
        out string message)
    {
        var entries = Items
            .Where(_ => !_.Conflicted)
            .ToList();
        var results = apply(entries.Select(_ => _.Patch).ToList());
        if (results.Count != entries.Count)
        {
            throw new ArgumentException($"{entries.Count} patches were handed over and {results.Count} results came back.", nameof(apply));
        }

        var outcomes = new List<(PendingInline Entry, InlineApplyResult Result)>(entries.Count);
        for (var index = 0; index < entries.Count; index++)
        {
            outcomes.Add((entries[index], results[index]));
        }

        return AcceptAll(outcomes, out message);
    }

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
        var queue = this;
        var tally = new AcceptAllTally();
        foreach (var (entry, result) in outcomes)
        {
            queue = queue.AcceptInBatch(entry, result, ref tally);
        }

        message = tally.Message(queue.Conflicts);
        return queue;
    }

    /// <summary>
    /// One entry of a bulk accept, completed on its own rather than with the rest of the batch.
    /// <para>
    /// A host applying a long queue commits each outcome as it arrives, so whoever is watching the
    /// queue sees it shrink as the batch goes, and a listing taken partway through says how far it
    /// has got. Completing everything at the end left a window showing an untouched queue for as
    /// long as the batch took, and then emptying all at once.
    /// </para>
    /// <para>
    /// The rules are the batch's rather than a single accept's, because the batch completion above
    /// is this, once per outcome. An entry that changed while its patch was applying is left alone
    /// and not counted, found by its variants the way the two phase accept finds it.
    /// </para>
    /// </summary>
    internal InlineQueue AcceptInBatch(PendingInline entry, InlineApplyResult result, ref AcceptAllTally tally)
    {
        var items = Items.ToList();
        var index = items.FindIndex(_ => ReferenceEquals(_.Variants, entry.Variants));
        if (index < 0)
        {
            return this;
        }

        var current = items[index];
        var (removed, stale, text) = Outcome(current, result);
        // Dropped on its own, an entry the reader was watching and got an answer about. Dropped
        // out of a batch of thirty, an entry nobody saw go: no literal written, nothing left in
        // the queue to say so, and a count of accepts that included it. So it stays, carrying
        // what the applier said, the way every other unwritten snapshot in the batch does. A
        // re-run brings the patch back and the arrival clears the status.
        if (stale)
        {
            tally = tally with
            {
                NotWritten = tally.NotWritten + 1,
                Failure = text
            };
            items[index] = current with { Status = text };
        }
        else if (removed)
        {
            tally = tally with { Accepted = tally.Accepted + 1 };
            items.RemoveAt(index);
        }
        else
        {
            tally = tally with
            {
                Failed = tally.Failed + 1,
                Failure = text
            };
            items[index] = current with { Status = text };
        }

        return new(items);
    }

    /// <summary>
    /// The queue with every entry of <paramref name="sourceFile"/> taken to the line its call
    /// site is on once the edits <paramref name="results"/> report have been made, in the order
    /// they were made. This same queue when nothing moved.
    /// <para>
    /// A key is a line, and accepting a snapshot moves every call site under it. The entries
    /// left behind were then named by lines that had become other calls', and everything that
    /// finds an entry by its key had to ask whose it was before believing it: a re-run folded
    /// into another call site's entry or queued a second one beside its own, a settle took an
    /// entry that was still failing, and the patcher was handed a line that named the wrong
    /// call. Those questions are still asked, for an edit made by hand or by another process,
    /// which nothing reports. For an accept made here the lines are simply kept right.
    /// </para>
    /// <para>
    /// A step of its own, after the outcomes are recorded and never part of recording one: a
    /// batch finds what it claimed by its variants (<see cref="AcceptInBatch"/>), and an entry
    /// that has moved is another list of them. So whoever accepts calls this once the patches it
    /// applied to a file have all been answered for.
    /// </para>
    /// <para>
    /// An entry whose new line is another entry's stays where it is: a queue holds one entry to
    /// a key, and the hint was wrong before the edit for that to happen.
    /// </para>
    /// </summary>
    internal InlineQueue Rebased(string sourceFile, IReadOnlyList<InlineApplyResult> results)
    {
        if (!results.Any(_ => _.MovedBy != 0))
        {
            return this;
        }

        // The lines of this file that are spoken for: an entry that stays, and each that moves
        // once it has
        HashSet<int>? taken = null;
        List<(int Index, int Line)>? moving = null;
        for (var index = 0; index < Items.Count; index++)
        {
            var patch = Items[index].Patch;
            if (!InlineKey.SamePath(patch.SourceFile, sourceFile))
            {
                continue;
            }

            var line = patch.LineHint;
            foreach (var result in results)
            {
                line = result.Rebase(line);
            }

            taken ??= [];
            if (line == patch.LineHint)
            {
                taken.Add(line);
            }
            else
            {
                moving ??= [];
                moving.Add((index, line));
            }
        }

        if (moving is null)
        {
            return this;
        }

        var items = Items.ToList();
        var changed = false;
        foreach (var (index, line) in moving)
        {
            if (!taken!.Add(line))
            {
                taken.Add(items[index].Patch.LineHint);
                continue;
            }

            items[index] = At(items[index], line);
            changed = true;
        }

        return changed ? new(items) : this;
    }

    /// <summary>
    /// What a bulk accept that has finished counts as still needing review: it never applies an
    /// entry with more than one variant, so whatever the queue holds of those is left for a
    /// reviewer to pick from.
    /// </summary>
    internal int Conflicts =>
        Items.Count(_ => _.Conflicted);

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
