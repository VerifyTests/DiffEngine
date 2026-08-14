public class InlineApplierTests
{
    static string WriteTemp(byte[] bytes, string extension = ".cs")
    {
        var path = Path.Combine(Path.GetTempPath(), $"InlineApplierTests_{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, bytes);
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
                "module Tests\n\nlet MyTest () =\n    Verifier.Verify(value).Snapshot(\"\"\"a\nb\"\"\").ToTask()\n");
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
