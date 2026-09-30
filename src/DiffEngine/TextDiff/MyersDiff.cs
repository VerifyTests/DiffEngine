using System.Buffers;

/// <summary>
/// Myers' O((N+M)D) difference algorithm, in its linear space form: find the middle snake of an
/// optimal edit path by searching from both ends at once, then solve the halves either side of it.
/// Only which elements changed is recorded, one flag per element per side; what did not change is
/// matched in order, which is all a line diff needs to walk the two sides together.
/// <para>
/// Written over spans, so each recursion is a slice of the same id and flag buffers rather than a
/// copy, and the two diagonal vectors are allocated once for the whole diff.
/// </para>
/// </summary>
static class MyersDiff
{
    /// <summary>
    /// Buffers up to this many elements go on the stack rather than to the pool.
    /// </summary>
    public const int StackLimit = 256;

    public static void Diff(ReadOnlySpan<int> a, ReadOnlySpan<int> b, Span<bool> changedA, Span<bool> changedB)
    {
        var length = VectorLength(a.Length, b.Length);
        int[]? rentedForward = null;
        int[]? rentedReverse = null;
        var forward = length <= StackLimit
            ? stackalloc int[length]
            : (rentedForward = ArrayPool<int>.Shared.Rent(length)).AsSpan(0, length);
        var reverse = length <= StackLimit
            ? stackalloc int[length]
            : (rentedReverse = ArrayPool<int>.Shared.Rent(length)).AsSpan(0, length);
        try
        {
            Recurse(a, b, changedA, changedB, forward, reverse);
        }
        finally
        {
            if (rentedForward != null)
            {
                ArrayPool<int>.Shared.Return(rentedForward);
            }

            if (rentedReverse != null)
            {
                ArrayPool<int>.Shared.Return(rentedReverse);
            }
        }
    }

    static int VectorLength(int n, int m) =>
        2 * ((n + m + 1) / 2) + 2;

    static void Recurse(
        ReadOnlySpan<int> a,
        ReadOnlySpan<int> b,
        Span<bool> changedA,
        Span<bool> changedB,
        Span<int> forward,
        Span<int> reverse)
    {
        var prefix = 0;
        while (prefix < a.Length &&
               prefix < b.Length &&
               a[prefix] == b[prefix])
        {
            prefix++;
        }

        a = a[prefix..];
        b = b[prefix..];
        changedA = changedA[prefix..];
        changedB = changedB[prefix..];

        var suffix = 0;
        while (suffix < a.Length &&
               suffix < b.Length &&
               a[a.Length - 1 - suffix] == b[b.Length - 1 - suffix])
        {
            suffix++;
        }

        a = a[..^suffix];
        b = b[..^suffix];
        changedA = changedA[..^suffix];
        changedB = changedB[..^suffix];

        if (a.Length == 0)
        {
            changedB.Fill(true);
            return;
        }

        if (b.Length == 0)
        {
            changedA.Fill(true);
            return;
        }

        if (!TryMiddleSnake(a, b, forward, reverse, out var x, out var y))
        {
            changedA.Fill(true);
            changedB.Fill(true);
            return;
        }

        Recurse(a[..x], b[..y], changedA[..x], changedB[..y], forward, reverse);
        Recurse(a[x..], b[y..], changedA[x..], changedB[y..], forward, reverse);
    }

    /// <summary>
    /// Searches forward from the start and backward from the end, one edit at a time, until the
    /// two paths overlap. The point where they meet lies on an optimal path, and splitting there
    /// leaves two problems each with about half the edits.
    /// <para>
    /// <paramref name="forward"/> holds, per diagonal k = x - y, the furthest x reached from the
    /// start. <paramref name="reverse"/> holds the same measured from the end. Which search checks
    /// for the overlap depends on the parity of the length difference, because only then can the
    /// two land on the same diagonal after the same number of steps.
    /// </para>
    /// </summary>
    static bool TryMiddleSnake(
        ReadOnlySpan<int> a,
        ReadOnlySpan<int> b,
        Span<int> forward,
        Span<int> reverse,
        out int splitX,
        out int splitY)
    {
        var n = a.Length;
        var m = b.Length;
        var maxD = (n + m + 1) / 2;
        var offset = maxD;
        var length = VectorLength(n, m);
        forward = forward[..length];
        reverse = reverse[..length];
        forward.Fill(-1);
        reverse.Fill(-1);
        forward[offset + 1] = 0;
        reverse[offset + 1] = 0;

        var delta = n - m;
        // With an odd difference the forward search reaches the overlap first.
        var front = delta % 2 != 0;
        // Diagonals that have run off an edge of the grid are not worth extending again.
        var forwardStart = 0;
        var forwardEnd = 0;
        var reverseStart = 0;
        var reverseEnd = 0;

        for (var d = 0; d < maxD; d++)
        {
            for (var k = -d + forwardStart; k <= d - forwardEnd; k += 2)
            {
                var index = offset + k;
                int x;
                if (k == -d ||
                    (k != d && forward[index - 1] < forward[index + 1]))
                {
                    x = forward[index + 1];
                }
                else
                {
                    x = forward[index - 1] + 1;
                }

                var y = x - k;
                while (x < n &&
                       y < m &&
                       a[x] == b[y])
                {
                    x++;
                    y++;
                }

                forward[index] = x;
                if (x > n)
                {
                    forwardEnd += 2;
                }
                else if (y > m)
                {
                    forwardStart += 2;
                }
                else if (front)
                {
                    var reverseIndex = offset + delta - k;
                    if (reverseIndex >= 0 &&
                        reverseIndex < length &&
                        reverse[reverseIndex] != -1 &&
                        x >= n - reverse[reverseIndex])
                    {
                        splitX = x;
                        splitY = y;
                        return true;
                    }
                }
            }

            for (var k = -d + reverseStart; k <= d - reverseEnd; k += 2)
            {
                var index = offset + k;
                int x;
                if (k == -d ||
                    (k != d && reverse[index - 1] < reverse[index + 1]))
                {
                    x = reverse[index + 1];
                }
                else
                {
                    x = reverse[index - 1] + 1;
                }

                var y = x - k;
                while (x < n &&
                       y < m &&
                       a[n - x - 1] == b[m - y - 1])
                {
                    x++;
                    y++;
                }

                reverse[index] = x;
                if (x > n)
                {
                    reverseEnd += 2;
                }
                else if (y > m)
                {
                    reverseStart += 2;
                }
                else if (!front)
                {
                    var forwardIndex = offset + delta - k;
                    if (forwardIndex >= 0 &&
                        forwardIndex < length &&
                        forward[forwardIndex] != -1)
                    {
                        var forwardX = forward[forwardIndex];
                        if (forwardX >= n - x)
                        {
                            splitX = forwardX;
                            splitY = forwardX - (forwardIndex - offset);
                            return true;
                        }
                    }
                }
            }
        }

        splitX = 0;
        splitY = 0;
        return false;
    }
}
