public class InlinePatcherTests
{
    const string rawOld = "\"\"\"\n        old\n        \"\"\"";

    static string Method(string body) =>
        $"class Tests\n{{\n    async Task Test()\n    {{\n{body}\n    }}\n}}";

    [Test]
    public async Task ReplaceRawLiteral()
    {
        var source = Method($"        await Snapshot({rawOld.Replace("\n", "\n        ")});");
        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, rawOld.Replace("\n", "\n        "), "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("new");
        await Assert.That(newSource).DoesNotContain("old");
        // Everything outside the span is untouched
        await Assert.That(newSource).Contains("class Tests");
        await Assert.That(newSource).Contains("await Snapshot(");
        await Assert.That(newSource.EndsWith(");\n    }\n}")).IsTrue();
    }

    [Test]
    public async Task ReplaceRegularLiteral()
    {
        var source = Method("        await Snapshot(\"old\");");
        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, "\"old\"", "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains(
            "        await Snapshot(\n" +
            "            \"\"\"\n" +
            "            new\n" +
            "            \"\"\");");
    }

    [Test]
    public async Task ReplacementUsesFileEol()
    {
        var source = Method("        await Snapshot(\"old\");").Replace("\n", "\r\n");
        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, "\"old\"", "a\nb", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).DoesNotContain("a\nb");
        await Assert.That(newSource).Contains("a\r\n            b");
    }

    [Test]
    public async Task AlreadyAppliedWhenLiteralMatches()
    {
        var source = Method("        await Snapshot(\"same\");");
        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, "\"same\"", "same", out _, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.AlreadyApplied);
    }

    [Test]
    public async Task ShiftedLinesStillFound()
    {
        var padding = string.Concat(Enumerable.Repeat("        // padding\n", 30));
        var source = Method(padding + "        await Snapshot(\"old\");");
        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, "\"old\"", "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).DoesNotContain("\"old\"");
    }

    [Test]
    public async Task DuplicateLiteralsPicksNearestToHint()
    {
        var source =
            "await A().Snapshot(\"dup\");\n" +
            string.Concat(Enumerable.Repeat("// filler\n", 10)) +
            "await B().Snapshot(\"dup\");\n";
        var status = InlinePatcher.TryApply(source, 12, InlinePatchMode.Set, "\"dup\"", "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        // First occurrence untouched, second replaced
        await Assert.That(newSource).Contains("A().Snapshot(\"dup\")");
        await Assert.That(newSource).DoesNotContain("B().Snapshot(\"dup\")");
    }

    // Two call sites, A on line 4 and B on line 7
    static string TwoCallSites(string literalA, string literalB) =>
        string.Join(
            "\n",
            "class Tests",
            "{",
            "    Task A() =>",
            $"        Verify(a).Snapshot({literalA});",
            "",
            "    Task B() =>",
            $"        Verify(b).Snapshot({literalB});",
            "}");

    static (string a, string b) Segments(string text)
    {
        var indexA = text.IndexOf("Verify(a)", StringComparison.Ordinal);
        var indexB = text.IndexOf("Verify(b)", StringComparison.Ordinal);
        return (text.Substring(indexA, indexB - indexA), text.Substring(indexB));
    }

    [Test]
    public async Task DuplicateLiteralsPicksNearestToHintFirst()
    {
        var source = TwoCallSites("\"dup\"", "\"dup\"");

        var status = InlinePatcher.TryApply(source, 4, InlinePatchMode.Set, "\"dup\"", "new", out var newSource, out _);

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
            "    Task A() =>",
            "        Verify(a).Snapshot(\"dup\");",
            "    Task B() =>",
            "        Verify(b).Snapshot(\"dup\");",
            "}");

        // Line 5 is equidistant from the sites on lines 4 and 6
        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, "\"dup\"", "new", out var newSource, out _);

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

        var first = InlinePatcher.TryApply(source, 4, InlinePatchMode.Set, "\"old\"", "new", out var afterFirst, out _);
        var second = InlinePatcher.TryApply(afterFirst, 7, InlinePatchMode.Set, "\"old\"", "new", out var afterSecond, out var reason);

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

        var first = InlinePatcher.TryApply(source, 4, InlinePatchMode.Set, "\"old\"", "newA", out var afterFirst, out _);
        var second = InlinePatcher.TryApply(afterFirst, 7, InlinePatchMode.Set, "\"old\"", "newB", out var afterSecond, out _);

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

        InlinePatcher.TryApply(source, 4, InlinePatchMode.Set, "\"old\"", "line1\nline2\nline3", out var afterFirst, out _);
        var lineShift = afterFirst.Split('\n').Length - source.Split('\n').Length;
        var second = InlinePatcher.TryApply(afterFirst, 7, InlinePatchMode.Set, "\"old\"", "newB", out var afterSecond, out _);

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

        var status = InlinePatcher.TryApply(source, 4, InlinePatchMode.Set, "\"gone\"", "new", out _, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.AlreadyApplied);
    }

    [Test]
    public async Task ExpressionGoneAndLiteralMatchesIsAlreadyApplied()
    {
        // The other TFM already applied: the old expression is gone,
        // and the current argument renders to the new content.
        var source = Method("        await Snapshot(\"new\");");
        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, "\"old-gone\"", "new", out _, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.AlreadyApplied);
    }

    [Test]
    public async Task ExpressionGoneAndLiteralDiffersIsNotFound()
    {
        var source = Method("        await Snapshot(\"different\");");
        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, "\"old-gone\"", "new", out _, out var reason);
        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("Re-run the test");
    }

    [Test]
    public async Task InsertIntoEmptyArgumentList()
    {
        var source = Method("        await Snapshot();");
        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, null, "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("await Snapshot(\n            \"\"\"\n            new\n            \"\"\");");
    }

    [Test]
    public async Task InsertReplacesNullArgument()
    {
        var source = Method("        await Snapshot(null, file, line);");
        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, null, "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("await Snapshot(\n            \"\"\"\n            new\n            \"\"\", file, line);");
    }

    [Test]
    public async Task InsertBeforeAnotherNamedArgument()
    {
        var source = Method("        await Snapshot(file: myFile);");
        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, null, "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("await Snapshot(expected: \"\"\"\n            new\n            \"\"\", file: myFile);");
    }

    [Test]
    public async Task NullOriginalWithDifferingLiteralIsNotFound()
    {
        var source = Method("        await Snapshot(\"different\");");
        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, null, "new", out _, out var reason);
        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("different expected argument");
    }

    [Test]
    public async Task NullOriginalWithEqualLiteralIsAlreadyApplied()
    {
        var source = Method("        await Snapshot(\"new\");");
        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, null, "new", out _, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.AlreadyApplied);
    }

    [Test]
    public async Task NoCallFound()
    {
        var source = Method("        await Verify(value);");
        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, null, "new", out _, out var reason);
        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("Could not find a Snapshot call");
    }

    [Test]
    public async Task PartialTokenIsNotMatched()
    {
        var source = Method("        await MySnapshotHelper(value);");
        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, null, "new", out _, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
    }

    [Test]
    public async Task AppendToABareVerify()
    {
        var source = Method("        await Verify(value);");

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Append, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains(
            "        await Verify(value)\n" +
            "            .Snapshot(\n" +
            "                \"\"\"\n" +
            "                new\n" +
            "                \"\"\");");
    }

    // Snapshot terminates the chain, so it has to land after everything already chained on
    [Test]
    public async Task AppendGoesAfterAnExistingChain()
    {
        var source = Method(
            "        await Verify(value)\n" +
            "            .UseDirectory(\"snapshots\")\n" +
            "            .ScrubLinesContaining(\"x\");");

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Append, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains(
            "            .ScrubLinesContaining(\"x\")\n" +
            "            .Snapshot(\n" +
            "                \"\"\"\n" +
            "                new\n" +
            "                \"\"\");");
    }

    [Test]
    public async Task AppendToAnEntryPointOverload()
    {
        var source = Method("        await VerifyXml(value);");

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Append, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("await VerifyXml(value)\n            .Snapshot(\n                \"\"\"");
    }

    // The verify call spans lines, so the closing paren is nowhere near the hint
    [Test]
    public async Task AppendToAMultiLineVerifyCall()
    {
        var source = Method(
            "        await Verify(\n" +
            "            new\n" +
            "            {\n" +
            "                value\n" +
            "            });");

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Append, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("            })\n            .Snapshot(\n                \"\"\"");
    }

    [Test]
    public async Task AppendUsesTheFileEol()
    {
        var source = Method("        await Verify(value);").Replace("\n", "\r\n");

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Append, null, "a\nb", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await AssertEolConsistent(newSource, crlf);
    }

    [Test]
    public async Task AppendIsRefusedWhenOneIsAlreadyChained()
    {
        var source = Method("        await Verify(value)\n            .Snapshot(\"already\");");

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Append, null, "new", out _, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("already has a Snapshot call");
    }

    [Test]
    public async Task AppendWithNoVerifyCall()
    {
        var source = Method("        await Something(value);");

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Append, null, "new", out _, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("Could not find a Verify call");
    }

    // A project helper named Verify is not the entry point, and a stale hint must not drift onto one
    [Test]
    public async Task AppendSkipsAVerifyOnAnotherReceiver()
    {
        var source = Method("        Assert.Empty(ContentValidation.Verify(value));");

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Append, null, "new", out _, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("Could not find a Verify call");
    }

    [Test]
    public async Task AppendSkipsAVerifyOnAnInstance()
    {
        var source = Method("        mock.VerifyAll();");

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Append, null, "new", out _, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("Could not find a Verify call");
    }

    // The entry point wrapping a helper of the same name: the outer call is the one to append to
    [Test]
    public async Task AppendPrefersTheEntryPointOverANestedHelper()
    {
        var source = Method("        await Verify(ContentValidation.Verify(value));");

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Append, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains(
            "        await Verify(ContentValidation.Verify(value))\n" +
            "            .Snapshot(\n" +
            "                \"\"\"");
    }

    [Test]
    public async Task AppendToAVerifierQualifiedCall()
    {
        var source = Method("        await Verifier.Verify(value);");

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Append, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("await Verifier.Verify(value)\n            .Snapshot(\n                \"\"\"");
    }

    [Test]
    public async Task AppendToAThisQualifiedCall()
    {
        var source = Method("        await this.Verify(value);");

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Append, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("await this.Verify(value)\n            .Snapshot(\n                \"\"\"");
    }

    [Test]
    public async Task RemoveTakesTheWholeLine()
    {
        var source = Method(
            "        await Verify(value)\n" +
            "            .Snapshot(\"\"\"\n" +
            "                old\n" +
            "                \"\"\");");

        var status = InlinePatcher.TryApply(source, 6, InlinePatchMode.Remove, null, "", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).IsEqualTo(Method("        await Verify(value);"));
    }

    [Test]
    public async Task RemoveLeavesTheRestOfTheChain()
    {
        var source = Method(
            "        await Verify(value)\n" +
            "            .UseDirectory(\"snapshots\")\n" +
            "            .Snapshot(\"old\");");

        var status = InlinePatcher.TryApply(source, 7, InlinePatchMode.Remove, null, "", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).IsEqualTo(
            Method(
                "        await Verify(value)\n" +
                "            .UseDirectory(\"snapshots\");"));
    }

    [Test]
    public async Task RemoveFromASingleLineChain()
    {
        var source = Method("        await Verify(value).Snapshot(\"old\");");

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Remove, null, "", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).IsEqualTo(Method("        await Verify(value);"));
    }

    [Test]
    public async Task RemoveWithCrlf()
    {
        var source = Method("        await Verify(value)\n            .Snapshot(\"old\");").Replace("\n", "\r\n");

        var status = InlinePatcher.TryApply(source, 6, InlinePatchMode.Remove, null, "", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).IsEqualTo(Method("        await Verify(value);").Replace("\n", "\r\n"));
    }

    [Test]
    public async Task RemovePicksTheSiteNearestTheHint()
    {
        var source = TwoCallSites("\"a\"", "\"b\"");

        var status = InlinePatcher.TryApply(source, 7, InlinePatchMode.Remove, null, "", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        var (a, b) = Segments(newSource);
        await Assert.That(a).Contains(".Snapshot(\"a\")");
        await Assert.That(b).DoesNotContain("Snapshot");
    }

    [Test]
    public async Task RemoveWhenTheCallIsNotChained()
    {
        var source = Method("        await Snapshot(\"old\");");

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Remove, null, "", out _, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("not a chained call");
    }

    [Test]
    public async Task RemoveWithNoSnapshotCall()
    {
        var source = Method("        await Verify(value);");

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Remove, null, "", out _, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("Could not find a Snapshot call");
    }

    [Test]
    public async Task TabIndentedFileUsesTabUnit()
    {
        var source = "class Tests\n{\n\tasync Task Test()\n\t{\n\t\tawait Snapshot();\n\t}\n}";
        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, null, "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("Snapshot(\n\t\t\t\"\"\"\n\t\t\tnew\n\t\t\t\"\"\");");
    }

    [Test]
    public async Task HintBeyondEndOfFile()
    {
        var source = "await Snapshot();";
        var status = InlinePatcher.TryApply(source, 500, InlinePatchMode.Set, null, "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        // No newline in the file, so the splice falls back to the environment's
        await Assert.That(newSource).Contains($"Snapshot({Environment.NewLine}    \"\"\"");
    }

    [Test]
    public async Task LfExpressionFoundInCrlfFile()
    {
        var source = Method($"        await Snapshot({rawOld.Replace("\n", "\n        ")});").Replace("\n", "\r\n");
        var expression = rawOld.Replace("\n", "\n        ");
        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, expression, "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).DoesNotContain("old");
    }

    [Test]
    public async Task OutsideSpanIsCharacterIdentical()
    {
        var body = "        await Snapshot(\"old\");";
        var source = Method(body);
        InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, "\"old\"", "new", out var newSource, out _);
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
            "        await Snapshot(\"\"\"",
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

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, expression, content, out var newSource, out var reason);

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
            "        await Snapshot();",
            "    }",
            "}");
        var content = "new1" + contentEol + "new2";

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, null, content, out var newSource, out _);

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

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, expression, "new1\rnew2", out var newSource, out _);

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
            "        await Snapshot(\"old\");",
            "    }",
            "}");
        var suffix = "\r\n// trailing\n// mixed tail\n";
        var source = prefix + body + suffix;

        var status = InlinePatcher.TryApply(source, 7, InlinePatchMode.Set, "\"old\"", "new", out var newSource, out _);

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
        var status = InlinePatcher.TryApply("await Snapshot(\"old\");", 1, InlinePatchMode.Set, "\"old\"", "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("new");
        await Assert.That(newSource).DoesNotContain("\"old\"");
    }

    // The verify argument is the same text as the snapshot, and comes first on the line
    [Test]
    public async Task SameLiteralInTheVerifyArgumentIsNotPatched()
    {
        var source = Method("        await Verify(\"same\").Snapshot(\"same\");");

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, "\"same\"", "changed", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("await Verify(\"same\").Snapshot(\n            \"\"\"");
        await Assert.That(newSource).Contains("changed");
    }

    // The expression search must match a whole argument, not the quoted part of a longer literal
    [Test]
    public async Task PrefixedLiteralIsNotPatchedThroughItsQuote()
    {
        var source = Method("        await Verify(x).Snapshot(@\"old\");");

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, "\"old\"", "new", out _, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("Re-run the test");
    }

    [Test]
    public async Task SuffixedLiteralIsNotPatchedThroughItsQuote()
    {
        var source = Method("        await Verify(x).Snapshot(\"old\"u8);");

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, "\"old\"", "new", out _, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("not a string literal");
    }

    [Test]
    public async Task CommentedOutCallIsSkipped()
    {
        var source = string.Join(
            "\n",
            "class Tests",
            "{",
            "    // await Verify(x).Snapshot(\"doc example\");",
            "    async Task Test() =>",
            "        await Verify(x).Snapshot();",
            "}");

        var status = InlinePatcher.TryApply(source, 3, InlinePatchMode.Set, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("    // await Verify(x).Snapshot(\"doc example\");\n");
        await Assert.That(newSource).Contains("        await Verify(x).Snapshot(\n            \"\"\"");
    }

    [Test]
    public async Task CallInsideAStringIsSkipped()
    {
        var source = Method(
            "        var text = \"await Snapshot(\\\"x\\\")\";\n" +
            "        await Verify(x).Snapshot();");

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("var text = \"await Snapshot(\\\"x\\\")\";\n");
        await Assert.That(newSource).Contains("await Verify(x).Snapshot(\n            \"\"\"");
    }

    // A declaration is a name followed by parens too, so it has to be told apart by what precedes it
    [Test]
    public async Task SnapshotDeclarationIsNotMistakenForACall()
    {
        var source = string.Join(
            "\n",
            "static class Extensions",
            "{",
            "    public static Task Snapshot(this Task task, string? expected = null) =>",
            "        task;",
            "}");

        var status = InlinePatcher.TryApply(source, 3, InlinePatchMode.Set, null, "new", out _, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("Could not find a Snapshot call");
    }

    [Test]
    public async Task AppendSkipsAVerifyPrefixedDeclaration()
    {
        var source = string.Join(
            "\n",
            "class Tests",
            "{",
            "    Task VerifyThing(string value) => Verify(value);",
            "}");

        var status = InlinePatcher.TryApply(source, 3, InlinePatchMode.Append, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains(
            "    Task VerifyThing(string value) => Verify(value)\n" +
            "        .Snapshot(\n" +
            "            \"\"\"");
    }

    // Snapshot terminates the chain, so a comment in the middle of one must not end the walk
    [Test]
    public async Task AppendGoesAfterACommentInTheChain()
    {
        var source = Method(
            "        await Verify(value) // note\n" +
            "            .UseDirectory(\"snapshots\");");

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Append, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains(
            "            .UseDirectory(\"snapshots\")\n" +
            "            .Snapshot(\n" +
            "                \"\"\"");
    }

    [Test]
    public async Task LiteralInACommentIsNotPatched()
    {
        var source = Method(
            "        // was \"old\"\n" +
            "        await Verify(x).Snapshot(\"old\");");

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, "\"old\"", "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("// was \"old\"\n");
        await Assert.That(newSource).Contains("Snapshot(\n            \"\"\"");
    }

    [Test]
    public async Task LiteralInAnotherMethodIsNotPatched()
    {
        var source = string.Join(
            "\n",
            "class Tests",
            "{",
            "    void Helper() => Log(\"old\");",
            "",
            "    async Task Test() =>",
            "        await Verify(x).Snapshot(\"old\");",
            "}");

        var status = InlinePatcher.TryApply(source, 3, InlinePatchMode.Set, "\"old\"", "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("void Helper() => Log(\"old\");");
        await Assert.That(newSource).Contains("Snapshot(\n            \"\"\"");
    }

    // The empty literal is two characters, and the opening of every raw literal holds a pair
    [Test]
    public async Task EmptyOriginalIsNotMatchedInsideARawDelimiter()
    {
        var source = string.Join(
            "\n",
            "class Tests",
            "{",
            "    Task A() =>",
            "        Verify(a).Snapshot(\"\"\"",
            "            content",
            "            \"\"\");",
            "",
            "    Task B() =>",
            "        Verify(b).Snapshot(\"\");",
            "}");

        var status = InlinePatcher.TryApply(source, 7, InlinePatchMode.Set, "\"\"", "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("            content\n            \"\"\");");
        await Assert.That(newSource).Contains("Verify(b).Snapshot(\n            \"\"\"\n            new\n            \"\"\");");
    }

    [Test]
    public async Task GenericSnapshotCall()
    {
        var source = Method("        await Verify(x).Snapshot<Thing>();");

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains(".Snapshot<Thing>(\n            \"\"\"");
    }

    [Test]
    public async Task AppendToAGenericVerify()
    {
        var source = Method("        await Verify<Thing>(value);");

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Append, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("await Verify<Thing>(value)\n            .Snapshot(\n                \"\"\"");
    }

    [Test]
    public async Task CommentInTheArgumentListIsNotTheArgument()
    {
        var source = Method("        await Verify(x).Snapshot(/* keep */);");

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("Snapshot(/* keep */\n            \"\"\"");
    }

    [Test]
    public async Task CommentAfterTheArgumentIsKept()
    {
        var source = Method("        await Verify(x).Snapshot(\"old\" /* why */);");

        var status = InlinePatcher.TryApply(source, 5, InlinePatchMode.Set, "\"old\"", "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("\"\"\" /* why */);");
    }

    // Pulling the call up onto the line above would take the semicolon into the comment
    [Test]
    public async Task RemoveLeavesALineCommentAboveIntact()
    {
        var source = Method(
            "        await Verify(value)\n" +
            "            // note\n" +
            "            .Snapshot(\"old\");");

        var status = InlinePatcher.TryApply(source, 7, InlinePatchMode.Remove, null, "", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).IsEqualTo(
            Method(
                "        await Verify(value)\n" +
                "            // note\n" +
                "            ;"));
    }
}
