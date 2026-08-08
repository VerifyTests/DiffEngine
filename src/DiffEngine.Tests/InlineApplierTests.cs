public class InlineApplierTests
{
    static string WriteTemp(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"InlineApplierTests_{Guid.NewGuid():N}.cs");
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

    const string source = "class C\n{\n    void M() => VerifyInline(value, \"old\");\n}";

    [Test]
    public async Task Utf8BomPreserved()
    {
        var path = WriteTemp(Utf8(source, bom: true));
        try
        {
            var result = InlineApplier.Apply(new(path, 3, "\"old\"", "new"));
            await Assert.That(result.Status).IsEqualTo(InlineApplyStatus.Applied);
            var bytes = File.ReadAllBytes(path);
            await Assert.That(bytes[0]).IsEqualTo((byte)0xEF);
            await Assert.That(bytes[1]).IsEqualTo((byte)0xBB);
            await Assert.That(bytes[2]).IsEqualTo((byte)0xBF);
            await Assert.That(File.ReadAllText(path)).Contains("new");
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
            var result = InlineApplier.Apply(new(path, 3, "\"old\"", "new"));
            await Assert.That(result.Status).IsEqualTo(InlineApplyStatus.Applied);
            var bytes = File.ReadAllBytes(path);
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
        var path = WriteTemp(encoding.GetPreamble().Concat(encoding.GetBytes(source)).ToArray());
        try
        {
            var result = InlineApplier.Apply(new(path, 3, "\"old\"", "new"));
            await Assert.That(result.Status).IsEqualTo(InlineApplyStatus.Applied);
            var bytes = File.ReadAllBytes(path);
            await Assert.That(bytes[0]).IsEqualTo((byte)0xFF);
            await Assert.That(bytes[1]).IsEqualTo((byte)0xFE);
            await Assert.That(File.ReadAllText(path, encoding)).Contains("new");
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
            var result = InlineApplier.Apply(new(path, 3, "\"old\"", "a\nb"));
            await Assert.That(result.Status).IsEqualTo(InlineApplyStatus.Applied);
            var text = File.ReadAllText(path);
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
            var result = InlineApplier.Apply(new(path, 3, "\"old\"", content));
            await Assert.That(result.Status).IsEqualTo(InlineApplyStatus.Applied);

            var bytes = File.ReadAllBytes(path);
            var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            await Assert.That(hasBom).IsEqualTo(bom);

            var text = File.ReadAllText(path);
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
            var result = InlineApplier.Apply(new(path, 3, "\"old\"", "a\nb"));
            await Assert.That(result.Status).IsEqualTo(InlineApplyStatus.Applied);
            await Assert.That(File.ReadAllText(path)).DoesNotContain("\r");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task MissingFileFails()
    {
        var result = InlineApplier.Apply(new(Path.Combine(Path.GetTempPath(), "does-not-exist-inline.cs"), 1, null, "x"));
        await Assert.That(result.Status).IsEqualTo(InlineApplyStatus.Failed);
    }

    [Test]
    public async Task AlreadyAppliedDoesNotWrite()
    {
        var path = WriteTemp(Utf8(source, bom: false));
        try
        {
            var before = File.GetLastWriteTimeUtc(path);
            var result = InlineApplier.Apply(new(path, 3, "\"old\"", "old"));
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
        var multi = "class C\n{\n    void A() => VerifyInline(a, \"oldA\");\n    void B() => VerifyInline(b, \"oldB\");\n}";
        var path = WriteTemp(Utf8(multi, bom: false));
        try
        {
            var taskA = InlineApplier.ApplyAsync(new(path, 3, "\"oldA\"", "newA"));
            var taskB = InlineApplier.ApplyAsync(new(path, 4, "\"oldB\"", "newB"));
            var results = await Task.WhenAll(taskA, taskB);
            await Assert.That(results[0].Status).IsEqualTo(InlineApplyStatus.Applied);
            await Assert.That(results[1].Status).IsEqualTo(InlineApplyStatus.Applied);
            var text = File.ReadAllText(path);
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
        var multi = "class C\n{\n    void A() => VerifyInline(a, \"old\");\n    void B() => VerifyInline(b, \"old\");\n}";
        var path = WriteTemp(Utf8(multi, bom: false));
        try
        {
            var taskA = InlineApplier.ApplyAsync(new(path, 3, "\"old\"", "same"));
            var taskB = InlineApplier.ApplyAsync(new(path, 4, "\"old\"", "same"));
            var results = await Task.WhenAll(taskA, taskB);

            await Assert.That(results[0].Status).IsEqualTo(InlineApplyStatus.Applied);
            await Assert.That(results[1].Status).IsEqualTo(InlineApplyStatus.Applied);

            var text = File.ReadAllText(path);
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
        var multi = "class C\n{\n    void A() => VerifyInline(a, \"old\");\n    void B() => VerifyInline(b, \"old\");\n}";
        var path = WriteTemp(Utf8(multi, bom: false));
        try
        {
            var first = InlineApplier.Apply(new(path, 3, "\"old\"", "newA"));
            var second = InlineApplier.Apply(new(path, 4, "\"old\"", "newB"));

            await Assert.That(first.Status).IsEqualTo(InlineApplyStatus.Applied);
            await Assert.That(second.Status).IsEqualTo(InlineApplyStatus.Applied);

            var text = File.ReadAllText(path);
            await Assert.That(text).DoesNotContain("old");
            var indexA = text.IndexOf("VerifyInline(a", StringComparison.Ordinal);
            var indexB = text.IndexOf("VerifyInline(b", StringComparison.Ordinal);
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
            var result = InlineApplier.Apply(new(path, 3, "\"gone-expression\"", "new"));
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
        var patch = new InlinePatch(@"C:\proj\Tests.cs", 42, "\"\"\"\nold\n\"\"\"", "line1\nline2");
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
        var patch = new InlinePatch("Tests.cs", 1, null, "content");
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
        var read = InlinePatchFile.TryParse("version: 2\nsourceFile: x\nlineHint: 1\noriginalExpression:\nnewContent: YQ==\n", out _);
        await Assert.That(read).IsFalse();
    }
}
