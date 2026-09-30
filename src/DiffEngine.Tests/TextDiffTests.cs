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
