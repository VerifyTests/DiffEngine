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

        var result = new List<QueueEntry>(entries.Count);
        var emitted = new HashSet<QueueEntry>(ReferenceEqualityComparer.Instance);
        foreach (var bucket in buckets)
        {
            foreach (var entry in entries)
            {
                if (entry.Solution != bucket ||
                    !emitted.Add(entry))
                {
                    continue;
                }

                result.Add(entry);
                if (TestGroup(entry) is not { } group)
                {
                    continue;
                }

                foreach (var mate in entries)
                {
                    if (mate.Solution == bucket &&
                        TestGroup(mate) == group &&
                        emitted.Add(mate))
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
        var entries = state.Queue;
        if (state.Mode == ViewerMode.File ||
            entries.Count == 0)
        {
            return [];
        }

        var solutions = new List<string?>();
        foreach (var entry in entries)
        {
            if (!solutions.Contains(entry.Solution))
            {
                solutions.Add(entry.Solution);
            }
        }

        var showSolutions = solutions.Count >= 2;
        var labels = Labels(entries);

        var rows = new List<QueueItem>();
        var index = 0;
        while (index < entries.Count)
        {
            var bucket = entries[index].Solution;
            var bucketEnd = index;
            while (bucketEnd < entries.Count &&
                   entries[bucketEnd].Solution == bucket)
            {
                bucketEnd++;
            }

            var header = showSolutions && bucket is not null;
            var bucketKey = $"solution|{bucket}";
            if (header)
            {
                var folded = state.Collapsed.Contains(bucketKey);
                rows.Add(new($"{Marker(folded)} {bucket} ({bucketEnd - index})", false, null, QueueRowKind.Header)
                {
                    GroupName = bucket,
                    GroupKey = bucketKey,
                    GroupMembers = Enumerable.Range(index, bucketEnd - index).ToList()
                });

                if (folded)
                {
                    index = bucketEnd;
                    continue;
                }
            }

            // Two, so an entry sits under its header's text rather than under the header's marker.
            var indent = header ? "  " : "";
            while (index < bucketEnd)
            {
                var group = TestGroup(entries[index]);
                var groupEnd = index;
                while (group is not null &&
                       groupEnd < bucketEnd &&
                       TestGroup(entries[groupEnd]) == group)
                {
                    groupEnd++;
                }

                if (groupEnd - index >= 2)
                {
                    var groupKey = $"test|{group}";
                    var folded = state.Collapsed.Contains(groupKey);
                    rows.Add(new($"{indent}{Marker(folded)} {entries[index].TestName} ({groupEnd - index})", false, null, QueueRowKind.Header)
                    {
                        GroupName = entries[index].TestName,
                        GroupKey = groupKey,
                        GroupMembers = Enumerable.Range(index, groupEnd - index).ToList()
                    });
                    if (folded)
                    {
                        index = groupEnd;
                        continue;
                    }

                    for (; index < groupEnd; index++)
                    {
                        // Under a test header the test name would repeat, so the entry falls back
                        // to its call site — and its tip leaves the name out for the same reason.
                        rows.Add(EntryRow(entries[index], index, $"{indent}  ", entries[index].Name, state, true));
                    }

                    continue;
                }

                rows.Add(EntryRow(entries[index], index, indent, labels[index], state));
                index++;
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
        foreach (var row in Rows(state))
        {
            if (row.EntryIndex >= 0)
            {
                visible.Add(row.EntryIndex);
            }
        }

        return visible;
    }

    /// <summary>
    /// The slice of <see cref="Rows"/> that fits the body: top anchored, so the leading headers
    /// stay visible, until the selection walks below the fold, then shifted to keep the selected
    /// row second from the bottom. <paramref name="top"/> is where the slice starts in the full
    /// projection, which is what maps a full-row anchor — the open menu's — into the slice.
    /// </summary>
    public static IReadOnlyList<QueueItem> Visible(SessionState state, int body, out int top)
    {
        var rows = Rows(state);
        top = 0;
        if (rows.Count <= body)
        {
            return rows;
        }

        var selected = 0;
        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index].Selected)
            {
                selected = index;
                break;
            }
        }

        top = selected < body
            ? 0
            : Math.Min(selected - (body - 2), rows.Count - body);
        var slice = new List<QueueItem>(body);
        for (var index = top; index < top + body && index < rows.Count; index++)
        {
            slice.Add(rows[index]);
        }

        return slice;
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
    /// One test is one group only within one file: two tests that merely share a name in
    /// different files must not coalesce.
    /// </summary>
    static string? TestGroup(QueueEntry entry) =>
        entry is { Kind: QueueEntryKind.Inline, TestName: not null, Patch: not null }
            ? $"{entry.Patch.SourceFile.ToLowerInvariant()}|{entry.TestName}"
            : null;

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

    static List<int> Collisions(IReadOnlyList<QueueEntry> entries, string[] labels)
    {
        var collisions = new List<int>();
        for (var index = 0; index < entries.Count; index++)
        {
            if (LabelPath(entries[index]) is null)
            {
                continue;
            }

            for (var other = 0; other < entries.Count; other++)
            {
                if (other != index &&
                    entries[other].Solution == entries[index].Solution &&
                    labels[other] == labels[index])
                {
                    collisions.Add(index);
                    break;
                }
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
