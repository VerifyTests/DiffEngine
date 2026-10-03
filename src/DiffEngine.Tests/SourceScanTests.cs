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

    /// <summary>
    /// What an edit is made of here: everything that opens, closes or escapes a comment or a
    /// literal in either language, line breaks of both kinds, and a backslash in front of one,
    /// which is the one thing a lexer reads across a line break from in front of it.
    /// </summary>
    static readonly string[] pieces =
    [
        "\"", "\"\"\"", "\"\"\"\"", "'", "\\", "\n", "\r\n", "\\\n", "'\\\n", "//", "/*", "*/", "(*", "*)", "(*)",
        "#", "#if X", "$\"", "$$\"\"\"", "@\"", "{", "}", "{{", "}}", "``", " ", "    ", "\t", "x", "'x'", "u8", "B",
        "Verify(value)", ".Snapshot(\"old\")", ";"
    ];

    /// <summary>
    /// A scan made from another by <see cref="SourceScan.Edited" /> is the scan of the whole
    /// text: every answer it gives, at every offset, after each of a run of edits made at random.
    /// A batch carries its scan from patch to patch this way, so anything the two disagree on is
    /// a patch told something other than what applying it by itself would have told it.
    /// </summary>
    [Test]
    [Arguments("cs")]
    [Arguments("fs")]
    public async Task AScanMadeFromAnotherIsTheScanOfTheWholeText(string extension)
    {
        var language = SourceLanguage.ForFile($"file.{extension}");
        var problems = new List<string>();
        for (var seed = 0; seed < 400 && problems.Count < 5; seed++)
        {
            var random = new Random(seed);
            // Each language's sample, and the other's, which is mostly not this one's syntax
            var other = seed % 4 == 3;
            var text = SourceLanguage.NormalizeNewlines(other == (extension == "cs") ? fsharp : csharp);
            var scan = language.Scan(text);
            try
            {
                for (var step = 0; step < 25; step++)
                {
                    // The line starts are carried only once they have been asked for
                    if (random.Next(2) == 0)
                    {
                        _ = scan.LineStarts;
                    }

                    var edited = Edit(random, text);
                    var next = scan.Edited(edited);
                    scan.Dispose();
                    scan = next;
                    text = edited;
                    using var whole = language.Scan(text);
                    if (Difference(scan, whole) is { } difference)
                    {
                        problems.Add($"seed {seed}, step {step}: {difference}");
                        break;
                    }
                }
            }
            finally
            {
                scan.Dispose();
            }
        }

        await Assert.That(problems).IsEmpty();
    }

    /// <summary>
    /// The same over a file of thousands of lines, where most of the scan is carried and the
    /// lexer has a long way to go to rejoin it when an edit opens something.
    /// </summary>
    [Test]
    [Arguments("InlinePatcherTests.cs")]
    [Arguments("FsCompilerRoundTripTests.cs")]
    public async Task AScanMadeFromAnotherOfTheSuitesOwnSourceIsTheScanOfTheWholeText(string file)
    {
        var text = await File.ReadAllTextAsync(Path.Combine(SourceDirectory, file));
        var random = new Random(file.Length);
        var problems = new List<string>();
        var scan = SourceLanguage.CSharp.Scan(text);
        try
        {
            for (var step = 0; step < 60; step++)
            {
                if (step == 20)
                {
                    _ = scan.LineStarts;
                }

                var edited = Edit(random, text);
                var next = scan.Edited(edited);
                scan.Dispose();
                scan = next;
                text = edited;
                using var whole = SourceLanguage.CSharp.Scan(text);
                if (Difference(scan, whole) is { } difference)
                {
                    problems.Add($"step {step}: {difference}");
                    break;
                }
            }
        }
        finally
        {
            scan.Dispose();
        }

        await Assert.That(problems).IsEmpty();
    }

    static string Edit(Random random, string text)
    {
        var start = random.Next(text.Length + 1);
        var length = random.Next(3) == 0 ? 0 : random.Next(Math.Min(12, text.Length - start) + 1);
        var replacement = new StringBuilder();
        for (var count = random.Next(4); count > 0; count--)
        {
            replacement.Append(pieces[random.Next(pieces.Length)]);
        }

        return text[..start] + replacement + text[(start + length)..];
    }

    /// <summary>
    /// The first thing two scans of one text answer differently, or null when there is none.
    /// </summary>
    static string? Difference(SourceScan made, SourceScan whole)
    {
        for (var offset = 0; offset <= whole.Source.Length + 1; offset++)
        {
            if (made.IsCode(offset) != whole.IsCode(offset))
            {
                return $"{offset} is code to one and not the other";
            }

            if (made.TryGetSkip(offset, out var madeEnd) != whole.TryGetSkip(offset, out var wholeEnd) ||
                madeEnd != wholeEnd)
            {
                return $"the span starting at {offset} ends at {madeEnd} and at {wholeEnd}";
            }

            if (made.TryGetCommentSkip(offset, out madeEnd) != whole.TryGetCommentSkip(offset, out wholeEnd) ||
                madeEnd != wholeEnd)
            {
                return $"the comment starting at {offset} ends at {madeEnd} and at {wholeEnd}";
            }

            if (made.TryGetSkipEndingAt(offset, out var madeStart) != whole.TryGetSkipEndingAt(offset, out var wholeStart) ||
                madeStart != wholeStart)
            {
                return $"the span ending at {offset} starts at {madeStart} and at {wholeStart}";
            }

            if (made.TryGetCommentEndingAt(offset, out madeStart) != whole.TryGetCommentEndingAt(offset, out wholeStart) ||
                madeStart != wholeStart)
            {
                return $"the comment ending at {offset} starts at {madeStart} and at {wholeStart}";
            }
        }

        if (!made.LineStarts.SequenceEqual(whole.LineStarts))
        {
            return "the lines start in different places";
        }

        if (made.Eol != whole.Eol ||
            made.IndentUnit != whole.IndentUnit)
        {
            return "the line break or the indentation differs";
        }

        return null;
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
