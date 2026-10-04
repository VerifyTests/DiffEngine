/// <summary>
/// The grouped view of the queue: solution buckets when more than one solution is represented,
/// test sub-groups when one test produced more than one change, the files derived from a document
/// beneath it, and a deterministic order that keeps <see cref="SessionState.Queue"/>, tab
/// traversal and the drawn column one list.
/// <para>
/// Everything is encoded in the row labels — headers flush left, entries indented — so all three
/// renderers agree with no per-renderer layout logic, and a queue with one solution and no test
/// metadata renders exactly as it always has.
/// </para>
/// </summary>
static class QueueProjection
{
    /// <summary>
    /// Which entries of a queue are shown beneath another, and beneath which.
    /// <para>
    /// A snapshot library that splits a document into its pages verifies the document and each
    /// page, and when the document changes it reports them all: the document, and every page file
    /// as a pending file of its own. A viewer drawing the document is already showing those
    /// pages, so each of them was a row to open and accept after the one that mattered. An entry
    /// that says what it was derived from (<see cref="QueueEntry.SourceKey"/>) is attached to
    /// that entry instead - no row of its own unless asked for, and accepted or discarded with it.
    /// </para>
    /// <para>
    /// Only where all of this holds, and an entry that is not attached is an ordinary row:
    /// </para>
    /// <list type="bullet">
    /// <item>The source is in the queue. One that was accepted, or settled, or never differed
    /// leaves what was derived from it standing on its own.</item>
    /// <item>The source is a move this viewer shows as a document. A text file with a picture
    /// taken of it is not: its picture is what there is to look at, and hiding it beneath the
    /// text would hide the review.</item>
    /// <item>The source is derived from nothing itself. One level, which is what the sender
    /// says: it names the outermost source that is pending.</item>
    /// <item>The two are in one solution, since a solution's entries are one run of the queue
    /// and a row beneath another has to be in the same run.</item>
    /// </list>
    /// <para>
    /// Decided here and nowhere else, as which rows have headers is, so the order, the rows and
    /// what an accept takes with it cannot disagree about which entries those are.
    /// </para>
    /// </summary>
    sealed class Derivation
    {
        public static readonly Derivation None = new([], []);

        // For each entry, the index of the entry it is shown beneath, or -1. Empty when nothing
        // in the queue is shown beneath anything, which is nearly every queue
        readonly int[] sources;

        // For each entry, how many are shown beneath it
        readonly int[] counts;

        Derivation(int[] sources, int[] counts)
        {
            this.sources = sources;
            this.counts = counts;
        }

        public bool Any => sources.Length > 0;

        public int SourceOf(int index)
        {
            if (index < 0 ||
                index >= sources.Length)
            {
                return -1;
            }

            return sources[index];
        }

        public int CountOf(int index)
        {
            if (index < 0 ||
                index >= counts.Length)
            {
                return 0;
            }

            return counts[index];
        }

        public static Derivation Of(IReadOnlyList<QueueEntry> entries)
        {
            // Asked for every change to the queue and every walk of it, so a queue where nothing
            // names a source - one with no paged documents in it - costs one pass and nothing made
            var named = false;
            foreach (var entry in entries)
            {
                if (entry.SourceKey is not null)
                {
                    named = true;
                    break;
                }
            }

            if (!named)
            {
                return None;
            }

            Dictionary<string, int>? documents = null;
            for (var index = 0; index < entries.Count; index++)
            {
                if (entries[index] is { Kind: QueueEntryKind.Move, SourceKey: null, IsDocument: true } entry)
                {
                    documents ??= new(StringComparer.Ordinal);
                    documents[entry.Key] = index;
                }
            }

            if (documents is null)
            {
                return None;
            }

            int[]? sources = null;
            int[]? counts = null;
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                if (entry.SourceKey is not { } key ||
                    !documents.TryGetValue(key, out var source) ||
                    entries[source].Solution != entry.Solution)
                {
                    continue;
                }

                if (sources is null)
                {
                    sources = new int[entries.Count];
                    Array.Fill(sources, -1);
                    counts = new int[entries.Count];
                }

                sources[index] = source;
                counts![source]++;
            }

            if (sources is null)
            {
                return None;
            }

            return new(sources, counts!);
        }
    }

    /// <summary>
    /// <see cref="Derivation"/> for a queue, worked out once for as long as the queue is that one,
    /// for the reason and in the way <see cref="LabelsOf"/> is.
    /// </summary>
    static Derivation DerivationOf(IReadOnlyList<QueueEntry> entries) =>
        derived.GetValue(entries, derivationOf);

    static readonly ConditionalWeakTable<IReadOnlyList<QueueEntry>, Derivation> derived = new();
    static readonly ConditionalWeakTable<IReadOnlyList<QueueEntry>, Derivation>.CreateValueCallback derivationOf = Derivation.Of;

    /// <summary>
    /// The entries shown beneath the one at <paramref name="index"/>, in queue order: what
    /// accepting or discarding it takes with it. None for an entry nothing is shown beneath,
    /// which is nearly every entry.
    /// </summary>
    public static IReadOnlyList<int> DerivedFrom(IReadOnlyList<QueueEntry> queue, int index)
    {
        var derivation = DerivationOf(queue);
        if (derivation.CountOf(index) == 0)
        {
            return [];
        }

        var indexes = new List<int>(derivation.CountOf(index));
        for (var position = 0; position < queue.Count; position++)
        {
            if (derivation.SourceOf(position) == index)
            {
                indexes.Add(position);
            }
        }

        return indexes;
    }

    /// <summary>
    /// How many entries are shown beneath the one at <paramref name="index"/>, without listing
    /// them: what a row, a button and a menu each say.
    /// </summary>
    public static int DerivedCount(IReadOnlyList<QueueEntry> queue, int index) =>
        DerivationOf(queue).CountOf(index);

    /// <summary>
    /// The entry the one at <paramref name="index"/> is shown beneath, or -1 when it stands alone.
    /// </summary>
    public static int SourceOf(IReadOnlyList<QueueEntry> queue, int index) =>
        DerivationOf(queue).SourceOf(index);

    /// <summary>
    /// Whether any entry of a queue is shown beneath another, and so whether there can be an entry
    /// with no row while nothing is folded.
    /// </summary>
    public static bool AnyDerived(IReadOnlyList<QueueEntry> queue) =>
        DerivationOf(queue).Any;

    /// <summary>
    /// Group-contiguous, deterministic order: solution buckets by first appearance with the
    /// ungrouped bucket last, entries in arrival order within a bucket except that a test's
    /// multiple changes coalesce at its first member's position.
    /// </summary>
    public static IReadOnlyList<QueueEntry> Order(IReadOnlyList<QueueEntry> entries)
    {
        if (entries.Count < 2)
        {
            return entries;
        }

        var buckets = new List<string?>();
        foreach (var entry in entries)
        {
            if (!buckets.Contains(entry.Solution))
            {
                buckets.Add(entry.Solution);
            }
        }

        // The ungrouped bucket trails, the way the tray menu renders a null group last and
        // headerless.
        if (buckets.Remove(null))
        {
            buckets.Add(null);
        }

        // Each entry's group worked out once, and its mates collected in one pass, rather than every
        // entry asking every other one for a key built from two new strings. This runs for every
        // change to the queue, under the lock the render loop takes, and at a few hundred entries
        // the pairwise version was tens of milliseconds and megabytes of garbage each time.
        var groups = new string?[entries.Count];
        var mates = new Dictionary<(string?, string), List<QueueEntry>>();
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (TestGroup(entry) is not { } group)
            {
                continue;
            }

            groups[index] = group;
            var key = (entry.Solution, group);
            if (!mates.TryGetValue(key, out var list))
            {
                list = [];
                mates[key] = list;
            }

            list.Add(entry);
        }

        var result = new List<QueueEntry>(entries.Count);
        var emitted = new HashSet<QueueEntry>(ReferenceEqualityComparer.Instance);
        foreach (var bucket in buckets)
        {
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                if (entry.Solution != bucket ||
                    !emitted.Add(entry))
                {
                    continue;
                }

                result.Add(entry);
                if (groups[index] is not { } group)
                {
                    continue;
                }

                foreach (var mate in mates[(bucket, group)])
                {
                    if (emitted.Add(mate))
                    {
                        result.Add(mate);
                    }
                }
            }
        }

        return BeneathTheirSources(result);
    }

    /// <summary>
    /// An ordered queue with each entry that is shown beneath another put directly after it, which
    /// is where its row goes when it has one. The same list when nothing is shown beneath
    /// anything.
    /// <para>
    /// By name beneath a source, rather than in the order they arrived. A tray's listing is its
    /// dictionary's order, and a page that arrives second is not the second page; by name, a
    /// document's files read page by page whichever process held them, and listing the same
    /// queue twice gives the same list, which is what makes this safe to apply to a list it has
    /// already been applied to.
    /// </para>
    /// </summary>
    static IReadOnlyList<QueueEntry> BeneathTheirSources(List<QueueEntry> ordered)
    {
        var derivation = Derivation.Of(ordered);
        if (!derivation.Any)
        {
            return ordered;
        }

        var beneath = new Dictionary<int, List<QueueEntry>>();
        for (var index = 0; index < ordered.Count; index++)
        {
            var source = derivation.SourceOf(index);
            if (source < 0)
            {
                continue;
            }

            if (!beneath.TryGetValue(source, out var list))
            {
                beneath[source] = list = [];
            }

            list.Add(ordered[index]);
        }

        var result = new List<QueueEntry>(ordered.Count);
        for (var index = 0; index < ordered.Count; index++)
        {
            if (derivation.SourceOf(index) >= 0)
            {
                continue;
            }

            result.Add(ordered[index]);
            if (!beneath.TryGetValue(index, out var list))
            {
                continue;
            }

            list.Sort(byName);
            result.AddRange(list);
        }

        return result;
    }

    // The key as well, so two files of one name - a move and the delete of what it replaces, say -
    // still have one order
    static readonly Comparison<QueueEntry> byName = (left, right) =>
    {
        var names = string.CompareOrdinal(left.Name, right.Name);
        if (names != 0)
        {
            return names;
        }

        return string.CompareOrdinal(left.Key, right.Key);
    };

    /// <summary>
    /// The full row list: headers inserted, labels indented, collisions disambiguated, conflicts
    /// marked. Assumes the queue is already <see cref="Order"/>ed, which every mutation ensures.
    /// </summary>
    public static IReadOnlyList<QueueItem> Rows(SessionState state)
    {
        var slots = Walk(state);
        return Describe(state, slots, 0, slots.Count);
    }

    /// <summary>
    /// Where a row is, before anything is said about it: which entry it is, or which run of
    /// entries its header stands over.
    /// </summary>
    /// <param name="Kind">What the row is.</param>
    /// <param name="Start">The entry, or the first of a header's.</param>
    /// <param name="End">One past the last of a header's entries.</param>
    /// <param name="Indented">Whether the row sits under a solution's header.</param>
    /// <param name="Folded">For a header, whether its entries are hidden.</param>
    readonly record struct Slot(SlotKind Kind, int Start, int End, bool Indented, bool Folded);

    enum SlotKind : byte
    {
        Solution,
        Test,
        Entry,
        // An entry under a test's header
        Member,
        // An entry shown beneath the document it was derived from, while that is unfolded
        Derived
    }

    /// <summary>
    /// The one walk that decides which rows there are: what gets a header, and what a fold hides.
    /// <para>
    /// It says where each row is and nothing about it. A label, a tooltip and a header's members
    /// are a kilobyte and more an entry, and two things ask for the rows without wanting them:
    /// <see cref="VisibleEntries"/>, which is asked on every step through the queue and after
    /// every entry a batch records, and <see cref="Visible"/>, which draws the forty rows that fit
    /// out of however many there are. The same walk for all of them, so what is visible cannot be
    /// decided twice.
    /// </para>
    /// </summary>
    static List<Slot> Walk(SessionState state)
    {
        var entries = state.Queue;
        if (state.Mode == ViewerMode.File ||
            entries.Count == 0)
        {
            return [];
        }

        // The queue is in order, so a second solution is wherever the first one's run ends
        var showSolutions = false;
        for (var index = 1; index < entries.Count; index++)
        {
            if (entries[index].Solution != entries[0].Solution)
            {
                showSolutions = true;
                break;
            }
        }

        // Nothing folded is nearly every queue, and then no header's key needs making to ask
        var folds = state.Collapsed.Count > 0;
        var derivation = DerivationOf(entries);
        var slots = new List<Slot>(entries.Count + 1);
        var position = 0;
        while (position < entries.Count)
        {
            var bucket = entries[position].Solution;
            var bucketEnd = position;
            while (bucketEnd < entries.Count &&
                   entries[bucketEnd].Solution == bucket)
            {
                bucketEnd++;
            }

            var header = showSolutions && bucket is not null;
            if (header)
            {
                var folded = folds && state.Collapsed.Contains(SolutionKey(bucket));
                slots.Add(new(SlotKind.Solution, position, bucketEnd, false, folded));
                if (folded)
                {
                    position = bucketEnd;
                    continue;
                }
            }

            while (position < bucketEnd)
            {
                // An entry shown beneath its source has a row only while the source is unfolded.
                // The other way about from a header, which hides its entries only once folded:
                // these are the rows a reviewer was being made to step through, so hidden is what
                // they are until asked for
                var source = derivation.SourceOf(position);
                if (source >= 0)
                {
                    if (state.Unfolded.Contains(entries[source].Key))
                    {
                        slots.Add(new(SlotKind.Derived, position, position + 1, header, false));
                    }

                    position++;
                    continue;
                }

                var group = TestGroup(entries[position]);
                var groupEnd = position;
                while (group is not null &&
                       groupEnd < bucketEnd &&
                       TestGroup(entries[groupEnd]) == group)
                {
                    groupEnd++;
                }

                if (groupEnd - position >= 2)
                {
                    var folded = folds && state.Collapsed.Contains(TestKey(group));
                    slots.Add(new(SlotKind.Test, position, groupEnd, header, folded));
                    if (folded)
                    {
                        position = groupEnd;
                        continue;
                    }

                    for (; position < groupEnd; position++)
                    {
                        slots.Add(new(SlotKind.Member, position, position + 1, header, false));
                    }

                    continue;
                }

                slots.Add(new(SlotKind.Entry, position, position + 1, header, false));
                position++;
            }
        }

        return slots;
    }

    static string SolutionKey(string? solution) =>
        $"solution|{solution}";

    static string TestKey(string? group) =>
        $"test|{group}";

    /// <summary>
    /// The rows for a run of slots: headers with their counts and members, entries with their
    /// labels, tooltips and marks.
    /// <para>
    /// A row says the same thing whether it is described with every other row or with the few on
    /// screen. What a label grows to tell it from another is decided over the whole queue either
    /// way (<see cref="LabelsOf"/>), and nothing else a row says depends on any row but its own.
    /// </para>
    /// </summary>
    static List<QueueItem> Describe(SessionState state, List<Slot> slots, int from, int to)
    {
        var entries = state.Queue;
        var rows = new List<QueueItem>(Math.Max(0, to - from));
        string[]? labels = null;
        for (var index = from; index < to; index++)
        {
            var slot = slots[index];
            // Two, so an entry sits under its header's text rather than under the header's marker.
            var indent = slot.Indented ? "  " : "";
            var entry = entries[slot.Start];
            switch (slot.Kind)
            {
                case SlotKind.Solution:
                    rows.Add(
                        new($"{Marker(slot.Folded)} {entry.Solution} ({slot.End - slot.Start})", false, null, QueueRowKind.Header)
                        {
                            GroupName = entry.Solution,
                            GroupKey = SolutionKey(entry.Solution),
                            GroupMembers = Enumerable.Range(slot.Start, slot.End - slot.Start).ToList()
                        });
                    break;
                case SlotKind.Test:
                    rows.Add(
                        new($"{indent}{Marker(slot.Folded)} {entry.TestName} ({slot.End - slot.Start})", false, null, QueueRowKind.Header)
                        {
                            GroupName = entry.TestName,
                            GroupKey = TestKey(TestGroup(entry)),
                            GroupMembers = Enumerable.Range(slot.Start, slot.End - slot.Start).ToList()
                        });
                    break;
                case SlotKind.Member:
                    // Under a test header the test name would repeat, so the entry falls back to
                    // its call site — and its tip leaves the name out for the same reason.
                    rows.Add(EntryRow(entry, slot.Start, $"{indent}  ", entry.Name, state, true));
                    break;
                case SlotKind.Derived:
                    // Under its source, whose name it would repeat, so it says what it adds to it
                    rows.Add(EntryRow(
                        entry,
                        slot.Start,
                        $"{indent}  ",
                        DerivedLabel(entries[DerivationOf(entries).SourceOf(slot.Start)], entry),
                        state));
                    break;
                default:
                    labels ??= LabelsOf(entries);
                    var beneath = DerivationOf(entries).CountOf(slot.Start);
                    if (beneath == 0)
                    {
                        rows.Add(EntryRow(entry, slot.Start, indent, labels[slot.Start], state));
                        break;
                    }

                    rows.Add(SourceRow(entry, slot.Start, indent, labels[slot.Start], beneath, state));
                    break;
            }
        }

        return rows;
    }

    /// <summary>
    /// The row of an entry others are shown beneath: marked and counted the way a header is, since
    /// it stands over them as one does, and still an entry, since it is one. A click selects it.
    /// <para>
    /// What is beneath it is hidden until it is unfolded, so the row answers for it: it carries
    /// the failure of a file under it where it has none of its own, or a locked page would be a
    /// failure with nothing on screen saying there was one, and its tip says the count is of
    /// files that go with it.
    /// </para>
    /// </summary>
    static QueueItem SourceRow(QueueEntry entry, int index, string indent, string text, int beneath, SessionState state)
    {
        var folded = !state.Unfolded.Contains(entry.Key);
        var status = entry.Status;
        if (status is null &&
            folded)
        {
            foreach (var derived in DerivedFrom(state.Queue, index))
            {
                if (state.Queue[derived].Status is { } hidden)
                {
                    status = hidden;
                    break;
                }
            }
        }

        var files = beneath == 1
            ? "1 file derived from it is accepted or discarded with it"
            : $"{beneath} files derived from it are accepted or discarded with it";
        var tip = Tooltip(entry, text, false);
        return new(
            $"{indent}{Marker(folded)} {text} ({beneath})",
            index == state.Selected,
            status,
            QueueRowKind.Entry,
            index)
        {
            Tooltip = tip is null ? files : $"{tip}\n{files}"
        };
    }

    /// <summary>
    /// What a file adds to the name of the document it is shown beneath, which is all its row has
    /// to say: <c>#page_0001 (png)</c> under <c>Sample.Test (pdf)</c>. The whole name where it
    /// does not begin with the document's, which is a file a sender derived and named its own way.
    /// </summary>
    static string DerivedLabel(QueueEntry source, QueueEntry entry)
    {
        // Twice, because a verified file carries two extensions, as TrackedEntry reads it
        var stem = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(source.TargetFile));
        if (string.IsNullOrEmpty(stem) ||
            entry.Name.Length <= stem.Length ||
            !entry.Name.StartsWith(stem, StringComparison.Ordinal))
        {
            return entry.Name;
        }

        return entry.Name[stem.Length..].TrimStart();
    }

    /// <summary>
    /// A disclosure marker, in both states. One that appeared only when folded would leave nothing
    /// on screen saying a group can be folded at all.
    /// </summary>
    static string Marker(bool collapsed) =>
        collapsed ? "+" : "-";

    /// <summary>
    /// The entries <see cref="Rows"/> actually emitted, in the order it emitted them.
    /// <para>
    /// Derived from the projection rather than by asking whether each entry's group is folded,
    /// because that second question would have to know that a test group needs two members before
    /// it gets a header and that solution headers only appear once two solutions are in play. Two
    /// implementations of that would drift the first time either rule moved.
    /// </para>
    /// </summary>
    public static List<int> VisibleEntries(SessionState state)
    {
        var visible = new List<int>();
        foreach (var slot in Walk(state))
        {
            if (slot.Kind is SlotKind.Entry or SlotKind.Member or SlotKind.Derived)
            {
                visible.Add(slot.Start);
            }
        }

        return visible;
    }

    /// <summary>
    /// The slice of <see cref="Rows"/> that fits the body: top anchored, so the leading headers
    /// stay visible, until the selection walks below the fold, then shifted to keep the selected
    /// row second from the bottom. <paramref name="top"/> is where the slice starts in the full
    /// projection, which is what maps a full-row anchor — the open menu's — into the slice.
    /// <para>
    /// Sliced before anything is described. A state is another one on every frame of a scroll or
    /// a drag, each of which builds a screen, and describing every row of the queue to draw the
    /// ones that fit was half a millisecond and a megabyte a frame at 2,000 entries.
    /// </para>
    /// </summary>
    public static IReadOnlyList<QueueItem> Visible(SessionState state, int body, out int top)
    {
        var slots = Walk(state);
        top = 0;
        if (slots.Count <= body)
        {
            return Describe(state, slots, 0, slots.Count);
        }

        var selected = 0;
        for (var index = 0; index < slots.Count; index++)
        {
            if (slots[index].Kind is SlotKind.Entry or SlotKind.Member or SlotKind.Derived &&
                slots[index].Start == state.Selected)
            {
                selected = index;
                break;
            }
        }

        top = selected < body
            ? 0
            : Math.Min(selected - (body - 2), slots.Count - body);
        return Describe(state, slots, top, Math.Min(top + body, slots.Count));
    }

    // The conflict marker leads rather than trails, because trailing decorations are the first
    // thing a narrow column truncates away — exactly when the label is long enough to need it.
    static QueueItem EntryRow(
        QueueEntry entry,
        int index,
        string indent,
        string text,
        SessionState state,
        bool underTestHeader = false) =>
        new(
            entry.Conflicted ? $"{indent}* {text}" : $"{indent}{text}",
            index == state.Selected,
            entry.Status,
            QueueRowKind.Entry,
            index)
        {
            Tooltip = Tooltip(entry, text, underTestHeader)
        };

    /// <summary>
    /// What the row cannot say for itself: the whole path behind a bare file name, the test behind
    /// a call site, every framework behind one variant, and the failure behind a <c>!</c>.
    /// <para>
    /// Null when all of that is already on the row. A tip that repeats its label has told the
    /// reader nothing, so on those rows there is no tip at all rather than an empty one.
    /// </para>
    /// <para>
    /// Composed here rather than in each head, so the three of them cannot drift and so the rule
    /// about repeating is decided once. Headers get none: their group is the rows underneath, and
    /// each of those answers for itself. <paramref name="underTestHeader"/> is that rule one row
    /// further out — a header naming the test sits directly above, so the tip does not name it
    /// again.
    /// </para>
    /// </summary>
    static string? Tooltip(QueueEntry entry, string label, bool underTestHeader)
    {
        var lines = new List<string>();
        switch (entry.Kind)
        {
            case QueueEntryKind.Inline when entry.Patch is { } patch:
                lines.Add($"{patch.SourceFile}:{patch.LineHint}");
                if (!underTestHeader &&
                    entry.TestName is { } test)
                {
                    lines.Add(test);
                }

                if (entry.Conflicted)
                {
                    // Every framework in play. The Variant button names only the one on screen, so
                    // which others disagree is otherwise found by cycling through them.
                    lines.Add(string.Join(", ", entry.Variants.SelectMany(_ => _.Origins).Distinct()));
                }

                break;
            case QueueEntryKind.Move when entry.LeftFile is not null && entry.TargetFile is not null:
                lines.Add(entry.LeftFile);
                lines.Add($"to {entry.TargetFile}");
                break;
            case QueueEntryKind.Delete when entry.LeftFile is not null:
                lines.Add(entry.LeftFile);
                break;
        }

        if (entry.Warning is not null)
        {
            lines.Add(entry.Warning);
        }

        if (entry.Status is not null)
        {
            lines.Add(entry.Status);
        }

        lines.RemoveAll(_ => _.Length == 0 || _ == label);
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    /// <summary>
    /// See <see cref="QueueEntry.TestGroup"/>, which works it out once an entry rather than once
    /// an asking.
    /// </summary>
    static string? TestGroup(QueueEntry entry) =>
        entry.TestGroup;

    /// <summary>
    /// The path an entry's label can be grown from: the source file for an inline entry, and the
    /// file a tracked one is about. Tracked entries used to have none, so two verified files with
    /// the same name in two projects of one solution were left showing the same label.
    /// </summary>
    static string? LabelPath(QueueEntry entry) =>
        entry.Kind switch
        {
            QueueEntryKind.Inline => entry.Patch?.SourceFile,
            QueueEntryKind.Move => entry.TargetFile ?? entry.LeftFile,
            QueueEntryKind.Delete => entry.LeftFile,
            _ => null
        };

    /// <summary>
    /// <see cref="Labels"/> for a queue, worked out once for as long as the queue is that one.
    /// <para>
    /// A label depends on every other entry's, so it cannot be had for the rows on screen alone:
    /// the collisions are counted over the whole queue. But a queue is a list that is replaced and
    /// never changed, and a scroll, a drag, a selection or a fold leaves it the list it was, so the
    /// labels are kept against the list itself. Weakly, so they go when it does.
    /// </para>
    /// </summary>
    static string[] LabelsOf(IReadOnlyList<QueueEntry> entries) =>
        labelled.GetValue(entries, labelsOf);

    static readonly ConditionalWeakTable<IReadOnlyList<QueueEntry>, string[]> labelled = new();
    static readonly ConditionalWeakTable<IReadOnlyList<QueueEntry>, string[]>.CreateValueCallback labelsOf = Labels;

    /// <summary>
    /// The label an entry shows when it stands alone: the test name when one is known, else the
    /// call site or the tracked file name. Collisions within a solution — the same file name and
    /// line in two projects, or the same test name in two files — grow the shortest
    /// distinguishing directory prefix, then fall back to naming the file.
    /// </summary>
    static string[] Labels(IReadOnlyList<QueueEntry> entries)
    {
        var labels = new string[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            labels[index] = entry.Kind == QueueEntryKind.Inline
                ? entry.TestName ?? entry.Name
                : entry.Name;
        }

        for (var depth = 1; depth <= 3; depth++)
        {
            var collisions = Collisions(entries, labels);
            if (collisions.Count == 0)
            {
                return labels;
            }

            foreach (var index in collisions)
            {
                var entry = entries[index];
                var baseLabel = entry.TestName ?? entry.Name;
                labels[index] = WithDirectories(LabelPath(entry)!, baseLabel, depth);
            }
        }

        // Still colliding after three directory levels: two files in one directory sharing a test
        // name. Naming the file always separates them, because one file's repeats coalesce into a
        // test group before labels matter.
        foreach (var index in Collisions(entries, labels))
        {
            var entry = entries[index];
            labels[index] = $"{entry.TestName ?? entry.Name} ({Path.GetFileName(LabelPath(entry)!)})";
        }

        return labels;
    }

    /// <summary>
    /// Counted once per label rather than each entry compared with every other, since this runs
    /// every frame over the whole queue.
    /// </summary>
    static List<int> Collisions(IReadOnlyList<QueueEntry> entries, string[] labels)
    {
        var counts = new Dictionary<(string?, string), int>();
        for (var index = 0; index < entries.Count; index++)
        {
            var key = (entries[index].Solution, labels[index]);
            counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        var collisions = new List<int>();
        for (var index = 0; index < entries.Count; index++)
        {
            if (LabelPath(entries[index]) is not null &&
                counts[(entries[index].Solution, labels[index])] > 1)
            {
                collisions.Add(index);
            }
        }

        return collisions;
    }

    static string WithDirectories(string sourceFile, string label, int depth)
    {
        var directory = Path.GetDirectoryName(sourceFile);
        var segments = new List<string>();
        while (depth-- > 0 &&
               !string.IsNullOrEmpty(directory))
        {
            segments.Insert(0, Path.GetFileName(directory));
            directory = Path.GetDirectoryName(directory);
        }

        return segments.Count == 0
            ? label
            : $"{string.Join("/", segments)}/{label}";
    }
}
