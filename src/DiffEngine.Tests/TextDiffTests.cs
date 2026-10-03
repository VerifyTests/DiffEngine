public class TextDiffTests
{
    [Test]
    public Task Identical() =>
        AssertCompute(
            "a\nb",
            "a\nb",
            "  1 1 |a",
            "  2 2 |b");

    [Test]
    public async Task BothEmpty()
    {
        await Assert.That(TextDiff.Compute("", "")).IsEmpty();
        await Assert.That(TextDiff.Format("", "", TextDiffFormat.Full)).IsEqualTo("");
        await Assert.That(TextDiff.Format("", "")).IsEqualTo("");
        await Assert.That(TextDiff.Format("", "", TextDiffFormat.Minimal)).IsEqualTo("");
    }

    [Test]
    public Task ExpectedEmpty() =>
        AssertCompute(
            "",
            "a\nb",
            "+ . 1 |a",
            "+ . 2 |b");

    [Test]
    public Task ReceivedEmpty() =>
        AssertCompute(
            "a\nb",
            "",
            "- 1 . |a",
            "- 2 . |b");

    [Test]
    public Task OneChangedLine() =>
        AssertCompute(
            "a\nb\nc",
            "a\nX\nc",
            "  1 1 |a",
            "- 2 . |b",
            "+ . 2 |X",
            "  3 3 |c");

    [Test]
    public Task PureInsert() =>
        AssertCompute(
            "a\nc",
            "a\nb\nc",
            "  1 1 |a",
            "+ . 2 |b",
            "  2 3 |c");

    [Test]
    public Task PureDelete() =>
        AssertCompute(
            "a\nb\nc",
            "a\nc",
            "  1 1 |a",
            "- 2 . |b",
            "  3 2 |c");

    [Test]
    public Task MoreRemovedThanAdded() =>
        AssertCompute(
            "a\nb\nc\nd",
            "a\nX\nd",
            "  1 1 |a",
            "- 2 . |b",
            "- 3 . |c",
            "+ . 2 |X",
            "  4 3 |d");

    [Test]
    public Task MoreAddedThanRemoved() =>
        AssertCompute(
            "a\nb\nd",
            "a\nX\nY\nd",
            "  1 1 |a",
            "- 2 . |b",
            "+ . 2 |X",
            "+ . 3 |Y",
            "  3 4 |d");

    /// <summary>
    /// Lines compare exactly. A snapshot that fails only on a trailing space or indentation must
    /// show the line, or the failure has no visible cause.
    /// </summary>
    [Test]
    public Task WhitespaceOnly() =>
        AssertCompute(
            "a\n  b\nc",
            "a\nb\nc ",
            "  1 1 |a",
            "- 2 . |  b",
            "- 3 . |c",
            "+ . 2 |b",
            "+ . 3 |c ");

    [Test]
    public Task CaseOnly() =>
        AssertCompute(
            "A",
            "a",
            "- 1 . |A",
            "+ . 1 |a");

    /// <summary>
    /// Line endings split lines rather than belong to them, so the same lines under different
    /// endings are the same lines.
    /// </summary>
    [Test]
    public Task MixedLineEndings() =>
        AssertCompute(
            "a\r\nb\rc\nd",
            "a\nb\nc\r\nd",
            "  1 1 |a",
            "  2 2 |b",
            "  3 3 |c",
            "  4 4 |d");

    [Test]
    public Task TrailingNewlineOnOneSide() =>
        AssertCompute(
            "a",
            "a\n",
            "  1 1 |a",
            "+ . 2 |");

    [Test]
    public Task TotallyDifferent() =>
        AssertCompute(
            "a\nb",
            "X\nY\nZ",
            "- 1 . |a",
            "- 2 . |b",
            "+ . 1 |X",
            "+ . 2 |Y",
            "+ . 3 |Z");

    const string numbered = "1\n2\n3\n4\n5\n6\n7\n8\n9\n10\n11";
    const string numberedChanged = "1\n2\n3\n4\nfive\n6\n7\n8\n9\n10\neleven";

    [Test]
    public Task FormatFull() =>
        AssertFormat(
            TextDiffFormat.Full,
            numbered,
            numberedChanged,
            "  1",
            "  2",
            "  3",
            "  4",
            "- 5",
            "+ five",
            "  6",
            "  7",
            "  8",
            "  9",
            "  10",
            "- 11",
            "+ eleven");

    [Test]
    public Task FormatMinimal() =>
        AssertFormat(
            TextDiffFormat.Minimal,
            numbered,
            numberedChanged,
            "- 5",
            "+ five",
            "- 11",
            "+ eleven");

    /// <summary>
    /// Two digit line numbers, so the numbers are zero padded and the symbols right aligned under
    /// them, and a change on the last line, so the output ends at [EOF].
    /// </summary>
    [Test]
    public Task FormatCompact() =>
        AssertFormat(
            TextDiffFormat.Compact,
            numbered,
            numberedChanged,
            "04 4",
            " - 5",
            " + five",
            "06 6",
            "",
            "10 10",
            " - 11",
            " + eleven",
            "   [EOF]");

    [Test]
    public Task FormatCompactIsTheDefault() =>
        AssertFormat(
            null,
            "a\nb\nc",
            "a\nX\nc",
            "1 a",
            "- b",
            "+ X",
            "3 c");

    [Test]
    public Task FormatCompactChangeOnFirstLine() =>
        AssertFormat(
            TextDiffFormat.Compact,
            "a\nb",
            "X\nb",
            "  [BOF]",
            "- a",
            "+ X",
            "2 b");

    [Test]
    public Task FormatCompactReceivedEmpty() =>
        AssertFormat(
            TextDiffFormat.Compact,
            "a",
            "",
            "  [BOF]",
            "- a",
            "  [EOF]");

    /// <summary>
    /// Only line breaks are trimmed from the end, so a trailing space that is the difference is
    /// still in the output.
    /// </summary>
    [Test]
    public Task FormatKeepsTrailingWhitespace() =>
        AssertFormat(
            TextDiffFormat.Minimal,
            "a",
            "a ",
            "- a",
            "+ a ");

    [Test]
    public async Task FormatUnknown() =>
        await Assert.That(() => TextDiff.Format("a", "b", (TextDiffFormat)99)).Throws<ArgumentOutOfRangeException>();

    /// <summary>
    /// An unchanged line on one side with nothing left on the other cannot be walked past, and
    /// must throw rather than loop.
    /// </summary>
    [Test]
    public async Task WalkThrowsWhenTheSidesDisagree() =>
        await Assert.That(() => LineDiff.Walk([false], [])).Throws<InvalidOperationException>();

    [Test]
    public async Task FormatIdentical() =>
        await Assert.That(TextDiff.Format("a\nb", "a\nb")).IsEqualTo("");

    /// <summary>
    /// Random texts over a small alphabet, so lines repeat and alignments are ambiguous, checked
    /// against the longest common subsequence from a dynamic programming table. The diff must be
    /// minimal, and each side must be exactly its lines in order.
    /// </summary>
    [Test]
    public void MatchesLongestCommonSubsequence()
    {
        var random = new Random(1);
        for (var iteration = 0; iteration < 3000; iteration++)
        {
            var alphabet = 1 + iteration % 6;
            var expected = RandomLines(random, random.Next(0, 40), alphabet);
            var received = RandomLines(random, random.Next(0, 40), alphabet);
            AssertMinimal(expected, received);
        }
    }

    /// <summary>
    /// Past <see cref="MyersDiff.StackLimit"/>, so the pooled buffers are used, with enough
    /// distinct lines that the interner's table has to probe past collisions.
    /// </summary>
    [Test]
    public void LargeRandom()
    {
        var random = new Random(2);
        for (var iteration = 0; iteration < 10; iteration++)
        {
            var expected = RandomLines(random, 600, 400);
            var received = RandomLines(random, 700, 400);
            AssertMinimal(expected, received);
        }
    }

    [Test]
    public async Task LargeWithFewEdits()
    {
        var expected = Enumerable.Range(0, 20000).Select(_ => $"line {_}").ToList();
        var received = expected.ToList();
        received[10] = "changed";
        received.RemoveAt(5000);
        received.Insert(15000, "inserted");

        var lines = TextDiff.Compute(string.Join("\n", expected), string.Join("\n", received));

        await Assert.That(lines.Count(_ => _.Kind == DiffLineKind.Unchanged)).IsEqualTo(19998);
        await Assert.That(lines.Count(_ => _.Kind == DiffLineKind.Removed)).IsEqualTo(2);
        await Assert.That(lines.Count(_ => _.Kind == DiffLineKind.Added)).IsEqualTo(2);
    }

    /// <summary>
    /// A larger alphabet than the lines drawn from it, so most lines are on one side only. Those
    /// are taken out before Myers runs, and what comes back has to be the same minimal diff, with
    /// every flag back on the line it was for.
    /// </summary>
    [Test]
    public void MatchesLongestCommonSubsequenceWhenMostLinesAreOnOneSide()
    {
        var random = new Random(3);
        for (var iteration = 0; iteration < 3000; iteration++)
        {
            var alphabet = 2 + iteration % 60;
            var expected = RandomLines(random, random.Next(0, 40), alphabet);
            var received = RandomLines(random, random.Next(0, 40), alphabet);
            AssertMinimal(expected, received);
        }
    }

    /// <summary>
    /// No line survives a change of indentation, which made this the slowest input there was: the
    /// search went through every line as an edit, and 40,000 lines a side took four seconds.
    /// </summary>
    [Test]
    public async Task LargeWithNothingInCommon()
    {
        var expected = Enumerable.Range(0, 30000).Select(_ => $"  \"property{_}\": {_},").ToList();
        var received = expected.Select(_ => "  " + _).ToList();

        var lines = TextDiff.Compute(string.Join("\n", expected), string.Join("\n", received));

        await Assert.That(lines.Count).IsEqualTo(60000);
        await Assert.That(lines.Take(30000).All(_ => _.Kind == DiffLineKind.Removed)).IsTrue();
        await Assert.That(lines.Skip(30000).All(_ => _.Kind == DiffLineKind.Added)).IsTrue();
    }

    /// <summary>
    /// Every line on both sides and none where it was, which leaves nothing to take out first and
    /// is long enough for the search to settle rather than finish. What comes back need not be
    /// minimal; it has to be a diff, each side its own lines in order.
    /// </summary>
    [Test]
    public async Task LargeInAnotherOrder()
    {
        var random = new Random(4);
        var expected = Enumerable.Range(0, 30000).Select(_ => $"line {_}").ToList();
        var received = expected.OrderBy(_ => random.Next()).ToList();

        var lines = TextDiff.Compute(string.Join("\n", expected), string.Join("\n", received));

        await Assert.That(lines.Where(_ => _.Kind != DiffLineKind.Added).Select(_ => _.Text).SequenceEqual(expected)).IsTrue();
        await Assert.That(lines.Where(_ => _.Kind != DiffLineKind.Removed).Select(_ => _.Text).SequenceEqual(received)).IsTrue();
    }

    /// <summary>
    /// The worst a text of a few thousand lines can be, every line in another place, is within
    /// what a diff may spend, so it is still minimal. Every line being on both sides once, the
    /// most that can stay unchanged is the longest run of them still in their old order.
    /// </summary>
    [Test]
    public async Task AFewThousandLinesInAnotherOrderAreStillDiffedMinimally()
    {
        var random = new Random(9);
        var order = Enumerable.Range(0, 5000).OrderBy(_ => random.Next()).ToArray();
        var expected = Enumerable.Range(0, 5000).Select(_ => $"line {_}").ToList();
        var received = order.Select(_ => expected[_]).ToList();

        var lines = TextDiff.Compute(string.Join("\n", expected), string.Join("\n", received));

        await Assert.That(lines.Count(_ => _.Kind == DiffLineKind.Unchanged)).IsEqualTo(LongestIncreasingRun(order));
        await Assert.That(lines.Where(_ => _.Kind != DiffLineKind.Added).Select(_ => _.Text).SequenceEqual(expected)).IsTrue();
        await Assert.That(lines.Where(_ => _.Kind != DiffLineKind.Removed).Select(_ => _.Text).SequenceEqual(received)).IsTrue();
    }

    // Patience sorting: the piles' tops stay sorted, and there are as many piles as the longest
    // increasing run is long.
    static int LongestIncreasingRun(int[] values)
    {
        var tops = new List<int>();
        foreach (var value in values)
        {
            var pile = tops.BinarySearch(value);
            if (pile < 0)
            {
                pile = ~pile;
            }

            if (pile == tops.Count)
            {
                tops.Add(value);
            }
            else
            {
                tops[pile] = value;
            }
        }

        return tops.Count;
    }

    /// <summary>
    /// And so is a long text with a large block moved, which is one deep search and little else:
    /// the block is what changes, and the rest of the text is found where it now is. What a diff
    /// may spend is counted in work rather than in lines so that this is not given up on for
    /// being long.
    /// </summary>
    [Test]
    public async Task ABlockMovedInALongTextIsStillFound()
    {
        var expected = Enumerable.Range(0, 60000).Select(_ => $"line {_}").ToList();
        var received = expected.Skip(5000).Concat(expected.Take(5000)).ToList();

        var lines = TextDiff.Compute(string.Join("\n", expected), string.Join("\n", received));

        await Assert.That(lines.Count(_ => _.Kind == DiffLineKind.Unchanged)).IsEqualTo(55000);
        await Assert.That(lines.Count(_ => _.Kind == DiffLineKind.Removed)).IsEqualTo(5000);
        await Assert.That(lines.Count(_ => _.Kind == DiffLineKind.Added)).IsEqualTo(5000);
    }

    /// <summary>
    /// A block moved further than a search may go deep is on a diagonal the search never reaches,
    /// so the search settles having passed nothing. Where it splits then is where the start of one
    /// text is in the other, and the rest of the text is found unchanged whichever way the block
    /// went. Split at the furthest point reached instead, this came out as the 88,000 lines that
    /// had not moved being removed and added again.
    /// </summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ABlockMovedFurtherThanASearchGoesIsStillFound(bool toTheEnd)
    {
        var expected = Enumerable.Range(0, 100000).Select(_ => $"line {_}").ToList();
        var moved = toTheEnd ? 12000 : 88000;
        var received = expected.Skip(moved).Concat(expected.Take(moved)).ToList();

        var lines = TextDiff.Compute(string.Join("\n", expected), string.Join("\n", received));

        await Assert.That(lines.Count(_ => _.Kind == DiffLineKind.Unchanged)).IsEqualTo(88000);
        await Assert.That(lines.Count(_ => _.Kind == DiffLineKind.Removed)).IsEqualTo(12000);
        await Assert.That(lines.Count(_ => _.Kind == DiffLineKind.Added)).IsEqualTo(12000);
        await Assert.That(lines.Where(_ => _.Kind != DiffLineKind.Added).Select(_ => _.Text).SequenceEqual(expected)).IsTrue();
        await Assert.That(lines.Where(_ => _.Kind != DiffLineKind.Removed).Select(_ => _.Text).SequenceEqual(received)).IsTrue();
    }

    /// <summary>
    /// The same on sequences small enough to check in bulk: runs of elements, the runs in another
    /// order on the other side, and searches with nothing to spend, so every one settles and
    /// looks for where the sequences line up. Whatever it splits at, what is left unchanged on
    /// each side has to be the same elements in the same order.
    /// </summary>
    [Test]
    public void ASearchThatSettlesOnMovedRunsStillDiffsCorrectly()
    {
        var random = new Random(10);
        for (var iteration = 0; iteration < 3000; iteration++)
        {
            var runs = Enumerable.Range(0, random.Next(2, 7))
                .Select(_ => Enumerable.Range(_ * 1000, random.Next(1, 40)).ToArray())
                .ToList();
            var a = runs.SelectMany(_ => _).ToArray();
            var b = runs.OrderBy(_ => random.Next()).SelectMany(_ => _).ToArray();
            // Some edits as well, so a run is not always whole on both sides
            for (var edit = random.Next(0, 4); edit > 0 && b.Length > 0; edit--)
            {
                b[random.Next(b.Length)] = a[random.Next(a.Length)];
            }

            var minimumDepth = 1 + iteration % 5;
            var changedA = new bool[a.Length];
            var changedB = new bool[b.Length];

            MyersDiff.Diff(a, b, changedA, changedB, 0, minimumDepth);

            var unchangedA = a.Where((_, index) => !changedA[index]);
            var unchangedB = b.Where((_, index) => !changedB[index]);
            if (!unchangedA.SequenceEqual(unchangedB))
            {
                Assert.Fail($"minimum: {minimumDepth} a: {string.Join(",", a)} b: {string.Join(",", b)}");
            }
        }
    }

    /// <summary>
    /// Searches made to settle after an edit or a few, on sequences small enough to check. The
    /// elements left unchanged on each side have to be the same elements in the same order, which
    /// is all that makes a diff correct; that there are as many as there could be is what settling
    /// gives up.
    /// </summary>
    [Test]
    public void ASearchThatSettlesStillDiffsCorrectly()
    {
        var random = new Random(5);
        for (var iteration = 0; iteration < 20000; iteration++)
        {
            var alphabet = 1 + iteration % 6;
            var a = RandomIds(random, random.Next(0, 60), alphabet);
            var b = RandomIds(random, random.Next(0, 60), alphabet);
            // From nothing to spend, where every search settles at its minimum, to enough for the
            // first few searches and not the rest
            var budget = iteration % 40;
            var minimumDepth = 1 + iteration % 5;
            var changedA = new bool[a.Length];
            var changedB = new bool[b.Length];

            MyersDiff.Diff(a, b, changedA, changedB, budget, minimumDepth);

            var unchangedA = a.Where((_, index) => !changedA[index]);
            var unchangedB = b.Where((_, index) => !changedB[index]);
            if (!unchangedA.SequenceEqual(unchangedB))
            {
                Assert.Fail($"budget: {budget} minimum: {minimumDepth} a: {string.Join(",", a)} b: {string.Join(",", b)}");
            }
        }
    }

    /// <summary>
    /// The same sequences with more to spend than any of them needs are diffed as they always
    /// were, so the budget costs nothing where it does not apply.
    /// </summary>
    [Test]
    public void ABudgetNoDiffSpendsChangesNothing()
    {
        var random = new Random(6);
        for (var iteration = 0; iteration < 5000; iteration++)
        {
            var alphabet = 1 + iteration % 6;
            var a = RandomIds(random, random.Next(0, 60), alphabet);
            var b = RandomIds(random, random.Next(0, 60), alphabet);
            var changedA = new bool[a.Length];
            var changedB = new bool[b.Length];

            MyersDiff.Diff(a, b, changedA, changedB, long.MaxValue, 1);

            var unchanged = changedA.Count(_ => !_);
            var longest = LongestCommonSubsequence(
                a.Select(_ => _.ToString()).ToList(),
                b.Select(_ => _.ToString()).ToList());
            if (unchanged != longest ||
                changedB.Count(_ => !_) != longest)
            {
                Assert.Fail($"a: {string.Join(",", a)} b: {string.Join(",", b)}");
            }
        }
    }

    static int[] RandomIds(Random random, int count, int alphabet) =>
        Enumerable.Range(0, count).Select(_ => random.Next(alphabet)).ToArray();

    // Never an empty line: a list of one empty line joins to empty text, which is no lines.
    static List<string> RandomLines(Random random, int count, int alphabet) =>
        Enumerable.Range(0, count).Select(_ => "l" + random.Next(alphabet)).ToList();

    static void AssertMinimal(List<string> expected, List<string> received)
    {
        var lines = TextDiff.Compute(string.Join("\n", expected), string.Join("\n", received));

        var expectedSide = lines.Where(_ => _.Kind != DiffLineKind.Added).Select(_ => _.Text);
        var receivedSide = lines.Where(_ => _.Kind != DiffLineKind.Removed).Select(_ => _.Text);
        var unchanged = lines.Count(_ => _.Kind == DiffLineKind.Unchanged);
        if (!expectedSide.SequenceEqual(expected) ||
            !receivedSide.SequenceEqual(received) ||
            unchanged != LongestCommonSubsequence(expected, received))
        {
            Assert.Fail($"expected: {string.Join(",", expected)} received: {string.Join(",", received)}");
        }
    }

    static int LongestCommonSubsequence(List<string> a, List<string> b)
    {
        var table = new int[a.Count + 1, b.Count + 1];
        for (var i = a.Count - 1; i >= 0; i--)
        {
            for (var j = b.Count - 1; j >= 0; j--)
            {
                if (a[i] == b[j])
                {
                    table[i, j] = table[i + 1, j + 1] + 1;
                }
                else
                {
                    table[i, j] = Math.Max(table[i + 1, j], table[i, j + 1]);
                }
            }
        }

        return table[0, 0];
    }

    static async Task AssertCompute(string expected, string received, params string[] lines)
    {
        var builder = new StringBuilder();
        foreach (var line in TextDiff.Compute(expected, received))
        {
            var symbol = line.Kind switch
            {
                DiffLineKind.Removed => '-',
                DiffLineKind.Added => '+',
                _ => ' '
            };
            builder.Append($"{symbol} {line.ExpectedLine?.ToString() ?? "."} {line.ReceivedLine?.ToString() ?? "."} |{line.Text}\n");
        }

        await Assert.That(builder.ToString()).IsEqualTo(string.Concat(lines.Select(_ => _ + "\n")));
    }

    static async Task AssertFormat(TextDiffFormat? format, string expected, string received, params string[] lines)
    {
        string actual;
        if (format == null)
        {
            actual = TextDiff.Format(expected, received);
        }
        else
        {
            actual = TextDiff.Format(expected, received, format.Value);
        }

        await Assert.That(actual).IsEqualTo(string.Join("\n", lines));
    }
}
