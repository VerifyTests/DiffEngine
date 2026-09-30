using System.Buffers;

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
            MyersDiff.Diff(ids[..n], ids[n..], changed[..n], changed[n..]);
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
    /// Walks both sides together. Lines neither side changed are matched in order, so the
    /// unchanged lines pair up and everything between them is one changed block.
    /// </summary>
    static List<LineEntry> Walk(ReadOnlySpan<bool> changedExpected, ReadOnlySpan<bool> changedReceived)
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
