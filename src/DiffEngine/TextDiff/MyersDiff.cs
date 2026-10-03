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
/// <para>
/// Minimal, for as long as that is affordable. A search for one middle snake costs by the square
/// of the edits it has to look through, and for two long sequences that share their elements in
/// another order that is all of them: 40,000 a side took four seconds. So a diff has a budget of
/// that work (<see cref="Budget"/>), and a search that would take it past the budget stops and
/// splits at the furthest point either of its paths got to. Any point is a correct place to
/// split, and that one is where the most has been passed. A diff that got that far may leave
/// fewer elements unchanged than it could have. Nothing the search used to do in a tenth of a
/// second gets that far, so what was quick is exactly as it was.
/// </para>
/// </summary>
static class MyersDiff
{
    /// <summary>
    /// Buffers up to this many elements go on the stack rather than to the pool.
    /// </summary>
    public const int StackLimit = 256;

    /// <summary>
    /// How much searching one diff may do before it settles, counted as each search's depth in
    /// edits, squared, which is the diagonals that search looks at.
    /// <para>
    /// Sixty seven million of them, which is about a sixth of a second. That is one search 8,000
    /// edits deep, so a block of 8,000 elements moved from one end of a sequence to the other is
    /// still found, however long the sequence. And it is more than any two sequences of 10,000
    /// elements between them can need, whatever is in them, so those are always diffed minimally.
    /// </para>
    /// </summary>
    internal const long Budget = 1L << 26;

    /// <summary>
    /// The depth every search is allowed once the budget has gone: enough to get past a few
    /// hundred scattered edits and split where they have been passed. A search that settles has
    /// moved on by at least its depth, so this times the length is all the rest of the diff can
    /// cost.
    /// </summary>
    internal const int MinimumDepth = 256;

    public static void Diff(ReadOnlySpan<int> a, ReadOnlySpan<int> b, Span<bool> changedA, Span<bool> changedB) =>
        Diff(a, b, changedA, changedB, Budget, MinimumDepth);

    /// <param name="a">One sequence.</param>
    /// <param name="b">The other.</param>
    /// <param name="changedA">Set for each element of <paramref name="a"/> with no match.</param>
    /// <param name="changedB">Set for each element of <paramref name="b"/> with no match.</param>
    /// <param name="budget">
    /// The searching to do before settling. Given by the tests, along with
    /// <paramref name="minimumDepth"/>, which is the only way a search that settles can be had on
    /// sequences small enough to check by other means.
    /// </param>
    /// <param name="minimumDepth">The depth a search is allowed with nothing left of the budget.</param>
    internal static void Diff(
        ReadOnlySpan<int> a,
        ReadOnlySpan<int> b,
        Span<bool> changedA,
        Span<bool> changedB,
        long budget,
        int minimumDepth)
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
            Recurse(a, b, changedA, changedB, forward, reverse, ref budget, Math.Max(1, minimumDepth));
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

    /// <summary>
    /// Recurses into the smaller of the two halves a split leaves and loops on the larger, so the
    /// stack is never deeper than the number of times the problem can be halved. Which half that
    /// is no longer follows from where the split is: one a search settled for can leave almost
    /// everything on either side of it.
    /// </summary>
    static void Recurse(
        ReadOnlySpan<int> a,
        ReadOnlySpan<int> b,
        Span<bool> changedA,
        Span<bool> changedB,
        Span<int> forward,
        Span<int> reverse,
        ref long budget,
        int minimumDepth)
    {
        while (true)
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

            // As deep as what is left of the budget buys, and never less than the minimum. The
            // whole of the grid when that is within reach, which is a search that cannot settle.
            var deepest = (a.Length + b.Length + 1) / 2;
            var affordable = budget <= 0 ? 0 : (long) Math.Sqrt(budget);
            var limit = (int) Math.Min(deepest, Math.Max(minimumDepth, affordable));
            var found = TryMiddleSnake(a, b, forward, reverse, limit, out var x, out var y, out var depth);
            budget -= (long) depth * depth;
            if (!found)
            {
                changedA.Fill(true);
                changedB.Fill(true);
                return;
            }

            if (x + y <= a.Length - x + b.Length - y)
            {
                Recurse(a[..x], b[..y], changedA[..x], changedB[..y], forward, reverse, ref budget, minimumDepth);
                a = a[x..];
                b = b[y..];
                changedA = changedA[x..];
                changedB = changedB[y..];
            }
            else
            {
                Recurse(a[x..], b[y..], changedA[x..], changedB[y..], forward, reverse, ref budget, minimumDepth);
                a = a[..x];
                b = b[..y];
                changedA = changedA[..x];
                changedB = changedB[..y];
            }
        }
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
    /// <para>
    /// After <paramref name="limit"/> edits without an overlap the search settles for
    /// <see cref="TryFurthest"/>. False when there is nowhere to split, which leaves everything in
    /// both sequences changed. <paramref name="depth"/> is how many edits deep it went either way,
    /// which is what it is charged for.
    /// </para>
    /// </summary>
    static bool TryMiddleSnake(
        ReadOnlySpan<int> a,
        ReadOnlySpan<int> b,
        Span<int> forward,
        Span<int> reverse,
        int limit,
        out int splitX,
        out int splitY,
        out int depth)
    {
        var n = a.Length;
        var m = b.Length;
        var maxD = (n + m + 1) / 2;
        var offset = maxD;
        // Only the diagonals this search can get to are cleared and read, the ones within its
        // deepest edit of the middle and one more either side for the neighbours each step reads.
        // Clearing the vectors whole cost by the length for every split, and a diff that settles
        // makes a split every few hundred elements: that was the length squared again by another
        // road. What lies outside holds whatever an earlier search left, so nothing may read it.
        var reach = Math.Min(maxD, limit + 1);
        var low = offset - reach;
        var high = offset + reach;
        forward[low..(high + 1)].Fill(-1);
        reverse[low..(high + 1)].Fill(-1);
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
                    if (reverseIndex >= low &&
                        reverseIndex <= high &&
                        reverse[reverseIndex] != -1 &&
                        x >= n - reverse[reverseIndex])
                    {
                        splitX = x;
                        splitY = y;
                        depth = d + 1;
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
                    if (forwardIndex >= low &&
                        forwardIndex <= high &&
                        forward[forwardIndex] != -1)
                    {
                        var forwardX = forward[forwardIndex];
                        if (forwardX >= n - x)
                        {
                            splitX = forwardX;
                            splitY = forwardX - (forwardIndex - offset);
                            depth = d + 1;
                            return true;
                        }
                    }
                }
            }

            if (d >= limit)
            {
                depth = d + 1;
                var furthest = TryFurthest(n, m, d, offset, forward, reverse, out splitX, out splitY, out var matched);
                if (TryDisplaced(a, b, depth, matched, out var displacedX, out var displacedY))
                {
                    splitX = displacedX;
                    splitY = displacedY;
                    return true;
                }

                return furthest;
            }
        }

        splitX = 0;
        splitY = 0;
        depth = maxD;
        return false;
    }

    /// <summary>
    /// A run of this many elements found by <see cref="TryDisplaced"/> is taken as the sequences
    /// lining up there rather than as a line that happens to repeat.
    /// </summary>
    const int DisplacedRun = 16;

    /// <summary>
    /// As far as a run is followed to see how long it is. Past this it is long enough, and the
    /// rest of it is walked once, by whoever is handed the split.
    /// </summary>
    const int RunCap = 4096;

    /// <summary>
    /// A better place to split than the furthest point, when a search settled having found next
    /// to nothing: where one sequence's first element is in the other, if a run of matches
    /// follows from there.
    /// <para>
    /// A search only looks within its depth of the diagonal it started on. A block of elements
    /// moved further than that, from one end of a sequence to the other, lines up on a diagonal it
    /// never reaches, so it passes nothing, every point it got to is as good as every other, and
    /// the split it settles for is as likely to walk away from where the sequences line up as
    /// towards it. Splitting by the furthest point alone, 20,000 lines moved within 400,000 came
    /// out as the 380,000 that had not moved being removed and added again. The start of each
    /// sequence is somewhere in the other, since elements only one has were set aside before the
    /// diff began, and looking for it is one scan.
    /// </para>
    /// <para>
    /// The nearer of the two when both lead to a run, since that is the smaller block to call
    /// changed. Neither when the search itself passed more than the run found, which is edits
    /// scattered along a diagonal it was already following.
    /// </para>
    /// </summary>
    static bool TryDisplaced(ReadOnlySpan<int> a, ReadOnlySpan<int> b, int depth, int matched, out int splitX, out int splitY)
    {
        splitX = 0;
        splitY = 0;
        // No further than the search looked, which was its depth squared, so a scan never costs
        // more than the search it follows did
        var window = (int) Math.Min(int.MaxValue, (long) depth * depth);
        var inA = a[..Math.Min(a.Length, window)].IndexOf(b[0]);
        var inB = b[..Math.Min(b.Length, window)].IndexOf(a[0]);
        var runA = inA > 0 ? Run(a[inA..], b) : 0;
        var runB = inB > 0 ? Run(a, b[inB..]) : 0;
        var needed = Math.Max(DisplacedRun, matched + 1);
        var fromA = runA >= needed;
        var fromB = runB >= needed;
        if (fromA &&
            (!fromB || inA <= inB))
        {
            splitX = inA;
            return true;
        }

        if (fromB)
        {
            splitY = inB;
            return true;
        }

        return false;
    }

    static int Run(ReadOnlySpan<int> a, ReadOnlySpan<int> b)
    {
        var length = Math.Min(RunCap, Math.Min(a.Length, b.Length));
        var run = 0;
        while (run < length &&
               a[run] == b[run])
        {
            run++;
        }

        return run;
    }

    /// <summary>
    /// Where to split when the search is not to go on: the point furthest along that either path
    /// reached, counted from the corner that path set out from. The elements between that corner
    /// and the point are a small diff, already known to be a few edits, and the rest is searched
    /// again from there.
    /// <para>
    /// Any point inside the grid splits the problem into two that together are a correct diff, so
    /// nothing here can produce a wrong one. What is lost is that the two together need not be the
    /// smallest. A corner is the one point that will not do, since one of its halves is the whole
    /// problem again.
    /// </para>
    /// <para>
    /// <paramref name="matched"/> is how many elements the path to that point passed as the same
    /// on both sides: what it got to, less the edits it took, on each side.
    /// </para>
    /// </summary>
    static bool TryFurthest(
        int n,
        int m,
        int d,
        int offset,
        ReadOnlySpan<int> forward,
        ReadOnlySpan<int> reverse,
        out int splitX,
        out int splitY,
        out int matched)
    {
        splitX = 0;
        splitY = 0;
        var best = 0;
        for (var k = -d; k <= d; k++)
        {
            // A diagonal that ran off the grid holds a point outside it, and one never reached
            // holds -1. Neither is somewhere to split.
            var x = forward[offset + k];
            var y = x - k;
            if (x >= 0 &&
                y >= 0 &&
                x <= n &&
                y <= m &&
                x + y > best)
            {
                best = x + y;
                splitX = x;
                splitY = y;
            }

            x = reverse[offset + k];
            y = x - k;
            if (x >= 0 &&
                y >= 0 &&
                x <= n &&
                y <= m &&
                x + y > best)
            {
                best = x + y;
                splitX = n - x;
                splitY = m - y;
            }
        }

        matched = Math.Max(0, best - d) / 2;
        return best > 0 &&
               best < n + m;
    }
}
