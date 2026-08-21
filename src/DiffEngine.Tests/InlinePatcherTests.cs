public class InlinePatcherTests
{
    static PatchStatus TryApply(
        string source,
        int lineHint,
        InlinePatchMode mode,
        string? originalExpression,
        string newContent,
        out string newSource,
        out string failReason,
        string? originalValue = null,
        string? memberName = null) =>
        InlinePatcher.TryApply(SourceLanguage.CSharp, source, lineHint, mode, originalExpression, originalValue, memberName, newContent, out newSource, out failReason);

    const string rawOld = "\"\"\"\n        old\n        \"\"\"";

    static string Method(string body) =>
        $"class Tests\n{{\n    async Task Test()\n    {{\n{body}\n    }}\n}}";

    [Test]
    public async Task ReplaceRawLiteral()
    {
        var source = Method($"        await Snapshot({rawOld.Replace("\n", "\n        ")});");
        var status = TryApply(source, 5, InlinePatchMode.Set, rawOld.Replace("\n", "\n        "), "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("new");
        await Assert.That(newSource).DoesNotContain("old");
        // Everything outside the span is untouched
        await Assert.That(newSource).Contains("class Tests");
        await Assert.That(newSource).Contains("await Snapshot(");
        await Assert.That(newSource.EndsWith(");\n    }\n}")).IsTrue();
    }

    /// <summary>
    /// An interpolated raw string whose hole holds a literal of its own. Skipping holes as content
    /// ended the outer string at the inner delimiter, after which the rest of the line lexed as
    /// code and a stray delimiter opened a string that ran on and swallowed the real call.
    /// </summary>
    [Test]
    public async Task ARawInterpolatedStringWithALiteralInItsHoleIsSteppedOverWhole()
    {
        var source = Method($"{rawInterpolated}\n        await Snapshot(\"old\");");
        var status = TryApply(source, 6, InlinePatchMode.Set, "\"old\"", "new", out var newSource, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(reason).IsEmpty();
        await Assert.That(newSource).Contains("Snapshot(\"new\")");
        // And the literal it stepped over is untouched
        await Assert.That(newSource).Contains(rawInterpolated);
    }

    /// <summary>
    /// The same shape with the call before it, so the scan has to get past the literal rather than
    /// stop short of it.
    /// </summary>
    [Test]
    public async Task ASnapshotBeforeARawInterpolatedStringIsStillFound()
    {
        var source = Method($"        await Snapshot(\"old\");\n{rawInterpolated}");
        var status = TryApply(source, 5, InlinePatchMode.Set, "\"old\"", "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("Snapshot(\"new\")");
    }

    // Written by concatenation because it holds runs of three and four quotes, which no raw string
    // in this file can carry without a wider delimiter than the thing being described.
    //
    // The literal in the hole is four-quoted and holds a run of three, so the runs no longer pair
    // up evenly. That matters: with a hole skipped as content, the blind scan ends the outer
    // string at the first run of three or more and then re-pairs the rest, which for evenly
    // matched runs lands back on its feet and hides the bug. Here the last run opens a string that
    // runs to the end of the file, taking the Snapshot call with it
    const string rawInterpolated =
        "        var text = $" + q3 + "{Render(" + q4 + "has " + q3 + " inside" + q4 + ")}" + q3 + ";";

    /// <summary>
    /// A verbatim string that opens on an escaped quote. Measuring the quote run before asking
    /// whether the literal is verbatim lexed this as a 3-quote raw string, which then ran to the
    /// end of the file and hid every call after it. There is no verbatim raw form, so a run of
    /// quotes after @" is content, not a delimiter.
    /// </summary>
    [Test]
    public async Task AVerbatimStringOpeningOnAnEscapedQuoteIsSteppedOverWhole()
    {
        var source = Method($"{verbatimEscapedQuote}\n        await Snapshot(\"old\");");
        var status = TryApply(source, 6, InlinePatchMode.Set, "\"old\"", "new", out var newSource, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(reason).IsEmpty();
        await Assert.That(newSource).Contains("Snapshot(\"new\")");
        // And the literal it stepped over is untouched
        await Assert.That(newSource).Contains(verbatimEscapedQuote);
    }

    /// <summary>
    /// The same shape with the call before it, so the scan has to get past the literal rather
    /// than stop short of it.
    /// </summary>
    [Test]
    public async Task ASnapshotBeforeAVerbatimStringOpeningOnAnEscapedQuoteIsStillFound()
    {
        var source = Method($"        await Snapshot(\"old\");\n{verbatimEscapedQuote}");
        var status = TryApply(source, 5, InlinePatchMode.Set, "\"old\"", "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("Snapshot(\"new\")");
        await Assert.That(newSource).Contains(verbatimEscapedQuote);
    }

    // A verbatim string whose first content character is an escaped quote:
    //
    //     var path = @"""C:\tools\run.exe"" --flag";
    //
    // By concatenation for the same reason rawInterpolated is: the quote runs cannot be written
    // in a literal here without a delimiter wider than the thing being described.
    const string verbatimEscapedQuote =
        "        var path = @" + q3 + "C:" + slash + "tools" + slash + "run.exe" + q2 + " --flag" + q1 + ";";

    const string q1 = "\"";
    const string q2 = "\"\"";
    const string slash = "\\";
    const string q3 = "\"\"\"";
    const string q4 = "\"\"\"\"";

    /// <summary>
    /// A retire is anchored the same way a set is. It used to delete whichever call sat nearest
    /// the recorded line, and that line stops being true the moment anything above it is edited -
    /// so a stale hint retired the snapshot in the test next door and reported Applied.
    /// </summary>
    [Test]
    public async Task RemoveTakesTheCallTheAnchorNamesRatherThanTheNearest()
    {
        var source = Method(
            "        await A().Snapshot(\"one\");\n" +
            "        await B().Snapshot(\"two\");");

        // Hint on the second call, anchor on the first
        var status = TryApply(source, 6, InlinePatchMode.Remove, "\"one\"", "", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).DoesNotContain("\"one\"");
        // The other test's snapshot is left alone
        await Assert.That(newSource).Contains("Snapshot(\"two\")");
    }

    /// <summary>
    /// And an anchor that matches nothing is reported rather than resolved to the nearest call.
    /// </summary>
    [Test]
    public async Task RemoveReportsWhenTheAnchorIsGone()
    {
        var source = Method("        await A().Snapshot(\"two\");");

        var status = TryApply(source, 5, InlinePatchMode.Remove, "\"one\"", "", out _, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("still the one the test run saw");
    }
    [Test]
    public async Task ReplaceRegularLiteral()
    {
        var source = Method("        await Snapshot(\"old\");");
        var status = TryApply(source, 5, InlinePatchMode.Set, "\"old\"", "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("        await Snapshot(\"new\");");
    }

    [Test]
    public async Task ReplacementUsesFileEol()
    {
        var source = Method("        await Snapshot(\"old\");").Replace("\n", "\r\n");
        var status = TryApply(source, 5, InlinePatchMode.Set, "\"old\"", "a\nb", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).DoesNotContain("a\nb");
        await Assert.That(newSource).Contains("a\r\n            b");
    }

    [Test]
    public async Task AlreadyAppliedWhenLiteralMatches()
    {
        var source = Method("        await Snapshot(\"same\");");
        var status = TryApply(source, 5, InlinePatchMode.Set, "\"same\"", "same", out _, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.AlreadyApplied);
    }

    [Test]
    public async Task ShiftedLinesStillFound()
    {
        var padding = string.Concat(Enumerable.Repeat("        // padding\n", 30));
        var source = Method(padding + "        await Snapshot(\"old\");");
        var status = TryApply(source, 5, InlinePatchMode.Set, "\"old\"", "new", out var newSource, out _);
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
        var status = TryApply(source, 12, InlinePatchMode.Set, "\"dup\"", "new", out var newSource, out _);
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

        var status = TryApply(source, 4, InlinePatchMode.Set, "\"dup\"", "new", out var newSource, out _);

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
        var status = TryApply(source, 5, InlinePatchMode.Set, "\"dup\"", "new", out var newSource, out _);

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

        var first = TryApply(source, 4, InlinePatchMode.Set, "\"old\"", "new", out var afterFirst, out _);
        var second = TryApply(afterFirst, 7, InlinePatchMode.Set, "\"old\"", "new", out var afterSecond, out var reason);

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

        var first = TryApply(source, 4, InlinePatchMode.Set, "\"old\"", "newA", out var afterFirst, out _);
        var second = TryApply(afterFirst, 7, InlinePatchMode.Set, "\"old\"", "newB", out var afterSecond, out _);

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

        TryApply(source, 4, InlinePatchMode.Set, "\"old\"", "line1\nline2\nline3", out var afterFirst, out _);
        var lineShift = afterFirst.Split('\n').Length - source.Split('\n').Length;
        var second = TryApply(afterFirst, 7, InlinePatchMode.Set, "\"old\"", "newB", out var afterSecond, out _);

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

        var status = TryApply(source, 4, InlinePatchMode.Set, "\"gone\"", "new", out _, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.AlreadyApplied);
    }

    [Test]
    public async Task ExpressionGoneAndLiteralMatchesIsAlreadyApplied()
    {
        // The other TFM already applied: the old expression is gone,
        // and the current argument renders to the new content.
        var source = Method("        await Snapshot(\"new\");");
        var status = TryApply(source, 5, InlinePatchMode.Set, "\"old-gone\"", "new", out _, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.AlreadyApplied);
    }

    [Test]
    public async Task ExpressionGoneAndLiteralDiffersIsNotFound()
    {
        var source = Method("        await Snapshot(\"different\");");
        var status = TryApply(source, 5, InlinePatchMode.Set, "\"old-gone\"", "new", out _, out var reason);
        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("Re-run the test");
    }

    [Test]
    public async Task InsertIntoEmptyArgumentList()
    {
        var source = Method("        await Snapshot();");
        var status = TryApply(source, 5, InlinePatchMode.Set, null, "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("await Snapshot(\"new\");");
    }

    [Test]
    public async Task InsertReplacesNullArgument()
    {
        var source = Method("        await Snapshot(null, file, line);");
        var status = TryApply(source, 5, InlinePatchMode.Set, null, "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("await Snapshot(\"new\", file, line);");
    }

    /// <summary>
    /// The other way of writing an absent snapshot. A producer sends no expression for it, for the
    /// same reason it sends none for null - a bare token is too common in a file to search for -
    /// so the insertion path is all there is, and it used to read `default` as an argument that
    /// was not a string literal and refuse the patch.
    /// </summary>
    [Test]
    public async Task InsertReplacesDefaultArgument()
    {
        var source = Method("        await Snapshot(default);");
        var status = TryApply(source, 5, InlinePatchMode.Set, null, "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("await Snapshot(\"new\");");
    }

    [Test]
    public async Task InsertBeforeAnotherNamedArgument()
    {
        var source = Method("        await Snapshot(file: myFile);");
        var status = TryApply(source, 5, InlinePatchMode.Set, null, "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("await Snapshot(expected: \"new\", file: myFile);");
    }

    [Test]
    public async Task NullOriginalWithDifferingLiteralIsNotFound()
    {
        var source = Method("        await Snapshot(\"different\");");
        var status = TryApply(source, 5, InlinePatchMode.Set, null, "new", out _, out var reason);
        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("different expected argument");
    }

    [Test]
    public async Task NullOriginalWithEqualLiteralIsAlreadyApplied()
    {
        var source = Method("        await Snapshot(\"new\");");
        var status = TryApply(source, 5, InlinePatchMode.Set, null, "new", out _, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.AlreadyApplied);
    }

    // The value anchor is not F# specific: a C# producer that sends one gets the same locating,
    // and the expression wins where both arrived, being what the source actually says
    [Test]
    public async Task ValueAnchorLocatesTheCall()
    {
        var source = Method("        await Snapshot(\"old\");");

        var status = TryApply(source, 5, InlinePatchMode.Set, null, "new", out var newSource, out _, originalValue: "old");

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("await Snapshot(\"new\");");
    }

    // Identical snapshots in two tests, and a hint that has drifted onto the wrong one. The
    // expression matches both, so the member is what says which test the patch came from
    [Test]
    public async Task MemberNameBeatsAStaleHint()
    {
        var source = TwoCallSites("\"dup\"", "\"dup\"");

        var status = TryApply(source, 4, InlinePatchMode.Set, "\"dup\"", "new", out var newSource, out _, memberName: "B");

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        var (a, b) = Segments(newSource);
        await Assert.That(a).Contains("\"dup\"");
        await Assert.That(b).Contains("new");
    }

    // The recorded line is tried before the member, so two snapshots in one method stay apart
    [Test]
    public async Task RecordedLineWinsOverTheMemberDeclaration()
    {
        var source = string.Join(
            "\n",
            "class Tests",
            "{",
            "    async Task Test()",
            "    {",
            "        await Verify(a).Snapshot(\"dup\");",
            "        await Verify(b).Snapshot(\"dup\");",
            "    }",
            "}");

        var status = TryApply(source, 6, InlinePatchMode.Set, "\"dup\"", "new", out var newSource, out _, memberName: "Test");

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("Verify(a).Snapshot(\"dup\")");
        await Assert.That(newSource).Contains("Verify(b).Snapshot(\"new\")");
    }

    // The test was renamed since the run, so there is no declaration to search from
    [Test]
    public async Task UnknownMemberNameFallsBackToTheHint()
    {
        var source = TwoCallSites("\"dup\"", "\"dup\"");

        var status = TryApply(source, 4, InlinePatchMode.Set, "\"dup\"", "new", out var newSource, out _, memberName: "GoneAway");

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        var (a, b) = Segments(newSource);
        await Assert.That(a).Contains("new");
        await Assert.That(b).Contains("\"dup\"");
    }

    // A helper that hides the Verify call without forwarding the caller-info attributes is not a
    // supported layout: the helper's one call is shared by every test that uses it, so an inline
    // snapshot spliced into it could only ever be right for one of them. The member floor makes
    // the layout fail explicitly rather than patch the helper. A helper that wants to take part
    // forwards the caller info and carries a Verify prefix, like any custom entry point
    [Test]
    public async Task AppendDoesNotReachAHelperDeclaredAboveTheMember()
    {
        var source = string.Join(
            "\n",
            "class Tests",
            "{",
            "    static Task Run(string value) =>",
            "        Verify(value);",
            "",
            "    async Task Test() =>",
            "        await Run(\"value\");",
            "}");

        var status = TryApply(source, 4, InlinePatchMode.Append, null, "new", out _, out var reason, memberName: "Test");

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("No Verify or Throws call");
    }

    // When the member's own body has a Verify call, the floor confines the search to it even
    // though the hint points above the declaration
    [Test]
    public async Task AppendPrefersACallInsideTheMemberOverAHelperAbove()
    {
        var source = string.Join(
            "\n",
            "class Tests",
            "{",
            "    static Task Run(string value) =>",
            "        Verify(value);",
            "",
            "    async Task Test()",
            "    {",
            "        await Verify(direct);",
            "        await Run(\"value\");",
            "    }",
            "}");

        var status = TryApply(source, 4, InlinePatchMode.Append, null, "new", out var newSource, out _, memberName: "Test");

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains(
            "        await Verify(direct)\n" +
            "            .Snapshot(\"new\");");
    }

    [Test]
    public async Task ExpressionWinsOverValue()
    {
        var source = TwoCallSites("\"a\"", "\"b\"");

        var status = TryApply(source, 4, InlinePatchMode.Set, "\"b\"", "new", out var newSource, out _, originalValue: "a");

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        var (a, b) = Segments(newSource);
        await Assert.That(a).Contains("\"a\"");
        await Assert.That(b).Contains("new");
    }

    [Test]
    public async Task NoCallFound()
    {
        var source = Method("        await Verify(value);");
        var status = TryApply(source, 5, InlinePatchMode.Set, null, "new", out _, out var reason);
        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("Could not find a Snapshot call");
    }

    [Test]
    public async Task PartialTokenIsNotMatched()
    {
        var source = Method("        await MySnapshotHelper(value);");
        var status = TryApply(source, 5, InlinePatchMode.Set, null, "new", out _, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
    }

    [Test]
    public async Task AppendToABareVerify()
    {
        var source = Method("        await Verify(value);");

        var status = TryApply(source, 5, InlinePatchMode.Append, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains(
            "        await Verify(value)\n" +
            "            .Snapshot(\"new\");");
    }

    // The raw form is the one that has to sit on its own line, indented under the call
    [Test]
    public async Task AppendMultiLineContent()
    {
        var source = Method("        await Verify(value);");

        var status = TryApply(source, 5, InlinePatchMode.Append, null, "a\nb", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains(
            "        await Verify(value)\n" +
            "            .Snapshot(\n" +
            "                \"\"\"\n" +
            "                a\n" +
            "                b\n" +
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

        var status = TryApply(source, 5, InlinePatchMode.Append, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains(
            "            .ScrubLinesContaining(\"x\")\n" +
            "            .Snapshot(\"new\");");
    }

    [Test]
    public async Task AppendToAnEntryPointOverload()
    {
        var source = Method("        await VerifyXml(value);");

        var status = TryApply(source, 5, InlinePatchMode.Append, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("await VerifyXml(value)\n            .Snapshot(\"new\");");
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

        var status = TryApply(source, 5, InlinePatchMode.Append, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("            })\n            .Snapshot(\"new\");");
    }

    [Test]
    public async Task AppendUsesTheFileEol()
    {
        var source = Method("        await Verify(value);").Replace("\n", "\r\n");

        var status = TryApply(source, 5, InlinePatchMode.Append, null, "a\nb", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await AssertEolConsistent(newSource, crlf);
    }

    [Test]
    public async Task AppendIsRefusedWhenOneIsAlreadyChained()
    {
        var source = Method("        await Verify(value)\n            .Snapshot(\"already\");");

        var status = TryApply(source, 5, InlinePatchMode.Append, null, "new", out _, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("already has a Snapshot call");
    }

    [Test]
    public async Task AppendWithNoVerifyCall()
    {
        var source = Method("        await Something(value);");

        var status = TryApply(source, 5, InlinePatchMode.Append, null, "new", out _, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("No Verify or Throws call at line");
    }

    // A project helper named Verify is not the entry point, and a stale hint must not drift onto one
    [Test]
    public async Task AppendSkipsAVerifyOnAnotherReceiver()
    {
        var source = Method("        Assert.Empty(ContentValidation.Verify(value));");

        var status = TryApply(source, 5, InlinePatchMode.Append, null, "new", out _, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("No Verify or Throws call at line");
    }

    /// <summary>
    /// A receiver that is a literal rather than a name. The scan back from the call steps over the
    /// literal whole - see SourceScan.PreviousSignificant - and lands on whatever precedes it,
    /// which must still read as "not the entry point".
    /// </summary>
    [Test]
    public async Task AppendSkipsAVerifyOnALiteralReceiver()
    {
        var source = Method("        Assert.Empty(\"some text\".Verify(value));");

        var status = TryApply(source, 5, InlinePatchMode.Append, null, "new", out _, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("No Verify or Throws call at line");
    }

    [Test]
    public async Task AppendSkipsAVerifyOnAnInstance()
    {
        var source = Method("        mock.VerifyAll();");

        var status = TryApply(source, 5, InlinePatchMode.Append, null, "new", out _, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("No Verify or Throws call at line");
    }

    // The entry point wrapping a helper of the same name: the outer call is the one to append to
    [Test]
    public async Task AppendPrefersTheEntryPointOverANestedHelper()
    {
        var source = Method("        await Verify(ContentValidation.Verify(value));");

        var status = TryApply(source, 5, InlinePatchMode.Append, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains(
            "        await Verify(ContentValidation.Verify(value))\n" +
            "            .Snapshot(\"new\");");
    }

    [Test]
    public async Task AppendToAVerifierQualifiedCall()
    {
        var source = Method("        await Verifier.Verify(value);");

        var status = TryApply(source, 5, InlinePatchMode.Append, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("await Verifier.Verify(value)\n            .Snapshot(\"new\");");
    }

    [Test]
    public async Task AppendToAThisQualifiedCall()
    {
        var source = Method("        await this.Verify(value);");

        var status = TryApply(source, 5, InlinePatchMode.Append, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("await this.Verify(value)\n            .Snapshot(\"new\");");
    }

    /// <summary>
    /// The throwing entry points return a SettingsTask like every other one, so a snapshot chains
    /// onto them identically. Searching for the Verify prefix alone left them all unpatchable, and
    /// silently: the verification declared itself inline, the append found nothing, and the
    /// verified file was deleted anyway - so the test could never go green again.
    /// </summary>
    [Test]
    [Arguments("Throws")]
    [Arguments("ThrowsTask")]
    [Arguments("ThrowsValueTask")]
    public async Task AppendToAThrowsCall(string entryPoint)
    {
        var source = Method($"        await {entryPoint}(() => Method(value));");

        var status = TryApply(source, 5, InlinePatchMode.Append, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains(
            $"        await {entryPoint}(() => Method(value))\n" +
            "            .Snapshot(\"new\");");
    }

    // The receiver check covers the new prefix too, or every Assert.Throws in the file becomes a
    // candidate the moment a hint goes stale
    [Test]
    public async Task AppendSkipsAThrowsOnAnotherReceiver()
    {
        var source = Method("        Assert.Throws<Exception>(() => Method(value));");

        var status = TryApply(source, 5, InlinePatchMode.Append, null, "new", out _, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("No Verify or Throws call at line");
    }

    /// <summary>
    /// Two entry points from different families on one line, the nearer one to the left. Names are
    /// searched one after another, so without a sort the Verify would win on being searched first
    /// rather than on being nearest, and the snapshot would land on the wrong call.
    /// </summary>
    [Test]
    public async Task AppendTakesTheLeftmostEntryPointOnALine()
    {
        var source = Method("        await Throws(() => Verify(value));");

        var status = TryApply(source, 5, InlinePatchMode.Append, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains(
            "        await Throws(() => Verify(value))\n" +
            "            .Snapshot(\"new\");");
    }

    /// <summary>
    /// The last line of a file with no newline after it, where the line's range ends at the end of
    /// the source rather than at a line break. The per-line search is bounded by that range, so
    /// this is the edge the bound is measured against.
    /// </summary>
    [Test]
    public async Task AppendOnTheFinalLineWithNoTrailingNewline()
    {
        var source = "class Tests\n{\n    Task Test() => Verify(value);";

        var status = TryApply(source, 3, InlinePatchMode.Append, null, "new", out var newSource, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(reason).IsEmpty();
        await Assert.That(newSource).Contains(".Snapshot(\"new\")");
    }

    // The name reaching the very last character, so the bounded search has no room to spare
    [Test]
    public async Task SetWhereTheLiteralEndsTheFile()
    {
        var source = "class Tests\n{\n    Task Test() => Verify(value).Snapshot(\"old\")";

        var status = TryApply(source, 3, InlinePatchMode.Set, "\"old\"", "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).EndsWith(".Snapshot(\"new\")");
    }

    [Test]
    public async Task RemoveTakesTheWholeLine()
    {
        var source = Method(
            "        await Verify(value)\n" +
            "            .Snapshot(\"\"\"\n" +
            "                old\n" +
            "                \"\"\");");

        var status = TryApply(source, 6, InlinePatchMode.Remove, null, "", out var newSource, out _);

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

        var status = TryApply(source, 7, InlinePatchMode.Remove, null, "", out var newSource, out _);

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

        var status = TryApply(source, 5, InlinePatchMode.Remove, null, "", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).IsEqualTo(Method("        await Verify(value);"));
    }

    [Test]
    public async Task RemoveWithCrlf()
    {
        var source = Method("        await Verify(value)\n            .Snapshot(\"old\");").Replace("\n", "\r\n");

        var status = TryApply(source, 6, InlinePatchMode.Remove, null, "", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).IsEqualTo(Method("        await Verify(value);").Replace("\n", "\r\n"));
    }

    [Test]
    public async Task RemovePicksTheSiteNearestTheHint()
    {
        var source = TwoCallSites("\"a\"", "\"b\"");

        var status = TryApply(source, 7, InlinePatchMode.Remove, null, "", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        var (a, b) = Segments(newSource);
        await Assert.That(a).Contains(".Snapshot(\"a\")");
        await Assert.That(b).DoesNotContain("Snapshot");
    }

    [Test]
    public async Task RemoveWhenTheCallIsNotChained()
    {
        var source = Method("        await Snapshot(\"old\");");

        var status = TryApply(source, 5, InlinePatchMode.Remove, null, "", out _, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("not a chained call");
    }

    [Test]
    public async Task RemoveWithNoSnapshotCall()
    {
        var source = Method("        await Verify(value);");

        var status = TryApply(source, 5, InlinePatchMode.Remove, null, "", out _, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("Could not find a Snapshot call");
    }

    [Test]
    public async Task TabIndentedFileUsesTabUnit()
    {
        var source = "class Tests\n{\n\tasync Task Test()\n\t{\n\t\tawait Snapshot();\n\t}\n}";
        var status = TryApply(source, 5, InlinePatchMode.Set, null, "a\nb", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("Snapshot(\n\t\t\t\"\"\"\n\t\t\ta\n\t\t\tb\n\t\t\t\"\"\");");
    }

    static string TabMethod(string body) =>
        $"class Tests\n{{\n\tasync Task Test()\n\t{{\n{body}\n\t}}\n}}";

    // Append works out its own indent unit, separately from the replace path
    [Test]
    public async Task AppendToATabIndentedFile()
    {
        var source = TabMethod("\t\tawait Verify(value);");

        var status = TryApply(source, 5, InlinePatchMode.Append, null, "a\nb", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).IsEqualTo(
            TabMethod(
                "\t\tawait Verify(value)\n" +
                "\t\t\t.Snapshot(\n" +
                "\t\t\t\t\"\"\"\n" +
                "\t\t\t\ta\n" +
                "\t\t\t\tb\n" +
                "\t\t\t\t\"\"\");"));
    }

    // The chain already sets the call indent, so only the content level comes from the unit
    [Test]
    public async Task AppendToATabIndentedChain()
    {
        var source = TabMethod(
            "\t\tawait Verify(value)\n" +
            "\t\t\t.UseDirectory(\"snapshots\");");

        var status = TryApply(source, 5, InlinePatchMode.Append, null, "a\nb", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).IsEqualTo(
            TabMethod(
                "\t\tawait Verify(value)\n" +
                "\t\t\t.UseDirectory(\"snapshots\")\n" +
                "\t\t\t.Snapshot(\n" +
                "\t\t\t\t\"\"\"\n" +
                "\t\t\t\ta\n" +
                "\t\t\t\tb\n" +
                "\t\t\t\t\"\"\");"));
    }

    [Test]
    public async Task RemoveFromATabIndentedChain()
    {
        var source = TabMethod(
            "\t\tawait Verify(value)\n" +
            "\t\t\t.Snapshot(\"\"\"\n" +
            "\t\t\t\told\n" +
            "\t\t\t\t\"\"\");");

        var status = TryApply(source, 6, InlinePatchMode.Remove, null, "", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).IsEqualTo(TabMethod("\t\tawait Verify(value);"));
    }

    static string MixedIndentSites() =>
        string.Join(
            "\n",
            "class Tests",
            "{",
            "    async Task Spaces()",
            "    {",
            "        await Verify(a).Snapshot(\"a\");",
            "    }",
            "",
            "\tasync Task Tabs()",
            "\t{",
            "\t\tawait Verify(b).Snapshot(\"b\");",
            "\t}",
            "}");

    // The unit comes from the call site's own line, not from the file, so a file that
    // indents inconsistently keeps each site consistent with itself
    [Test]
    public async Task MixedIndentFileUsesTheSiteIndent()
    {
        var source = MixedIndentSites();

        var spaces = TryApply(source, 5, InlinePatchMode.Set, "\"a\"", "a1\na2", out var afterSpaces, out _);
        // The first patch turned one line into five, so the second site has moved down four
        var tabs = TryApply(afterSpaces, 14, InlinePatchMode.Set, "\"b\"", "b1\nb2", out var afterTabs, out _);

        await Assert.That(spaces).IsEqualTo(PatchStatus.Applied);
        await Assert.That(tabs).IsEqualTo(PatchStatus.Applied);
        // Neither site picked up the other's whitespace
        await Assert.That(afterTabs).IsEqualTo(
            string.Join(
                "\n",
                "class Tests",
                "{",
                "    async Task Spaces()",
                "    {",
                "        await Verify(a).Snapshot(",
                "            \"\"\"",
                "            a1",
                "            a2",
                "            \"\"\");",
                "    }",
                "",
                "\tasync Task Tabs()",
                "\t{",
                "\t\tawait Verify(b).Snapshot(",
                "\t\t\t\"\"\"",
                "\t\t\tb1",
                "\t\t\tb2",
                "\t\t\t\"\"\");",
                "\t}",
                "}"));
    }

    // Tabs for indentation, spaces for alignment. The site's indentation ends in spaces, so
    // the level added continues in spaces: a tab would advance to the next tab stop from
    // wherever the alignment left off, a different width in every editor
    [Test]
    public async Task LineIndentedWithTabsThenSpaces()
    {
        var source = "class Tests\n{\n\tasync Task Test() =>\n\t    Verify(value).Snapshot(\"old\");\n}";

        var status = TryApply(source, 4, InlinePatchMode.Set, "\"old\"", "a\nb", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains(
            "Snapshot(\n" +
            "\t        \"\"\"\n" +
            "\t        a\n" +
            "\t        b\n" +
            "\t        \"\"\");");
    }

    static string TwoSpaceMethod(string body) =>
        $"class Tests\n{{\n  async Task Test()\n  {{\n{body}\n  }}\n}}";

    // A level is whatever the file makes it, not four spaces
    [Test]
    public async Task TwoSpaceFileUsesATwoSpaceUnit()
    {
        var source = TwoSpaceMethod("    await Snapshot(\"old\");");

        var status = TryApply(source, 5, InlinePatchMode.Set, "\"old\"", "a\nb", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).IsEqualTo(
            TwoSpaceMethod(
                "    await Snapshot(\n" +
                "      \"\"\"\n" +
                "      a\n" +
                "      b\n" +
                "      \"\"\");"));
    }

    [Test]
    public async Task AppendToATwoSpaceFile()
    {
        var source = TwoSpaceMethod("    await Verify(value);");

        var status = TryApply(source, 5, InlinePatchMode.Append, null, "a\nb", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).IsEqualTo(
            TwoSpaceMethod(
                "    await Verify(value)\n" +
                "      .Snapshot(\n" +
                "        \"\"\"\n" +
                "        a\n" +
                "        b\n" +
                "        \"\"\");"));
    }

    // The snapshot's own content lines are text, not indentation. Here they step by four and
    // outnumber the one real step of two, so counting them would measure the snapshot
    [Test]
    public async Task LiteralContentDoesNotSetTheUnit()
    {
        var expression = string.Join(
            "\n",
            "\"\"\"",
            "      a",
            "          b",
            "              c",
            "      \"\"\"");
        var source = string.Join(
            "\n",
            "class Tests",
            "{",
            $"  Task T() => Snapshot({expression});",
            "}");

        var status = TryApply(source, 3, InlinePatchMode.Set, expression, "x\ny", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).IsEqualTo(
            string.Join(
                "\n",
                "class Tests",
                "{",
                "  Task T() => Snapshot(",
                "    \"\"\"",
                "    x",
                "    y",
                "    \"\"\");",
                "}"));
    }

    // Nothing in the file indents, so there is no step to read and the default stands
    [Test]
    public async Task FileWithNoIndentationFallsBackToFourSpaces()
    {
        var source = "class Tests\n{\nasync Task Test() =>\nSnapshot(\"old\");\n}";

        var status = TryApply(source, 4, InlinePatchMode.Set, "\"old\"", "a\nb", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains(
            "Snapshot(\n" +
            "    \"\"\"\n" +
            "    a\n" +
            "    b\n" +
            "    \"\"\");");
    }

    // A literal whose content lines and closing delimiter disagree on tabs versus spaces
    // is not something the parser can strip an indent from, so it is left alone
    [Test]
    public async Task LiteralWithMismatchedIndentCharactersIsNotPatched()
    {
        var source = string.Join(
            "\n",
            "class Tests",
            "{",
            "    async Task Test() =>",
            "        Snapshot(\"\"\"",
            "\t        old",
            "            \"\"\");",
            "}");

        var status = TryApply(source, 4, InlinePatchMode.Set, null, "new", out _, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("is not a string literal");
    }

    [Test]
    public async Task HintBeyondEndOfFile()
    {
        var source = "await Snapshot();";
        var status = TryApply(source, 500, InlinePatchMode.Set, null, "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("Snapshot(\"new\");");
    }

    [Test]
    public async Task LfExpressionFoundInCrlfFile()
    {
        var source = Method($"        await Snapshot({rawOld.Replace("\n", "\n        ")});").Replace("\n", "\r\n");
        var expression = rawOld.Replace("\n", "\n        ");
        var status = TryApply(source, 5, InlinePatchMode.Set, expression, "new", out var newSource, out _);
        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).DoesNotContain("old");
    }

    [Test]
    public async Task OutsideSpanIsCharacterIdentical()
    {
        var body = "        await Snapshot(\"old\");";
        var source = Method(body);
        TryApply(source, 5, InlinePatchMode.Set, "\"old\"", "new", out var newSource, out _);
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

        var status = TryApply(source, 5, InlinePatchMode.Set, expression, content, out var newSource, out var reason);

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

        var status = TryApply(source, 5, InlinePatchMode.Set, null, content, out var newSource, out _);

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

        var status = TryApply(source, 5, InlinePatchMode.Set, expression, "new1\rnew2", out var newSource, out _);

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

        var status = TryApply(source, 7, InlinePatchMode.Set, "\"old\"", "new1\nnew2", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        // Untouched regions keep their original endings byte for byte
        await Assert.That(newSource.StartsWith(prefix, StringComparison.Ordinal)).IsTrue();
        await Assert.That(newSource.EndsWith(suffix, StringComparison.Ordinal)).IsTrue();
        // The spliced literal uses the file's dominant ending
        await Assert.That(newSource).Contains("Snapshot(\r\n            \"\"\"\r\n            new1\r\n            new2\r\n            \"\"\");");
    }

    [Test]
    public async Task SingleLineFileWithNoNewlines()
    {
        var status = TryApply("await Snapshot(\"old\");", 1, InlinePatchMode.Set, "\"old\"", "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("new");
        await Assert.That(newSource).DoesNotContain("\"old\"");
    }

    // The verify argument is the same text as the snapshot, and comes first on the line
    [Test]
    public async Task SameLiteralInTheVerifyArgumentIsNotPatched()
    {
        var source = Method("        await Verify(\"same\").Snapshot(\"same\");");

        var status = TryApply(source, 5, InlinePatchMode.Set, "\"same\"", "changed", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("await Verify(\"same\").Snapshot(\"changed\");");
    }

    // The expression search must match a whole argument, not the quoted part of a longer literal
    [Test]
    public async Task PrefixedLiteralIsNotPatchedThroughItsQuote()
    {
        var source = Method("        await Verify(x).Snapshot(@\"old\");");

        var status = TryApply(source, 5, InlinePatchMode.Set, "\"old\"", "new", out _, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("Re-run the test");
    }

    [Test]
    public async Task SuffixedLiteralIsNotPatchedThroughItsQuote()
    {
        var source = Method("        await Verify(x).Snapshot(\"old\"u8);");

        var status = TryApply(source, 5, InlinePatchMode.Set, "\"old\"", "new", out _, out var reason);

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

        var status = TryApply(source, 3, InlinePatchMode.Set, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("    // await Verify(x).Snapshot(\"doc example\");\n");
        await Assert.That(newSource).Contains("        await Verify(x).Snapshot(\"new\");");
    }

    [Test]
    public async Task CallInsideAStringIsSkipped()
    {
        var source = Method(
            "        var text = \"await Snapshot(\\\"x\\\")\";\n" +
            "        await Verify(x).Snapshot();");

        var status = TryApply(source, 5, InlinePatchMode.Set, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("var text = \"await Snapshot(\\\"x\\\")\";\n");
        await Assert.That(newSource).Contains("await Verify(x).Snapshot(\"new\");");
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

        var status = TryApply(source, 3, InlinePatchMode.Set, null, "new", out _, out var reason);

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

        var status = TryApply(source, 3, InlinePatchMode.Append, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains(
            "    Task VerifyThing(string value) => Verify(value)\n" +
            "        .Snapshot(\"new\");");
    }

    // Snapshot terminates the chain, so a comment in the middle of one must not end the walk
    [Test]
    public async Task AppendGoesAfterACommentInTheChain()
    {
        var source = Method(
            "        await Verify(value) // note\n" +
            "            .UseDirectory(\"snapshots\");");

        var status = TryApply(source, 5, InlinePatchMode.Append, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains(
            "            .UseDirectory(\"snapshots\")\n" +
            "            .Snapshot(\"new\");");
    }

    [Test]
    public async Task LiteralInACommentIsNotPatched()
    {
        var source = Method(
            "        // was \"old\"\n" +
            "        await Verify(x).Snapshot(\"old\");");

        var status = TryApply(source, 5, InlinePatchMode.Set, "\"old\"", "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("// was \"old\"\n");
        await Assert.That(newSource).Contains("Snapshot(\"new\");");
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

        var status = TryApply(source, 3, InlinePatchMode.Set, "\"old\"", "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("void Helper() => Log(\"old\");");
        await Assert.That(newSource).Contains("Snapshot(\"new\");");
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

        var status = TryApply(source, 7, InlinePatchMode.Set, "\"\"", "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("            content\n            \"\"\");");
        await Assert.That(newSource).Contains("Verify(b).Snapshot(\"new\");");
    }

    [Test]
    public async Task GenericSnapshotCall()
    {
        var source = Method("        await Verify(x).Snapshot<Thing>();");

        var status = TryApply(source, 5, InlinePatchMode.Set, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains(".Snapshot<Thing>(\"new\");");
    }

    [Test]
    public async Task AppendToAGenericVerify()
    {
        var source = Method("        await Verify<Thing>(value);");

        var status = TryApply(source, 5, InlinePatchMode.Append, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("await Verify<Thing>(value)\n            .Snapshot(\"new\");");
    }

    [Test]
    public async Task CommentInTheArgumentListIsNotTheArgument()
    {
        var source = Method("        await Verify(x).Snapshot(/* keep */);");

        var status = TryApply(source, 5, InlinePatchMode.Set, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("Snapshot(/* keep */\"new\");");
    }

    [Test]
    public async Task CommentAfterTheArgumentIsKept()
    {
        var source = Method("        await Verify(x).Snapshot(\"old\" /* why */);");

        var status = TryApply(source, 5, InlinePatchMode.Set, "\"old\"", "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("\"new\" /* why */);");
    }

    // Pulling the call up onto the line above would take the semicolon into the comment
    [Test]
    public async Task RemoveLeavesALineCommentAboveIntact()
    {
        var source = Method(
            "        await Verify(value)\n" +
            "            // note\n" +
            "            .Snapshot(\"old\");");

        var status = TryApply(source, 7, InlinePatchMode.Remove, null, "", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).IsEqualTo(
            Method(
                "        await Verify(value)\n" +
                "            // note\n" +
                "            ;"));
    }
}
