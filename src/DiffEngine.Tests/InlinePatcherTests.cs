public class InlinePatcherTests
{
    const string rawOld = "\"\"\"\n        old\n        \"\"\"";

    static string Method(string body) =>
        $"class Tests\n{{\n    async Task Test()\n    {{\n{body}\n    }}\n}}";

    [Test]
    public async Task ReplaceRawLiteral()
    {
        var source = Method($"        await VerifyInline(value, {rawOld.Replace("\n", "\n        ")});");
        var status = InlinePatcher.TryApply(source, 5, rawOld.Replace("\n", "\n        "), "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("new");
        await Assert.That(newSource).DoesNotContain("old");
        // Everything outside the span is untouched
        await Assert.That(newSource).Contains("class Tests");
        await Assert.That(newSource).Contains("await VerifyInline(value, ");
        await Assert.That(newSource.EndsWith(");\n    }\n}")).IsTrue();
    }

    [Test]
    public async Task ReplaceRegularLiteral()
    {
        var source = Method("        await VerifyInline(value, \"old\");");
        var status = InlinePatcher.TryApply(source, 5, "\"old\"", "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains(
            """
            await VerifyInline(value, ""
            """ + "\"");
        await Assert.That(newSource).Contains("            new");
    }

    [Test]
    public async Task ReplacementUsesFileEol()
    {
        var source = Method("        await VerifyInline(value, \"old\");").Replace("\n", "\r\n");
        var status = InlinePatcher.TryApply(source, 5, "\"old\"", "a\nb", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).DoesNotContain("a\nb");
        await Assert.That(newSource).Contains("a\r\n            b");
    }

    [Test]
    public async Task AlreadyAppliedWhenLiteralMatches()
    {
        var source = Method("        await VerifyInline(value, \"same\");");
        var status = InlinePatcher.TryApply(source, 5, "\"same\"", "same", out _, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.AlreadyApplied);
    }

    [Test]
    public async Task ShiftedLinesStillFound()
    {
        var padding = string.Concat(Enumerable.Repeat("        // padding\n", 30));
        var source = Method(padding + "        await VerifyInline(value, \"old\");");
        var status = InlinePatcher.TryApply(source, 5, "\"old\"", "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).DoesNotContain("\"old\"");
    }

    [Test]
    public async Task DuplicateLiteralsPicksNearestToHint()
    {
        var source =
            "await VerifyInline(a, \"dup\");\n" +
            string.Concat(Enumerable.Repeat("// filler\n", 10)) +
            "await VerifyInline(b, \"dup\");\n";
        var status = InlinePatcher.TryApply(source, 12, "\"dup\"", "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        // First occurrence untouched, second replaced
        await Assert.That(newSource).Contains("VerifyInline(a, \"dup\")");
        await Assert.That(newSource).DoesNotContain("VerifyInline(b, \"dup\")");
    }

    // Two call sites, A on line 4 and B on line 7
    static string TwoCallSites(string literalA, string literalB) =>
        string.Join(
            "\n",
            "class Tests",
            "{",
            "    void A() =>",
            $"        VerifyInline(a, {literalA});",
            "",
            "    void B() =>",
            $"        VerifyInline(b, {literalB});",
            "}");

    static (string a, string b) Segments(string text)
    {
        var indexA = text.IndexOf("VerifyInline(a", StringComparison.Ordinal);
        var indexB = text.IndexOf("VerifyInline(b", StringComparison.Ordinal);
        return (text.Substring(indexA, indexB - indexA), text.Substring(indexB));
    }

    [Test]
    public async Task DuplicateLiteralsPicksNearestToHintFirst()
    {
        var source = TwoCallSites("\"dup\"", "\"dup\"");

        var status = InlinePatcher.TryApply(source, 4, "\"dup\"", "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        var (a, b) = Segments(newSource);
        await Assert.That(a).Contains("new");
        await Assert.That(a).DoesNotContain("dup");
        // The other site is untouched
        await Assert.That(b).Contains("\"dup\"");
        await Assert.That(b).DoesNotContain("new");
    }

    [Test]
    public async Task EquidistantDuplicatesPreferAtOrAfterHint()
    {
        var source = string.Join(
            "\n",
            "class Tests",
            "{",
            "    void A() =>",
            "        VerifyInline(a, \"dup\");",
            "    void B() =>",
            "        VerifyInline(b, \"dup\");",
            "}");

        // Line 5 is equidistant from the sites on lines 4 and 6
        var status = InlinePatcher.TryApply(source, 5, "\"dup\"", "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        var (a, b) = Segments(newSource);
        await Assert.That(a).Contains("\"dup\"");
        await Assert.That(b).Contains("new");
    }

    // Two tests in the same file producing the same result: both sites must end up
    // patched, and the second apply must not mistake the first for its own
    [Test]
    public async Task SequentialPatchesOfIdenticalLiteralsSameContent()
    {
        var source = TwoCallSites("\"old\"", "\"old\"");

        var first = InlinePatcher.TryApply(source, 4, "\"old\"", "new", out var afterFirst, out _);
        var second = InlinePatcher.TryApply(afterFirst, 7, "\"old\"", "new", out var afterSecond, out var reason);

        await Assert.That(first).IsEqualTo(PatchStatus.Applied);
        await Assert.That(second).IsEqualTo(PatchStatus.Applied);
        await Assert.That(reason).IsEmpty();
        await Assert.That(afterSecond).DoesNotContain("old");
        var (a, b) = Segments(afterSecond);
        await Assert.That(a).Contains("new");
        await Assert.That(b).Contains("new");
    }

    [Test]
    public async Task SequentialPatchesOfIdenticalLiteralsDifferentContent()
    {
        var source = TwoCallSites("\"old\"", "\"old\"");

        var first = InlinePatcher.TryApply(source, 4, "\"old\"", "newA", out var afterFirst, out _);
        var second = InlinePatcher.TryApply(afterFirst, 7, "\"old\"", "newB", out var afterSecond, out _);

        await Assert.That(first).IsEqualTo(PatchStatus.Applied);
        await Assert.That(second).IsEqualTo(PatchStatus.Applied);
        var (a, b) = Segments(afterSecond);
        // Each site gets its own content, not the other's
        await Assert.That(a).Contains("newA");
        await Assert.That(a).DoesNotContain("newB");
        await Assert.That(b).Contains("newB");
        await Assert.That(b).DoesNotContain("newA");
    }

    // The first apply turns a single line literal into a multi line one, so the second
    // site has shifted down by the time its (stale) line hint is used
    [Test]
    public async Task SecondPatchSurvivesLineShiftFromTheFirst()
    {
        var source = TwoCallSites("\"old\"", "\"old\"");

        InlinePatcher.TryApply(source, 4, "\"old\"", "line1\nline2\nline3", out var afterFirst, out _);
        var lineShift = afterFirst.Split('\n').Length - source.Split('\n').Length;
        var second = InlinePatcher.TryApply(afterFirst, 7, "\"old\"", "newB", out var afterSecond, out _);

        await Assert.That(lineShift).IsGreaterThan(0);
        await Assert.That(second).IsEqualTo(PatchStatus.Applied);
        await Assert.That(afterSecond).DoesNotContain("old");
        var (a, b) = Segments(afterSecond);
        await Assert.That(a).Contains("line1");
        await Assert.That(b).Contains("newB");
    }

    // Re-applying a patch whose site is already done, while an identical literal exists
    // elsewhere, must not patch the other site
    [Test]
    public async Task ReapplyingWithIdenticalLiteralsIsAlreadyApplied()
    {
        var source = TwoCallSites("\"new\"", "\"old\"");

        var status = InlinePatcher.TryApply(source, 4, "\"gone\"", "new", out _, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.AlreadyApplied);
    }

    [Test]
    public async Task ExpressionGoneAndLiteralMatchesIsAlreadyApplied()
    {
        // The other TFM already applied: the old expression is gone,
        // and the current argument renders to the new content.
        var source = Method("        await VerifyInline(value, \"new\");");
        var status = InlinePatcher.TryApply(source, 5, "\"old-gone\"", "new", out _, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.AlreadyApplied);
    }

    [Test]
    public async Task ExpressionGoneAndLiteralDiffersIsNotFound()
    {
        var source = Method("        await VerifyInline(value, \"different\");");
        var status = InlinePatcher.TryApply(source, 5, "\"old-gone\"", "new", out _, out var reason);
        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("Re-run the test");
    }

    [Test]
    public async Task InsertIntoSingleArgumentCall()
    {
        var source = Method("        await VerifyInline(value);");
        var status = InlinePatcher.TryApply(source, 5, null, "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("await VerifyInline(value, \"\"\"\n            new\n            \"\"\");");
    }

    [Test]
    public async Task InsertWithComplexTargetExpression()
    {
        var source = Method("        await VerifyInline(new { a = 1, b = Call(\"x, y\") });");
        var status = InlinePatcher.TryApply(source, 5, null, "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("Call(\"x, y\") }, \"\"\"");
    }

    [Test]
    public async Task InsertReplacesNullArgument()
    {
        var source = Method("        await VerifyInline(value, null, settings);");
        var status = InlinePatcher.TryApply(source, 5, null, "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("await VerifyInline(value, \"\"\"\n            new\n            \"\"\", settings);");
    }

    [Test]
    public async Task InsertBeforeNamedSettingsArgument()
    {
        var source = Method("        await VerifyInline(value, settings: mySettings);");
        var status = InlinePatcher.TryApply(source, 5, null, "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("await VerifyInline(value, expected: \"\"\"\n            new\n            \"\"\", settings: mySettings);");
    }

    [Test]
    public async Task InsertLeavesFluentContinuationIntact()
    {
        var source = Method("        await VerifyInline(value)\n            .UseDirectory(\"snapshots\");");
        var status = InlinePatcher.TryApply(source, 5, null, "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains(".UseDirectory(\"snapshots\");");
        await Assert.That(newSource).Contains("VerifyInline(value, \"\"\"");
    }

    [Test]
    public async Task NullOriginalWithDifferingLiteralIsNotFound()
    {
        var source = Method("        await VerifyInline(value, \"different\");");
        var status = InlinePatcher.TryApply(source, 5, null, "new", out _, out var reason);
        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("different expected argument");
    }

    [Test]
    public async Task NullOriginalWithEqualLiteralIsAlreadyApplied()
    {
        var source = Method("        await VerifyInline(value, \"new\");");
        var status = InlinePatcher.TryApply(source, 5, null, "new", out _, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.AlreadyApplied);
    }

    [Test]
    public async Task NoCallFound()
    {
        var source = Method("        await Verify(value);");
        var status = InlinePatcher.TryApply(source, 5, null, "new", out _, out var reason);
        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("Could not find a VerifyInline call");
    }

    [Test]
    public async Task PartialTokenIsNotMatched()
    {
        var source = Method("        await MyVerifyInlineHelper(value);");
        var status = InlinePatcher.TryApply(source, 5, null, "new", out _, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
    }

    [Test]
    public async Task TabIndentedFileUsesTabUnit()
    {
        var source = "class Tests\n{\n\tasync Task Test()\n\t{\n\t\tawait VerifyInline(value);\n\t}\n}";
        var status = InlinePatcher.TryApply(source, 5, null, "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("VerifyInline(value, \"\"\"\n\t\t\tnew\n\t\t\t\"\"\");");
    }

    [Test]
    public async Task HintBeyondEndOfFile()
    {
        var source = "await VerifyInline(value);";
        var status = InlinePatcher.TryApply(source, 500, null, "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("VerifyInline(value, \"\"\"");
    }

    [Test]
    public async Task LfExpressionFoundInCrlfFile()
    {
        var source = Method($"        await VerifyInline(value, {rawOld.Replace("\n", "\n        ")});").Replace("\n", "\r\n");
        var expression = rawOld.Replace("\n", "\n        ");
        var status = InlinePatcher.TryApply(source, 5, expression, "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).DoesNotContain("old");
    }

    [Test]
    public async Task OutsideSpanIsCharacterIdentical()
    {
        var body = "        await VerifyInline(value, \"old\");";
        var source = Method(body);
        InlinePatcher.TryApply(source, 5, "\"old\"", "new", out var newSource, out _);
        var prefix = source.Substring(0, source.IndexOf("\"old\"", StringComparison.Ordinal));
        var suffix = source.Substring(source.IndexOf("\"old\"", StringComparison.Ordinal) + 5);
        await Assert.That(newSource.StartsWith(prefix)).IsTrue();
        await Assert.That(newSource.EndsWith(suffix)).IsTrue();
    }

    const string crlf = "\r\n";
    const string lf = "\n";

    static string BuildMultiLineSource(string eol) =>
        string.Join(
            eol,
            "class Tests",
            "{",
            "    async Task Test()",
            "    {",
            "        await VerifyInline(value, \"\"\"",
            "            old1",
            "            old2",
            "            \"\"\");",
            "    }",
            "}");

    static string BuildExpression(string eol) =>
        string.Join(
            eol,
            "\"\"\"",
            "            old1",
            "            old2",
            "            \"\"\"");

    // Every line ending in the result must match the file's, with no strays
    static async Task AssertEolConsistent(string text, string eol)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (current == '\r')
            {
                await Assert.That(eol).IsEqualTo(crlf);
                await Assert.That(index + 1 < text.Length && text[index + 1] == '\n').IsTrue();
                continue;
            }

            if (current == '\n' &&
                eol == crlf)
            {
                await Assert.That(index > 0 && text[index - 1] == '\r').IsTrue();
            }
        }
    }

    [Test]
    [Arguments(crlf, crlf, crlf)]
    [Arguments(crlf, crlf, lf)]
    [Arguments(crlf, lf, crlf)]
    [Arguments(crlf, lf, lf)]
    [Arguments(lf, crlf, crlf)]
    [Arguments(lf, crlf, lf)]
    [Arguments(lf, lf, crlf)]
    [Arguments(lf, lf, lf)]
    public async Task EolCombinations(string fileEol, string expressionEol, string contentEol)
    {
        var source = BuildMultiLineSource(fileEol);
        var expression = BuildExpression(expressionEol);
        var content = "new1" + contentEol + "new2";

        var status = InlinePatcher.TryApply(source, 5, expression, content, out var newSource, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(reason).IsEmpty();
        await Assert.That(newSource).Contains("new1");
        await Assert.That(newSource).Contains("new2");
        await Assert.That(newSource).DoesNotContain("old1");
        await Assert.That(newSource).DoesNotContain("old2");
        await AssertEolConsistent(newSource, fileEol);
    }

    [Test]
    [Arguments(crlf, crlf)]
    [Arguments(crlf, lf)]
    [Arguments(lf, crlf)]
    [Arguments(lf, lf)]
    public async Task EolCombinationsForInsert(string fileEol, string contentEol)
    {
        var source = string.Join(
            fileEol,
            "class Tests",
            "{",
            "    async Task Test()",
            "    {",
            "        await VerifyInline(value);",
            "    }",
            "}");
        var content = "new1" + contentEol + "new2";

        var status = InlinePatcher.TryApply(source, 5, null, content, out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("new1");
        await Assert.That(newSource).Contains("new2");
        await AssertEolConsistent(newSource, fileEol);
    }

    [Test]
    public async Task LoneCarriageReturnInContentIsNormalized()
    {
        var source = BuildMultiLineSource(lf);
        var expression = BuildExpression(lf);

        var status = InlinePatcher.TryApply(source, 5, expression, "new1\rnew2", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).DoesNotContain("\r");
        await Assert.That(newSource).Contains("new1");
        await Assert.That(newSource).Contains("new2");
    }

    [Test]
    public async Task MixedEolFileLeavesUntouchedRegionsAlone()
    {
        // The prefix and tail use LF, the body uses CRLF, so CRLF is dominant
        var prefix = "// leading comment\n// another\n";
        var body = string.Join(
            crlf,
            "class Tests",
            "{",
            "    async Task Test()",
            "    {",
            "        await VerifyInline(value, \"old\");",
            "    }",
            "}");
        var suffix = "\r\n// trailing\n// mixed tail\n";
        var source = prefix + body + suffix;

        var status = InlinePatcher.TryApply(source, 7, "\"old\"", "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        // Untouched regions keep their original endings byte for byte
        await Assert.That(newSource.StartsWith(prefix, StringComparison.Ordinal)).IsTrue();
        await Assert.That(newSource.EndsWith(suffix, StringComparison.Ordinal)).IsTrue();
        // The spliced literal uses the file's dominant ending
        await Assert.That(newSource).Contains("\"\"\"\r\n            new\r\n            \"\"\"");
    }

    [Test]
    public async Task SingleLineFileWithNoNewlines()
    {
        var status = InlinePatcher.TryApply("await VerifyInline(value, \"old\");", 1, "\"old\"", "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("new");
        await Assert.That(newSource).DoesNotContain("\"old\"");
    }
}
