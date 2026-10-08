using System.Buffers;

namespace DiffEngine;

/// <summary>
/// The diff of two texts as line ranges of the originals, in inline order: unchanged lines, and
/// each changed block as its removed lines followed by its added lines.
/// <para>
/// What <see cref="TextDiff.Compute"/> and <see cref="TextDiff.Format"/> share. Nothing here
/// copies a line: <see cref="TextDiff.Compute"/> makes a string of each line it returns, and
/// <see cref="TextDiff.Format"/> appends each line's span straight to its output.
/// </para>
/// <para>
/// Lines compare exactly, whitespace and case included. A snapshot that fails on indentation or
/// a trailing space has to show the line that changed, or the reader is shown a failure with no
/// visible difference.
/// </para>
/// </summary>
sealed class LineDiff
{
    public string Expected { get; }
    public string Received { get; }
    public LineRange[] ExpectedLines { get; }
    public LineRange[] ReceivedLines { get; }
    public List<LineEntry> Entries { get; }

    LineDiff(string expected, string received, LineRange[] expectedLines, LineRange[] receivedLines, List<LineEntry> entries)
    {
        Expected = expected;
        Received = received;
        ExpectedLines = expectedLines;
        ReceivedLines = receivedLines;
        Entries = entries;
    }

    /// <summary>
    /// The text of an entry. Unchanged lines are the same on both sides, and are read from the
    /// received one.
    /// </summary>
    public CharSpan Text(LineEntry entry)
    {
        if (entry.Kind == DiffLineKind.Removed)
        {
            var expectedRange = ExpectedLines[entry.Expected];
            return Expected.AsSpan(expectedRange.Start, expectedRange.Length);
        }

        var range = ReceivedLines[entry.Received];
        return Received.AsSpan(range.Start, range.Length);
    }

    public static LineDiff Build(string expected, string received)
    {
        var expectedLines = LineSplitter.Split(expected);
        var receivedLines = LineSplitter.Split(received);
        var n = expectedLines.Length;
        var total = n + receivedLines.Length;

        int[]? rentedIds = null;
        bool[]? rentedChanged = null;
        var ids = total <= MyersDiff.StackLimit
            ? stackalloc int[total]
            : (rentedIds = ArrayPool<int>.Shared.Rent(total)).AsSpan(0, total);
        var changed = total <= MyersDiff.StackLimit
            ? stackalloc bool[total]
            : (rentedChanged = ArrayPool<bool>.Shared.Rent(total)).AsSpan(0, total);
        try
        {
            changed.Clear();
            new LineInterner(expected, expectedLines, received, receivedLines).Intern(ids[..n], ids[n..]);
            DiffShared(ids[..n], ids[n..], changed[..n], changed[n..]);
            var entries = Walk(changed[..n], changed[n..]);
            return new(expected, received, expectedLines, receivedLines, entries);
        }
        finally
        {
            if (rentedIds != null)
            {
                ArrayPool<int>.Shared.Return(rentedIds);
            }

            if (rentedChanged != null)
            {
                ArrayPool<bool>.Shared.Return(rentedChanged);
            }
        }
    }

    /// <summary>
    /// Diffs the lines both sides have, having marked every other line as changed without asking.
    /// <para>
    /// A line only one side has cannot be unchanged, so it is no part of the question Myers
    /// answers, and leaving it in was what made the answer expensive. Myers costs by the number of
    /// edits, and two texts with no line in common are edits and nothing else: 40,000 lines a side
    /// took four seconds. That is not a rare input. A serializer setting that changes the
    /// indentation makes it out of any large snapshot. With those lines taken out first there is
    /// nothing left to search, and the same goes for the usual failure, a few lines that differ in
    /// a text that otherwise matches.
    /// </para>
    /// <para>
    /// The result is no worse for it. The longest run of lines the two sides share is the same
    /// with or without the lines that could never be in it, so as many lines are unchanged as
    /// before.
    /// </para>
    /// <para>
    /// Which lines those are is already in the ids. <see cref="LineInterner"/> gives a line the
    /// index of the first line with its content, counting the expected lines first, so a received
    /// line whose id is past the expected lines is in none of them, and one pass over the received
    /// ids says which expected lines are in neither.
    /// </para>
    /// <para>
    /// The ids are compacted where they are, since they are this diff's own and are not read
    /// again.
    /// </para>
    /// </summary>
    static void DiffShared(Span<int> expectedIds, Span<int> receivedIds, Span<bool> changedExpected, Span<bool> changedReceived)
    {
        var n = expectedIds.Length;
        bool[]? rentedShared = null;
        var shared = n <= MyersDiff.StackLimit
            ? stackalloc bool[n]
            : (rentedShared = ArrayPool<bool>.Shared.Rent(n)).AsSpan(0, n);
        shared.Clear();
        var keptReceived = 0;
        foreach (var id in receivedIds)
        {
            if (id < n)
            {
                shared[id] = true;
                keptReceived++;
            }
        }

        var keptExpected = 0;
        foreach (var id in expectedIds)
        {
            if (shared[id])
            {
                keptExpected++;
            }
        }

        var kept = keptExpected + keptReceived;
        int[]? rentedOrigins = null;
        bool[]? rentedFlags = null;
        // Where each line that is kept came from, the expected ones and then the received.
        var origins = kept <= MyersDiff.StackLimit
            ? stackalloc int[kept]
            : (rentedOrigins = ArrayPool<int>.Shared.Rent(kept)).AsSpan(0, kept);
        var flags = kept <= MyersDiff.StackLimit
            ? stackalloc bool[kept]
            : (rentedFlags = ArrayPool<bool>.Shared.Rent(kept)).AsSpan(0, kept);
        try
        {
            flags.Clear();
            var next = 0;
            for (var index = 0; index < n; index++)
            {
                var id = expectedIds[index];
                if (shared[id])
                {
                    expectedIds[next] = id;
                    origins[next] = index;
                    next++;
                }
                else
                {
                    changedExpected[index] = true;
                }
            }

            next = 0;
            for (var index = 0; index < receivedIds.Length; index++)
            {
                var id = receivedIds[index];
                if (id < n)
                {
                    receivedIds[next] = id;
                    origins[keptExpected + next] = index;
                    next++;
                }
                else
                {
                    changedReceived[index] = true;
                }
            }

            MyersDiff.Diff(
                expectedIds[..keptExpected],
                receivedIds[..keptReceived],
                flags[..keptExpected],
                flags[keptExpected..]);

            for (var index = 0; index < keptExpected; index++)
            {
                if (flags[index])
                {
                    changedExpected[origins[index]] = true;
                }
            }

            for (var index = keptExpected; index < kept; index++)
            {
                if (flags[index])
                {
                    changedReceived[origins[index]] = true;
                }
            }
        }
        finally
        {
            if (rentedShared != null)
            {
                ArrayPool<bool>.Shared.Return(rentedShared);
            }

            if (rentedOrigins != null)
            {
                ArrayPool<int>.Shared.Return(rentedOrigins);
            }

            if (rentedFlags != null)
            {
                ArrayPool<bool>.Shared.Return(rentedFlags);
            }
        }
    }

    /// <summary>
    /// Walks both sides together. Lines neither side changed are matched in order, so the
    /// unchanged lines pair up and everything between them is one changed block.
    /// <para>
    /// Sides that disagree on how many lines are unchanged would leave the walk unable to move, so
    /// that throws rather than looping forever. Myers never produces it, and internal only so the
    /// guard can be tested.
    /// </para>
    /// </summary>
    internal static List<LineEntry> Walk(ReadOnlySpan<bool> changedExpected, ReadOnlySpan<bool> changedReceived)
    {
        var entries = new List<LineEntry>(Math.Max(changedExpected.Length, changedReceived.Length));
        var expected = 0;
        var received = 0;
        while (expected < changedExpected.Length ||
               received < changedReceived.Length)
        {
            if (expected < changedExpected.Length &&
                received < changedReceived.Length &&
                !changedExpected[expected] &&
                !changedReceived[received])
            {
                entries.Add(new(DiffLineKind.Unchanged, expected, received));
                expected++;
                received++;
                continue;
            }

            var before = expected + received;
            while (expected < changedExpected.Length &&
                   changedExpected[expected])
            {
                entries.Add(new(DiffLineKind.Removed, expected, -1));
                expected++;
            }

            while (received < changedReceived.Length &&
                   changedReceived[received])
            {
                entries.Add(new(DiffLineKind.Added, -1, received));
                received++;
            }

            if (expected + received == before)
            {
                throw new InvalidOperationException("The two sides of the diff do not have the same number of unchanged lines.");
            }
        }

        return entries;
    }
}
