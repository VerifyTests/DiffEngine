/// <summary>
/// The grouped view of the queue: solution buckets when more than one solution is represented,
/// test sub-groups when one test produced more than one change, and a deterministic order that
/// keeps <see cref="SessionState.Queue"/>, tab traversal and the drawn column one list.
/// <para>
/// Everything is encoded in the row labels — headers flush left, entries indented — so all three
/// renderers agree with no per-renderer layout logic, and a queue with one solution and no test
/// metadata renders exactly as it always has.
/// </para>
/// </summary>
static class QueueProjection
{
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

        return result;
    }

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
        Member
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
                default:
                    labels ??= LabelsOf(entries);
                    rows.Add(EntryRow(entry, slot.Start, indent, labels[slot.Start], state));
                    break;
            }
        }

        return rows;
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
            if (slot.Kind is SlotKind.Entry or SlotKind.Member)
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
            if (slots[index].Kind is SlotKind.Entry or SlotKind.Member &&
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
