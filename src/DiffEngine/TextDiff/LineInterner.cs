using System.Buffers;

/// <summary>
/// Gives equal lines equal ids, across both texts, so the diff compares ints rather than strings.
/// <para>
/// An open addressing table over spans of the original strings: a line's id is the index of the
/// first line with the same content, found by hash and confirmed by comparing the characters. A
/// <c>Dictionary&lt;string, int&gt;</c> would need a copy of every line to use as its key, and the
/// span keyed lookup that avoids that only exists from .NET 9.
/// </para>
/// </summary>
sealed class LineInterner(string expected, LineRange[] expectedLines, string received, LineRange[] receivedLines)
{
    public void Intern(Span<int> expectedIds, Span<int> receivedIds)
    {
        var total = expectedLines.Length + receivedLines.Length;
        var capacity = 16;
        while (capacity < total * 2)
        {
            capacity <<= 1;
        }

        // A slot holds the index of the line that claimed it plus one, so zero is free.
        var slots = ArrayPool<int>.Shared.Rent(capacity);
        var hashes = ArrayPool<int>.Shared.Rent(capacity);
        try
        {
            Array.Clear(slots, 0, capacity);
            for (var index = 0; index < expectedIds.Length; index++)
            {
                expectedIds[index] = Find(index, slots, hashes, capacity - 1);
            }

            for (var index = 0; index < receivedIds.Length; index++)
            {
                receivedIds[index] = Find(expectedLines.Length + index, slots, hashes, capacity - 1);
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(slots);
            ArrayPool<int>.Shared.Return(hashes);
        }
    }

    int Find(int index, int[] slots, int[] hashes, int mask)
    {
        var line = Line(index);
        var hash = Hash(line);
        var slot = hash & mask;
        while (true)
        {
            var claimed = slots[slot];
            if (claimed == 0)
            {
                slots[slot] = index + 1;
                hashes[slot] = hash;
                return index;
            }

            if (hashes[slot] == hash &&
                Line(claimed - 1).SequenceEqual(line))
            {
                return claimed - 1;
            }

            slot = (slot + 1) & mask;
        }
    }

    /// <summary>
    /// A line by its index across both texts: the expected lines, then the received ones.
    /// </summary>
    CharSpan Line(int index)
    {
        if (index < expectedLines.Length)
        {
            var range = expectedLines[index];
            return expected.AsSpan(range.Start, range.Length);
        }

        var receivedRange = receivedLines[index - expectedLines.Length];
        return received.AsSpan(receivedRange.Start, receivedRange.Length);
    }

    /// <summary>
    /// FNV-1a over the characters. Ordinal, and the same on every target framework, which
    /// <c>string.GetHashCode(ReadOnlySpan&lt;char&gt;)</c> is not available on.
    /// </summary>
    static int Hash(CharSpan line)
    {
        unchecked
        {
            var hash = 2166136261;
            foreach (var ch in line)
            {
                hash = (hash ^ ch) * 16777619;
            }

            return (int)hash;
        }
    }
}
