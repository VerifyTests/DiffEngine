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
            if (header)
            {
                rows.Add(new($"{bucket} ({bucketEnd - index})", false, null, QueueRowKind.Header));
            }

            var indent = header ? " " : "";
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
                    rows.Add(new($"{indent}{entries[index].TestName} ({groupEnd - index})", false, null, QueueRowKind.Header));
                    for (; index < groupEnd; index++)
                    {
                        // Under a test header the test name would repeat, so the entry falls back
                        // to its call site.
                        rows.Add(EntryRow(entries[index], index, $"{indent} ", entries[index].Name, state));
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
    /// The slice of <see cref="Rows"/> that fits the body: top anchored, so the leading headers
    /// stay visible, until the selection walks below the fold, then shifted to keep the selected
    /// row second from the bottom.
    /// </summary>
    public static IReadOnlyList<QueueItem> Visible(SessionState state, int body)
    {
        var rows = Rows(state);
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

        var top = selected < body
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
    static QueueItem EntryRow(QueueEntry entry, int index, string indent, string text, SessionState state) =>
        new(
            entry.Conflicted ? $"{indent}* {text}" : $"{indent}{text}",
            index == state.Selected,
            entry.Status,
            QueueRowKind.Entry,
            index);

    /// <summary>
    /// One test is one group only within one file: two tests that merely share a name in
    /// different files must not coalesce.
    /// </summary>
    static string? TestGroup(QueueEntry entry) =>
        entry is { Kind: QueueEntryKind.Inline, TestName: not null, Patch: not null }
            ? $"{entry.Patch.SourceFile.ToLowerInvariant()}|{entry.TestName}"
            : null;

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
                labels[index] = WithDirectories(entry.Patch!.SourceFile, baseLabel, depth);
            }
        }

        // Still colliding after three directory levels: two files in one directory sharing a test
        // name. Naming the file always separates them, because one file's repeats coalesce into a
        // test group before labels matter.
        foreach (var index in Collisions(entries, labels))
        {
            var entry = entries[index];
            labels[index] = $"{entry.TestName ?? entry.Name} ({Path.GetFileName(entry.Patch!.SourceFile)})";
        }

        return labels;
    }

    static List<int> Collisions(IReadOnlyList<QueueEntry> entries, string[] labels)
    {
        var collisions = new List<int>();
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index] is not { Kind: QueueEntryKind.Inline, Patch: not null })
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
