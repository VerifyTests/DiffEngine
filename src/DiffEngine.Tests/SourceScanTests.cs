/// <summary>
/// The map every search in the patcher reads. What it has to be is one consistent account of a
/// file: each offset is code or is inside exactly one comment or literal, and that span is found
/// from its start by a search stepping forwards and from its end by one stepping back. The spans
/// are found by searching sorted lists behind a map of which offsets are code, so the three have
/// to agree at every offset, and these walk every offset to see that they do.
/// </summary>
public class SourceScanTests
{
    const string csharp =
        """"
        // a line comment
        #warning said "like this"
        class C /* a block comment */
        {
            char c = '"';
            string s = "text // not a comment";
            string v = @"verbatim ""quoted"" \";
            string r = """
                raw "quoted" text
                """;
            string i = $"hole {Call("inner")} end";
            void M() => Verify(value) // trailing
                .Snapshot("old");
        }
        """";

    const string fsharp =
        """"
        // a line comment
        #if DEBUG
        (* a block (* nested *) comment *)
        let c = 'x'
        let f (x: 'T) = x
        let s = "text (* not a comment *)"
        let v = @"verbatim ""quoted"""
        let r = """triple "quoted" """
        let m () = Verify(value).Snapshot("old") // trailing
        """";

    [Test]
    public async Task CSharpSpansAreFoundFromBothEnds()
    {
        var (spans, comments, problems) = Walk(SourceLanguage.CSharp, csharp);

        await Assert.That(problems).IsEmpty();
        // Two comments, a directive and a trailing comment, and six literals: the hole and the
        // literal inside it belong to the string they are in
        await Assert.That(comments).IsEqualTo(4);
        await Assert.That(spans).IsEqualTo(10);
    }

    [Test]
    public async Task FSharpSpansAreFoundFromBothEnds()
    {
        var (spans, comments, problems) = Walk(SourceLanguage.FSharp, fsharp);

        await Assert.That(problems).IsEmpty();
        // The tick of a type parameter is code, so five literals and four comments
        await Assert.That(comments).IsEqualTo(4);
        await Assert.That(spans).IsEqualTo(9);
    }

    /// <summary>
    /// Source nobody arranged: the patcher's own tests, which are thousands of lines of literals
    /// holding code, comments holding literals, and raw strings holding both.
    /// </summary>
    [Test]
    [Arguments("InlinePatcherTests.cs")]
    [Arguments("InlinePatcherFsTests.cs")]
    [Arguments("CsStringLiteralTests.cs")]
    [Arguments("FsCompilerRoundTripTests.cs")]
    public async Task TheSuitesOwnSourceIsOneConsistentMap(string file)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(SourceDirectory, file));

        var (spans, _, problems) = Walk(SourceLanguage.CSharp, source);

        await Assert.That(problems).IsEmpty();
        await Assert.That(spans).IsGreaterThan(10);
    }

    static string SourceDirectory { get; } = Path.GetDirectoryName(GetSourceFile())!;

    static string GetSourceFile([CallerFilePath] string path = "") => path;

    /// <summary>
    /// Steps through the source the way a search does, over spans and one offset at a time across
    /// code, and asks every question the scan answers at each place it stops.
    /// </summary>
    static (int spans, int comments, List<string> problems) Walk(SourceLanguage language, string source)
    {
        using var scan = language.Scan(source);
        var problems = new List<string>();
        var ends = new HashSet<int>();
        var spans = 0;
        var comments = 0;
        var index = 0;
        while (index < source.Length)
        {
            if (!scan.TryGetSkip(index, out var end))
            {
                if (!scan.IsCode(index))
                {
                    problems.Add($"{index} is not code and no span starts there");
                }

                if (scan.TryGetCommentSkip(index, out _))
                {
                    problems.Add($"{index} starts a comment and no span");
                }

                index++;
                continue;
            }

            spans++;
            ends.Add(end);
            if (end <= index ||
                end > source.Length)
            {
                problems.Add($"the span at {index} ends at {end}");
                break;
            }

            for (var offset = index; offset < end; offset++)
            {
                if (scan.IsCode(offset))
                {
                    problems.Add($"{offset} is code inside the span at {index}");
                }

                if (offset > index &&
                    scan.TryGetSkip(offset, out _))
                {
                    problems.Add($"a span starts at {offset} inside the span at {index}");
                }
            }

            if (!scan.TryGetSkipEndingAt(end, out var start) ||
                start != index)
            {
                problems.Add($"the span at {index} is not found from its end at {end}");
            }

            var comment = scan.TryGetCommentSkip(index, out var commentEnd);
            if (comment)
            {
                comments++;
                if (commentEnd != end)
                {
                    problems.Add($"the comment at {index} ends at {commentEnd} and the span at {end}");
                }
            }

            if (scan.TryGetCommentEndingAt(end, out var commentStart) != comment ||
                comment && commentStart != index)
            {
                problems.Add($"the span at {index} is a comment from one end and not from the other");
            }

            index = end;
        }

        // And nothing ends where the walk found no span ending
        for (var offset = 0; offset <= source.Length + 1; offset++)
        {
            if (!ends.Contains(offset) &&
                scan.TryGetSkipEndingAt(offset, out _))
            {
                problems.Add($"a span ends at {offset} that no span was found to");
            }
        }

        return (spans, comments, problems);
    }
}
