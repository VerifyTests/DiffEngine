using System.Buffers;

namespace DiffEngine;

/// <summary>
/// The elements two sequences can be lined up by when searching for a diff has cost too much:
/// those that occur once in each, and of those the longest run that is in the same order in both.
/// <para>
/// An element that is on each side once can only be matched with itself, so nothing has to be
/// searched to pair them up, and the longest run of the pairs still in order is found by patience
/// sorting, in the time of a sort. Each pair of the run is a point both sequences pass through, and
/// what lies between one point and the next is a diff of its own, a small one.
/// </para>
/// <para>
/// This is what <see cref="MyersDiff"/> falls back on, never what it starts with. A run of unique
/// elements in order is a diff, but not always the smallest: elements that repeat can line up
/// better some other way. So it is only asked for by a search that has already given up on the
/// smallest.
/// </para>
/// </summary>
static class LineAnchors
{
    /// <summary>
    /// The run, as positions in <paramref name="a"/> and the positions in <paramref name="b"/>
    /// they pair with, both ascending. The arrays are rented, and the caller's to return, when
    /// the count that comes back is more than zero.
    /// </summary>
    /// <param name="a">One sequence.</param>
    /// <param name="b">The other.</param>
    /// <param name="needed">
    /// How long a run is worth having. A shorter one comes back as none, and with fewer pairs
    /// than this there is none to look for.
    /// </param>
    /// <param name="xs">Where each element of the run is in <paramref name="a"/>.</param>
    /// <param name="ys">Where each is in <paramref name="b"/>.</param>
    public static int Find(ReadOnlySpan<int> a, ReadOnlySpan<int> b, int needed, out int[]? xs, out int[]? ys)
    {
        xs = null;
        ys = null;
        var most = Math.Min(a.Length, b.Length);
        if (most == 0 ||
            most < needed)
        {
            return 0;
        }

        var pool = ArrayPool<int>.Shared;
        var pairX = pool.Rent(most);
        var pairY = pool.Rent(most);
        try
        {
            var pairs = Pairs(a, b, pairX, pairY);
            if (pairs == 0 ||
                pairs < needed)
            {
                return 0;
            }

            // In the order of one side, the run is the longest increasing one of the other's
            Array.Sort(pairX, pairY, 0, pairs);
            return Longest(pairX, pairY, pairs, needed, out xs, out ys);
        }
        finally
        {
            pool.Return(pairX);
            pool.Return(pairY);
        }
    }

    /// <summary>
    /// Every element that is in each sequence exactly once, as where it is in each. Found by
    /// sorting each side's elements with their positions and walking the two together, so nothing
    /// is assumed about what an element is beyond that two of them can be compared.
    /// </summary>
    static int Pairs(ReadOnlySpan<int> a, ReadOnlySpan<int> b, int[] pairX, int[] pairY)
    {
        var pool = ArrayPool<int>.Shared;
        var valuesA = pool.Rent(a.Length);
        var placesA = pool.Rent(a.Length);
        var valuesB = pool.Rent(b.Length);
        var placesB = pool.Rent(b.Length);
        try
        {
            Sorted(a, valuesA, placesA);
            Sorted(b, valuesB, placesB);
            var pairs = 0;
            var indexA = 0;
            var indexB = 0;
            while (indexA < a.Length &&
                   indexB < b.Length)
            {
                var value = valuesA[indexA];
                if (value < valuesB[indexB])
                {
                    indexA++;
                    continue;
                }

                if (value > valuesB[indexB])
                {
                    indexB++;
                    continue;
                }

                var endA = indexA + 1;
                while (endA < a.Length &&
                       valuesA[endA] == value)
                {
                    endA++;
                }

                var endB = indexB + 1;
                while (endB < b.Length &&
                       valuesB[endB] == value)
                {
                    endB++;
                }

                if (endA - indexA == 1 &&
                    endB - indexB == 1)
                {
                    pairX[pairs] = placesA[indexA];
                    pairY[pairs] = placesB[indexB];
                    pairs++;
                }

                indexA = endA;
                indexB = endB;
            }

            return pairs;
        }
        finally
        {
            pool.Return(valuesA);
            pool.Return(placesA);
            pool.Return(valuesB);
            pool.Return(placesB);
        }
    }

    static void Sorted(ReadOnlySpan<int> sequence, int[] values, int[] places)
    {
        sequence.CopyTo(values);
        for (var index = 0; index < sequence.Length; index++)
        {
            places[index] = index;
        }

        Array.Sort(values, places, 0, sequence.Length);
    }

    /// <summary>
    /// Patience sorting over the pairs' second halves. <c>tops</c> holds, for each length of run
    /// so far, the pair that ends the one ending lowest, and each pair remembers the pair before
    /// it in the run it extended, so the longest can be read back from its end.
    /// </summary>
    static int Longest(int[] pairX, int[] pairY, int pairs, int needed, out int[]? xs, out int[]? ys)
    {
        xs = null;
        ys = null;
        var pool = ArrayPool<int>.Shared;
        var tops = pool.Rent(pairs);
        var before = pool.Rent(pairs);
        try
        {
            var length = 0;
            for (var pair = 0; pair < pairs; pair++)
            {
                var y = pairY[pair];
                var low = 0;
                var high = length;
                while (low < high)
                {
                    var middle = (low + high) / 2;
                    if (pairY[tops[middle]] < y)
                    {
                        low = middle + 1;
                    }
                    else
                    {
                        high = middle;
                    }
                }

                before[pair] = low == 0 ? -1 : tops[low - 1];
                tops[low] = pair;
                if (low == length)
                {
                    length++;
                }
            }

            if (length < needed)
            {
                return 0;
            }

            xs = pool.Rent(length);
            ys = pool.Rent(length);
            var at = tops[length - 1];
            for (var index = length - 1; index >= 0; index--)
            {
                xs[index] = pairX[at];
                ys[index] = pairY[at];
                at = before[at];
            }

            return length;
        }
        finally
        {
            pool.Return(tops);
            pool.Return(before);
        }
    }
}
