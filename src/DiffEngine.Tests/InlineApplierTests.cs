public class InlineApplierTests
{
    static string WriteTemp(byte[] bytes, string extension = ".cs")
    {
        var path = Path.Combine(Path.GetTempPath(), $"InlineApplierTests_{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    // A directory of its own, for the tests that are about what is left in one
    static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"InlineApplierTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    static byte[] Utf8(string text, bool bom)
    {
        var encoding = new UTF8Encoding(bom);
        var content = encoding.GetBytes(text);
        if (!bom)
        {
            return content;
        }

        var preamble = encoding.GetPreamble();
        var result = new byte[preamble.Length + content.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(content, 0, result, preamble.Length, content.Length);
        return result;
    }

    const string source = "class C\n{\n    void M() => Verify(value).Snapshot(\"old\");\n}";

    // Nothing here queues a patch, so none of them has a reviewable identity. Stated once rather
    // than at every call site below.
    static InlinePatch Patch(
        string sourceFile,
        int lineHint,
        string? originalExpression,
        string newContent,
        InlinePatchMode mode = InlinePatchMode.Set) =>
        new(sourceFile, lineHint, originalExpression, newContent, mode)
        {
            TestName = null
        };

    [Test]
    public async Task Utf8BomPreserved()
    {
        var path = WriteTemp(Utf8(source, bom: true));
        try
        {
            var result = InlineApplier.Apply(Patch(path, 3, "\"old\"", "new"));
            await Assert.That(result.Status).IsEqualTo(InlineApplyStatus.Applied);
            var bytes = await File.ReadAllBytesAsync(path);
            await Assert.That(bytes[0]).IsEqualTo((byte)0xEF);
            await Assert.That(bytes[1]).IsEqualTo((byte)0xBB);
            await Assert.That(bytes[2]).IsEqualTo((byte)0xBF);
            await Assert.That(await File.ReadAllTextAsync(path)).Contains("new");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task NoBomStaysNoBom()
    {
        var path = WriteTemp(Utf8(source, bom: false));
        try
        {
            var result = InlineApplier.Apply(Patch(path, 3, "\"old\"", "new"));
            await Assert.That(result.Status).IsEqualTo(InlineApplyStatus.Applied);
            var bytes = await File.ReadAllBytesAsync(path);
            await Assert.That(bytes[0]).IsEqualTo((byte)'c');
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task Utf16Preserved()
    {
        var encoding = new UnicodeEncoding(false, true);
        var path = WriteTemp([.. encoding.GetPreamble(), .. encoding.GetBytes(source)]);
        try
        {
            var result = InlineApplier.Apply(Patch(path, 3, "\"old\"", "new"));
            await Assert.That(result.Status).IsEqualTo(InlineApplyStatus.Applied);
            var bytes = await File.ReadAllBytesAsync(path);
            await Assert.That(bytes[0]).IsEqualTo((byte)0xFF);
            await Assert.That(bytes[1]).IsEqualTo((byte)0xFE);
            await Assert.That(await File.ReadAllTextAsync(path, encoding)).Contains("new");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// DetectEncoding has a branch for this and nothing exercised it. The BOM is four bytes and
    /// its first two are UTF-16's, so a reader that stopped at two would decode the whole file as
    /// the wrong encoding and write it back that way.
    /// </summary>
    [Test]
    public async Task Utf32Preserved()
    {
        var encoding = new UTF32Encoding(false, true);
        var path = WriteTemp([.. encoding.GetPreamble(), .. encoding.GetBytes(source)]);
        try
        {
            var result = InlineApplier.Apply(Patch(path, 3, "\"old\"", "new"));

            await Assert.That(result.Status).IsEqualTo(InlineApplyStatus.Applied);
            var bytes = await File.ReadAllBytesAsync(path);
            await Assert.That(bytes[0]).IsEqualTo((byte) 0xFF);
            await Assert.That(bytes[1]).IsEqualTo((byte) 0xFE);
            await Assert.That(bytes[2]).IsEqualTo((byte) 0x00);
            await Assert.That(bytes[3]).IsEqualTo((byte) 0x00);
            await Assert.That(await File.ReadAllTextAsync(path, encoding)).Contains("new");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The whole way through, rather than at the renderer: content holding a line terminator
    /// reaches the file as an escape. Written in as itself it ends the literal and the file stops
    /// compiling, which is a thing no test that stops at the rendered string can see.
    /// </summary>
    [Test]
    [Arguments(0x85)]
    [Arguments(0x2028)]
    [Arguments(0x2029)]
    public async Task LineTerminatorContentIsEscapedIntoTheFile(int codePoint)
    {
        var terminator = (char) codePoint;
        var content = $"a{terminator}b";
        var path = WriteTemp(Utf8(source, bom: false));
        try
        {
            var result = InlineApplier.Apply(Patch(path, 3, "\"old\"", content));

            await Assert.That(result.Status).IsEqualTo(InlineApplyStatus.Applied);
            var patched = await File.ReadAllTextAsync(path);
            await Assert.That(patched).Contains($"\\u{codePoint:x4}");
            await Assert.That(patched).DoesNotContain(terminator.ToString());

            // And reads back as the content it came from. AlreadyApplied is only reachable by
            // parsing the literal just written and finding the same value, so it says the round
            // trip closed without this test having to take the literal apart itself
            var again = InlineApplier.Apply(Patch(path, 3, null, content));
            await Assert.That(again.Status).IsEqualTo(InlineApplyStatus.AlreadyApplied);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // The swap goes through a sibling temporary. It is gone by the time the patch is applied,
    // whatever else is true, because a stray file in a source directory is the caller's problem
    [Test]
    public async Task LeavesNoTemporaryBehind()
    {
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "Sample.cs");
            File.WriteAllBytes(path, Utf8(source, bom: false));

            var result = InlineApplier.Apply(Patch(path, 3, "\"old\"", "new"));

            await Assert.That(result.Status).IsEqualTo(InlineApplyStatus.Applied);
            await Assert.That(Directory.GetFileSystemEntries(directory)).IsEquivalentTo([path]);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    // Writing in place truncates first, so a reader - or a process that stops partway, which is
    // the case this stands in for - could see a file with its tail missing. Reading alongside the
    // apply can only fail when that window is real, so it never goes red on timing alone
    [Test]
    public async Task ContentIsNeverObservedHalfWritten()
    {
        var padding = string.Join("\n", Enumerable.Repeat("    // padding, to widen the window a truncating write would open", 30000));
        var text = $"class C\n{{\n    void M() => Verify(value).Snapshot(\"old\");\n{padding}\n}}";
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "Big.cs");
            File.WriteAllBytes(path, Utf8(text, bom: false));
            var before = new FileInfo(path).Length;

            using var cancellation = new CancellationTokenSource();
            var seen = new ConcurrentDictionary<long, byte>();
            var reader = Task.Run(
                () =>
                {
                    while (!cancellation.IsCancellationRequested)
                    {
                        try
                        {
                            // Shared every way, so watching the file never blocks the write being
                            // watched. Holding it any other way tests which of the two wins the
                            // file rather than what a reader of it sees
                            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                            seen.TryAdd(stream.Length, 0);
                        }
                        catch (Exception exception)
                            when (exception is IOException or UnauthorizedAccessException)
                        {
                            // The swap is in flight. Not an observation of the content
                        }
                    }
                });

            // Long enough that the two whole files differ in length, which is what makes a
            // half written one tell itself apart
            var result = InlineApplier.Apply(Patch(path, 3, "\"old\"", "a replacement longer than what it replaces"));
            cancellation.Cancel();
            await reader;

            await Assert.That(result.Status).IsEqualTo(InlineApplyStatus.Applied);
            var after = new FileInfo(path).Length;
            await Assert.That(after).IsNotEqualTo(before);

            var partial = seen.Keys.Where(_ => _ != before && _ != after).ToList();
            await Assert.That(partial).IsEmpty();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    // Swapping the file in takes the path away for the instant it takes to rename over it. An
    // applier waiting its turn must not read that as a file that is not there, which is what
    // asking before taking the lock did
    [Test]
    public async Task ConcurrentAppliesNeverReportAMissingFile()
    {
        const int writers = 4;
        const int rounds = 60;
        var directory = NewDirectory();
        try
        {
            // A snapshot each, flipped back and forth, so every apply is a real write and the
            // file is swapped out from under the others hundreds of times. The window this is
            // about is microseconds wide and no arrangement here reproduces it on demand - it
            // showed up as ParallelAppliesToSameFile going red under the load of the rest of this
            // class. What is pinned is the contract: contended appliers all get their write, and
            // none of them reports the file as gone
            var methods = Enumerable.Range(0, writers)
                .Select(_ => $"    void M{_}() => Verify(v).Snapshot(\"a{_}\");");
            var path = Path.Combine(directory, "Contended.cs");
            File.WriteAllBytes(path, Utf8($"class C\n{{\n{string.Join("\n", methods)}\n}}", bom: false));

            var results = await Task.WhenAll(
                Enumerable
                    .Range(0, writers)
                    .Select(
                        index => Task.Run(
                            () =>
                            {
                                var outcomes = new List<InlineApplyResult>();
                                for (var round = 0; round < rounds; round++)
                                {
                                    var (from, to) = round % 2 == 0
                                        ? ($"a{index}", $"b{index}")
                                        : ($"b{index}", $"a{index}");
                                    outcomes.Add(InlineApplier.Apply(Patch(path, index + 3, $"\"{from}\"", to)));
                                }

                                return outcomes;
                            })));

            var failures = results
                .SelectMany(_ => _)
                .Where(_ => _.Status == InlineApplyStatus.Failed)
                .Select(_ => _.Message)
                .ToList();
            await Assert.That(failures).IsEmpty();

            // And none of them lost another's line along the way
            var text = await File.ReadAllTextAsync(path);
            for (var index = 0; index < writers; index++)
            {
                await Assert.That(text).Contains($"void M{index}()");
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    // The same file with no Snapshot call yet, for the append case below
    const string appendable = "class C\n{\n    void M() => Verify(value);\n}";

    /// <summary>
    /// The file ends the way it began, whichever way that is. A final newline gained or lost is a
    /// line in the diff of every commit that touches the file afterwards, so it is worth saying out
    /// loud rather than leaving to hold by construction, which is all it does today: splicing
    /// builds prefix, replacement, suffix, so it never reaches the end of the file, and the write
    /// appends nothing.
    /// <para>
    /// Two tests here would catch a blanket change incidentally, by comparing whole file text —
    /// <see cref="EolAndBomCombinations"/> in the direction of gaining one,
    /// <see cref="FSharpFileGetsAnFSharpLiteral"/> in the direction of losing one. Both patch in
    /// <see cref="InlinePatchMode.Set"/>. Neither covers the modes that rewrite around a call
    /// rather than inside a literal, and damage confined to those is caught by nothing else.
    /// </para>
    /// </summary>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TrailingNewlineIsLeftAsItWas(bool trailing)
    {
        var suffix = trailing ? "\n" : "";

        // Replacing a literal, writing a Snapshot call where there was none, and taking one away
        await Assert.That(EndsWithNewline(source + suffix, 3, "\"old\"", "new", InlinePatchMode.Set)).IsEqualTo(trailing);
        await Assert.That(EndsWithNewline(appendable + suffix, 3, null, "new", InlinePatchMode.Append)).IsEqualTo(trailing);
        await Assert.That(EndsWithNewline(source + suffix, 3, null, "", InlinePatchMode.Remove)).IsEqualTo(trailing);
    }

    static bool EndsWithNewline(string text, int line, string? expression, string content, InlinePatchMode mode)
    {
        var path = WriteTemp(Utf8(text, bom: false));
        try
        {
            var result = InlineApplier.Apply(Patch(path, line, expression, content, mode));
            if (result.Status != InlineApplyStatus.Applied)
            {
                throw new($"{mode} was not applied: {result.Message}");
            }

            return File.ReadAllText(path).EndsWith('\n');
        }
        finally
        {
            File.Delete(path);
        }
    }

    // The whole file is rewritten, not just the patched span, so a byte that does not decode
    // would come back as a replacement character everywhere it appears
    [Test]
    public async Task NonUtf8FileIsRefused()
    {
        byte[] bytes =
        [
            .. Utf8("class C\n{\n    // caf", bom: false),
            0xE9, // é in Latin-1, not valid UTF-8
            .. Utf8("\n    void M() => Verify(value).Snapshot(\"old\");\n}", bom: false)
        ];
        var path = WriteTemp(bytes);
        try
        {
            var result = InlineApplier.Apply(Patch(path, 4, "\"old\"", "new"));

            await Assert.That(result.Status).IsEqualTo(InlineApplyStatus.Failed);
            await Assert.That(result.Message!).Contains("Convert it to UTF-8");
            await Assert.That((await File.ReadAllBytesAsync(path)).SequenceEqual(bytes)).IsTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task NonAsciiUtf8IsPreserved()
    {
        var text = "class C\n{\n    // café ☕\n    void M() => Verify(value).Snapshot(\"old\");\n}";
        var path = WriteTemp(Utf8(text, bom: false));
        try
        {
            var result = InlineApplier.Apply(Patch(path, 4, "\"old\"", "naïve ☕"));

            await Assert.That(result.Status).IsEqualTo(InlineApplyStatus.Applied);
            var after = await File.ReadAllTextAsync(path);
            await Assert.That(after).Contains("// café ☕");
            await Assert.That(after).Contains("naïve ☕");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task CrlfPreserved()
    {
        var path = WriteTemp(Utf8(source.Replace("\n", "\r\n"), bom: false));
        try
        {
            var result = InlineApplier.Apply(Patch(path, 3, "\"old\"", "a\nb"));
            await Assert.That(result.Status).IsEqualTo(InlineApplyStatus.Applied);
            var text = await File.ReadAllTextAsync(path);
            await Assert.That(text).DoesNotContain("a\nb");
            await Assert.That(text).Contains("a\r\n");
        }
        finally
        {
            File.Delete(path);
        }
    }

    // fileEol x contentEol, with both bom states, asserting the file keeps a single
    // consistent line ending and the BOM state is untouched
    [Test]
    [Arguments("\r\n", "\r\n", true)]
    [Arguments("\r\n", "\n", true)]
    [Arguments("\r\n", "\r\n", false)]
    [Arguments("\r\n", "\n", false)]
    [Arguments("\n", "\r\n", true)]
    [Arguments("\n", "\n", true)]
    [Arguments("\n", "\r\n", false)]
    [Arguments("\n", "\n", false)]
    public async Task EolAndBomCombinations(string fileEol, string contentEol, bool bom)
    {
        var path = WriteTemp(Utf8(source.Replace("\n", fileEol), bom));
        try
        {
            var content = "a" + contentEol + "b";
            var result = InlineApplier.Apply(Patch(path, 3, "\"old\"", content));
            await Assert.That(result.Status).IsEqualTo(InlineApplyStatus.Applied);

            var bytes = await File.ReadAllBytesAsync(path);
            var hasBom = bytes is [0xEF, 0xBB, 0xBF, ..];
            await Assert.That(hasBom).IsEqualTo(bom);

            var text = await File.ReadAllTextAsync(path);
            await Assert.That(text).Contains("a" + fileEol);
            await Assert.That(text).Contains("b");
            await Assert.That(text).DoesNotContain("old");

            // No stray endings: every \r is part of the file ending, and when the file
            // is LF there are no \r at all
            for (var index = 0; index < text.Length; index++)
            {
                if (text[index] == '\r')
                {
                    await Assert.That(fileEol).IsEqualTo("\r\n");
                    await Assert.That(index + 1 < text.Length && text[index + 1] == '\n').IsTrue();
                }
                else if (text[index] == '\n' && fileEol == "\r\n")
                {
                    await Assert.That(index > 0 && text[index - 1] == '\r').IsTrue();
                }
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task LfFileIsNotConvertedToCrlf()
    {
        var path = WriteTemp(Utf8(source, bom: false));
        try
        {
            var result = InlineApplier.Apply(Patch(path, 3, "\"old\"", "a\nb"));
            await Assert.That(result.Status).IsEqualTo(InlineApplyStatus.Applied);
            await Assert.That(await File.ReadAllTextAsync(path)).DoesNotContain("\r");
        }
        finally
        {
            File.Delete(path);
        }
    }

    // A writer that encodes with a preamble, such as Process.StandardInput on .NET Framework,
    // prefixes the payload with a BOM
    [Test]
    public async Task TryParseToleratesLeadingBom()
    {
        var patch = Patch(@"C:\proj\Tests.cs", 7, "\"old\"", "new content");
        var payload = "﻿" + InlinePatchFile.Build(patch);

        var read = InlinePatchFile.TryParse(payload, out var result);

        await Assert.That(read).IsTrue();
        await Assert.That(result!.SourceFile).IsEqualTo(patch.SourceFile);
        await Assert.That(result.LineHint).IsEqualTo(7);
        await Assert.That(result.NewContent).IsEqualTo("new content");
    }

    // The extension picks the language, so the same patch content is written as the literal that
    // file's compiler reads. A C# raw string here would not even parse
    [Test]
    [Arguments(".fs")]
    [Arguments(".fsx")]
    [Arguments(".FS")]
    public async Task FSharpFileGetsAnFSharpLiteral(string extension)
    {
        var fsharp = "module Tests\n\nlet MyTest () =\n    Verifier.Verify(value).Snapshot(\"old\").ToTask()\n";
        var path = WriteTemp(Utf8(fsharp, bom: false), extension);
        try
        {
            var result = InlineApplier.Apply(Patch(path, 4, "\"old\"", "a\nb"));

            await Assert.That(result.Status).IsEqualTo(InlineApplyStatus.Applied);
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(
                "module Tests\n\nlet MyTest () =\n    Verifier.Verify(value).Snapshot(\n        \"\"\"\n        a\n        b\n        \"\"\").ToTask()\n");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task MissingFileFails()
    {
        var result = InlineApplier.Apply(Patch(Path.Combine(Path.GetTempPath(), "does-not-exist-inline.cs"), 1, null, "x"));
        await Assert.That(result.Status).IsEqualTo(InlineApplyStatus.Failed);
    }

    [Test]
    public async Task AlreadyAppliedDoesNotWrite()
    {
        var path = WriteTemp(Utf8(source, bom: false));
        try
        {
            var before = File.GetLastWriteTimeUtc(path);
            var result = InlineApplier.Apply(Patch(path, 3, "\"old\"", "old"));
            await Assert.That(result.Status).IsEqualTo(InlineApplyStatus.AlreadyApplied);
            await Assert.That(File.GetLastWriteTimeUtc(path)).IsEqualTo(before);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ParallelAppliesToSameFile()
    {
        var multi = "class C\n{\n    void A() => Verify(a).Snapshot(\"oldA\");\n    void B() => Verify(b).Snapshot(\"oldB\");\n}";
        var path = WriteTemp(Utf8(multi, bom: false));
        try
        {
            var taskA = Task.Run(() => InlineApplier.Apply(Patch(path, 3, "\"oldA\"", "newA")));
            var taskB = Task.Run(() => InlineApplier.Apply(Patch(path, 4, "\"oldB\"", "newB")));
            var results = await Task.WhenAll(taskA, taskB);
            await Assert.That(results[0].Status).IsEqualTo(InlineApplyStatus.Applied);
            await Assert.That(results[1].Status).IsEqualTo(InlineApplyStatus.Applied);
            var text = await File.ReadAllTextAsync(path);
            await Assert.That(text).Contains("newA");
            await Assert.That(text).Contains("newB");
        }
        finally
        {
            File.Delete(path);
        }
    }

    static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while (true)
        {
            index = text.IndexOf(value, index, StringComparison.Ordinal);
            if (index < 0)
            {
                return count;
            }

            count++;
            index += value.Length;
        }
    }

    // Two tests in the same file producing the same result, accepted concurrently.
    // Both sites must end up patched: neither apply may claim the other's literal.
    [Test]
    public async Task ParallelAppliesWithIdenticalLiterals()
    {
        var multi = "class C\n{\n    void A() => Verify(a).Snapshot(\"old\");\n    void B() => Verify(b).Snapshot(\"old\");\n}";
        var path = WriteTemp(Utf8(multi, bom: false));
        try
        {
            var taskA = Task.Run(() => InlineApplier.Apply(Patch(path, 3, "\"old\"", "same")));
            var taskB = Task.Run(() => InlineApplier.Apply(Patch(path, 4, "\"old\"", "same")));
            var results = await Task.WhenAll(taskA, taskB);

            await Assert.That(results[0].Status).IsEqualTo(InlineApplyStatus.Applied);
            await Assert.That(results[1].Status).IsEqualTo(InlineApplyStatus.Applied);

            var text = await File.ReadAllTextAsync(path);
            await Assert.That(text).DoesNotContain("old");
            await Assert.That(Count(text, "same")).IsEqualTo(2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task SequentialAppliesWithIdenticalLiterals()
    {
        var multi = "class C\n{\n    void A() => Verify(a).Snapshot(\"old\");\n    void B() => Verify(b).Snapshot(\"old\");\n}";
        var path = WriteTemp(Utf8(multi, bom: false));
        try
        {
            var first = InlineApplier.Apply(Patch(path, 3, "\"old\"", "newA"));
            var second = InlineApplier.Apply(Patch(path, 4, "\"old\"", "newB"));

            await Assert.That(first.Status).IsEqualTo(InlineApplyStatus.Applied);
            await Assert.That(second.Status).IsEqualTo(InlineApplyStatus.Applied);

            var text = await File.ReadAllTextAsync(path);
            await Assert.That(text).DoesNotContain("old");
            var indexA = text.IndexOf("Verify(a)", StringComparison.Ordinal);
            var indexB = text.IndexOf("Verify(b)", StringComparison.Ordinal);
            var segmentA = text.Substring(indexA, indexB - indexA);
            await Assert.That(segmentA).Contains("newA");
            await Assert.That(segmentA).DoesNotContain("newB");
            await Assert.That(text.Substring(indexB)).Contains("newB");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task NotFoundWhenSourceChanged()
    {
        var path = WriteTemp(Utf8(source, bom: false));
        try
        {
            var result = InlineApplier.Apply(Patch(path, 3, "\"gone-expression\"", "new"));
            await Assert.That(result.Status).IsEqualTo(InlineApplyStatus.NotFound);
            await Assert.That(result.Message!).Contains("Re-run the test");
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class InlinePatchFileTests
{
    [Test]
    public async Task RoundTrip()
    {
        var patch = new InlinePatch(@"C:\proj\Tests.cs", 42, "\"\"\"\nold\n\"\"\"", "line1\nline2")
        {
            TestName = null
        };
        var path = Path.Combine(Path.GetTempPath(), $"InlinePatchFileTests_{Guid.NewGuid():N}.inlinepatch");
        try
        {
            InlinePatchFile.Write(path, patch);
            var read = InlinePatchFile.TryRead(path, out var result);
            await Assert.That(read).IsTrue();
            await Assert.That(result!.SourceFile).IsEqualTo(patch.SourceFile);
            await Assert.That(result.LineHint).IsEqualTo(42);
            await Assert.That(result.OriginalExpression).IsEqualTo(patch.OriginalExpression);
            await Assert.That(result.NewContent).IsEqualTo(patch.NewContent);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task RoundTripNullExpression()
    {
        var patch = new InlinePatch("Tests.cs", 1, null, "content")
        {
            TestName = null
        };
        var path = Path.Combine(Path.GetTempPath(), $"InlinePatchFileTests_{Guid.NewGuid():N}.inlinepatch");
        try
        {
            InlinePatchFile.Write(path, patch);
            var read = InlinePatchFile.TryRead(path, out var result);
            await Assert.That(read).IsTrue();
            await Assert.That(result!.OriginalExpression).IsNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task RoundTripOriginalValue()
    {
        var patch = new InlinePatch("Tests.fs", 4, null, "new")
        {
            TestName = null,
            OriginalValue = "old line1\nold line2"
        };

        var read = InlinePatchFile.TryParse(InlinePatchFile.Build(patch), out var result);

        await Assert.That(read).IsTrue();
        await Assert.That(result!.OriginalValue).IsEqualTo(patch.OriginalValue);
    }

    [Test]
    public async Task RoundTripMemberName()
    {
        var patch = new InlinePatch("Tests.fs", 4, null, "new")
        {
            TestName = null,
            MemberName = "MyTest"
        };

        var read = InlinePatchFile.TryParse(InlinePatchFile.Build(patch), out var result);

        await Assert.That(read).IsTrue();
        await Assert.That(result!.MemberName).IsEqualTo("MyTest");
    }

    [Test]
    public async Task RoundTripNullOriginalValue()
    {
        var read = InlinePatchFile.TryParse(InlinePatchFile.Build(new("Tests.cs", 1, null, "content") { TestName = null }), out var result);

        await Assert.That(read).IsTrue();
        await Assert.That(result!.OriginalValue).IsNull();
    }

    // The field sits past the six fixed lines, so a payload written before it existed still parses
    [Test]
    public async Task PayloadWithoutOriginalValue()
    {
        var read = InlinePatchFile.TryParse(
            "version: 2\nsourceFile: x\nlineHint: 1\nmode: Set\noriginalExpression:\nnewContent: YQ==\ntestName:\nframework: net9.0\n",
            out var result);

        await Assert.That(read).IsTrue();
        await Assert.That(result!.OriginalValue).IsNull();
        await Assert.That(result.MemberName).IsNull();
        await Assert.That(result.Framework).IsEqualTo("net9.0");
    }

    [Test]
    [Arguments(InlinePatchMode.Set)]
    [Arguments(InlinePatchMode.Append)]
    [Arguments(InlinePatchMode.Remove)]
    public async Task RoundTripMode(InlinePatchMode mode)
    {
        var patch = new InlinePatch("Tests.cs", 1, null, "content", mode)
        {
            TestName = null
        };

        var read = InlinePatchFile.TryParse(InlinePatchFile.Build(patch), out var result);

        await Assert.That(read).IsTrue();
        await Assert.That(result!.Mode).IsEqualTo(mode);
    }

    [Test]
    public async Task DefaultModeIsSet()
    {
        var read = InlinePatchFile.TryParse(InlinePatchFile.Build(new("Tests.cs", 1, null, "content") { TestName = null }), out var result);

        await Assert.That(read).IsTrue();
        await Assert.That(result!.Mode).IsEqualTo(InlinePatchMode.Set);
    }

    // The version 1 shape, which had no mode line
    [Test]
    public async Task PreviousVersionFails()
    {
        var read = InlinePatchFile.TryParse("version: 1\nsourceFile: x\nlineHint: 1\noriginalExpression:\nnewContent: YQ==\n", out _);
        await Assert.That(read).IsFalse();
    }

    [Test]
    public async Task UnknownModeFails()
    {
        var read = InlinePatchFile.TryParse("version: 2\nsourceFile: x\nlineHint: 1\nmode: Sideways\noriginalExpression:\nnewContent: YQ==\n", out _);
        await Assert.That(read).IsFalse();
    }

    /// <summary>
    /// The sending process stamps its own framework, and does it onto the payload rather than onto
    /// the caller's patch. The fallback keeps every other caller - a listing rebuilding a payload
    /// from a queued patch - writing the framework the patch was born with.
    /// </summary>
    [Test]
    public async Task BuildTakesTheFrameworkGiven()
    {
        var stamped = InlinePatchFile.Build(
            new("Tests.cs", 1, null, "content")
            {
                TestName = null
            },
            "net9.0");
        await Assert.That(InlinePatchFile.TryParse(stamped, out var read)).IsTrue();
        await Assert.That(read!.Framework).IsEqualTo("net9.0");

        // Nothing given, so the patch's own stands
        var own = InlinePatchFile.Build(
            new("Tests.cs", 1, null, "content")
            {
                TestName = null,
                Framework = "net8.0"
            });
        await Assert.That(InlinePatchFile.TryParse(own, out var fallback)).IsTrue();
        await Assert.That(fallback!.Framework).IsEqualTo("net8.0");
    }

    // A number parses as an enum as readily as a name, so this used to arrive as an
    // InlinePatchMode that is none of them and behave as a Set
    [Test]
    [Arguments("7")]
    [Arguments("-1")]
    [Arguments("99")]
    public async Task UndefinedNumericModeFails(string mode)
    {
        var read = InlinePatchFile.TryParse($"version: 2\nsourceFile: x\nlineHint: 1\nmode: {mode}\noriginalExpression:\nnewContent: YQ==\n", out _);
        await Assert.That(read).IsFalse();
    }

    // The numbers that do name a mode still read, since that is how the enum has always been
    // written on the wire
    [Test]
    public async Task DefinedNumericModeReads()
    {
        var read = InlinePatchFile.TryParse($"version: 2\nsourceFile: x\nlineHint: 1\nmode: {(int) InlinePatchMode.Append}\noriginalExpression:\nnewContent: YQ==\n", out var patch);
        await Assert.That(read).IsTrue();
        await Assert.That(patch!.Mode).IsEqualTo(InlinePatchMode.Append);
    }

    // Both ride the payload as themselves, so a line break in either ends the line and the rest
    // is read as more of the payload: the fixed lines shift, or a trailing field is forged
    [Test]
    public async Task ALineBreakInAFieldThatIsNotEncodedIsRefused()
    {
        await Assert.That(
                () => InlinePatchFile.Build(
                    new("a\nb.cs", 1, null, "new", InlinePatchMode.Set)
                    {
                        TestName = null
                    }))
            .Throws<ArgumentException>();
        await Assert.That(
                () => InlinePatchFile.Build(
                    new("a.cs", 1, null, "new", InlinePatchMode.Set)
                    {
                        TestName = null,
                        Framework = "net8.0\noriginalValue: forged"
                    }))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task MissingFileFails()
    {
        var read = InlinePatchFile.TryRead(Path.Combine(Path.GetTempPath(), "missing.inlinepatch"), out _);
        await Assert.That(read).IsFalse();
    }

    [Test]
    public async Task GarbageFails()
    {
        var read = InlinePatchFile.TryParse("not a patch", out _);
        await Assert.That(read).IsFalse();
    }

    [Test]
    public async Task WrongVersionFails()
    {
        var read = InlinePatchFile.TryParse("version: 3\nsourceFile: x\nlineHint: 1\nmode: Set\noriginalExpression:\nnewContent: YQ==\n", out _);
        await Assert.That(read).IsFalse();
    }

    [Test]
    public async Task MetadataRoundTrips()
    {
        var patch = new InlinePatch("Tests.cs", 1, null, "content")
        {
            TestName = "Compare handles nulls",
            Framework = "net9.0"
        };

        var read = InlinePatchFile.TryParse(InlinePatchFile.Build(patch), out var result);

        await Assert.That(read).IsTrue();
        await Assert.That(result!.TestName).IsEqualTo("Compare handles nulls");
        await Assert.That(result.Framework).IsEqualTo("net9.0");
    }

    [Test]
    public async Task NullMetadataRoundTripsAsNull()
    {
        var read = InlinePatchFile.TryParse(InlinePatchFile.Build(new("Tests.cs", 1, null, "content") { TestName = null }), out var result);

        await Assert.That(read).IsTrue();
        await Assert.That(result!.TestName).IsNull();
        await Assert.That(result.Framework).IsNull();
    }

    /// <summary>
    /// A payload with only the six fixed lines still parses; parsers never invent metadata, so
    /// both fields come back null rather than being stamped by the reading process.
    /// </summary>
    [Test]
    public async Task AbsentMetadataParsesAsNull()
    {
        var read = InlinePatchFile.TryParse("version: 2\nsourceFile: x\nlineHint: 1\nmode: Set\noriginalExpression:\nnewContent: YQ==\n", out var result);

        await Assert.That(read).IsTrue();
        await Assert.That(result!.TestName).IsNull();
        await Assert.That(result.Framework).IsNull();
    }

    /// <summary>
    /// Test names are caller supplied, so the field has to survive the same hostile content the
    /// snapshot fields do.
    /// </summary>
    [Test]
    public async Task AnAwkwardTestNameSurvives()
    {
        var patch = new InlinePatch("Tests.cs", 1, null, "content")
        {
            TestName = "pipes | and\nnewlines and framework: lies"
        };

        var read = InlinePatchFile.TryParse(InlinePatchFile.Build(patch), out var result);

        await Assert.That(read).IsTrue();
        await Assert.That(result!.TestName).IsEqualTo("pipes | and\nnewlines and framework: lies");
    }

    [Test]
    public async Task MetadataOrderIsFlexible()
    {
        var read = InlinePatchFile.TryParse("version: 2\nsourceFile: x\nlineHint: 1\nmode: Set\noriginalExpression:\nnewContent: YQ==\nframework: net8.0\ntestName:\n", out var result);

        await Assert.That(read).IsTrue();
        await Assert.That(result!.Framework).IsEqualTo("net8.0");
        await Assert.That(result.TestName).IsNull();
    }

    [Test]
    public async Task UnknownTrailingLinesAreIgnored()
    {
        var payload = InlinePatchFile.Build(new("Tests.cs", 1, null, "content") { TestName = null }) + "future: value\n";

        var read = InlinePatchFile.TryParse(payload, out var result);

        await Assert.That(read).IsTrue();
        await Assert.That(result!.SourceFile).IsEqualTo("Tests.cs");
    }

    // Matching the strictness of the other encoded fields
    [Test]
    public async Task ABadTestNameBase64Fails()
    {
        var read = InlinePatchFile.TryParse("version: 2\nsourceFile: x\nlineHint: 1\nmode: Set\noriginalExpression:\nnewContent: YQ==\ntestName: not-base64!\n", out _);

        await Assert.That(read).IsFalse();
    }
}
