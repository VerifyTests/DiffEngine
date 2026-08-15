public class InlinePatcherFsTests
{
    // Line 5 is the first line of the body
    static string Test(string body) =>
        $"module Tests\n\n[<Fact>]\nlet MyTest () =\n{body}\n";

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
        InlinePatcher.TryApply(SourceLanguage.FSharp, source, lineHint, mode, originalExpression, originalValue, memberName, newContent, out newSource, out failReason);

    [Test]
    public async Task ReplaceRegularLiteral()
    {
        var source = Test("    Verifier.Verify(15).Snapshot(\"old\").ToTask() |> Async.AwaitTask");

        var status = TryApply(source, 5, InlinePatchMode.Set, "\"old\"", "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).IsEqualTo(
            Test("    Verifier.Verify(15).Snapshot(\"new\").ToTask() |> Async.AwaitTask"));
    }

    // The content of a triple-quoted literal is verbatim, so it starts on the delimiter's line
    // and every line after it sits at the left margin. An indent would be snapshot content
    [Test]
    public async Task MultiLineContentIsNotIndented()
    {
        var source = Test("    Verifier.Verify(15).Snapshot().ToTask() |> Async.AwaitTask");

        var status = TryApply(source, 5, InlinePatchMode.Set, null, "a\nb", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).IsEqualTo(
            Test("    Verifier.Verify(15).Snapshot(\"\"\"a\nb\"\"\").ToTask() |> Async.AwaitTask"));
    }

    // The closing delimiter would land at column 0..2, left of the statement, and F# reads that as
    // the end of the statement rather than as part of it. One source line instead
    [Test]
    public async Task ContentEndingInANewlineTakesTheEscapedForm()
    {
        var source = Test("    Verifier.Verify(15).Snapshot().ToTask() |> Async.AwaitTask");

        var status = TryApply(source, 5, InlinePatchMode.Set, null, "a\nb\n", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).IsEqualTo(
            Test("    Verifier.Verify(15).Snapshot(\"a\\nb\\n\").ToTask() |> Async.AwaitTask"));
    }

    // Same content, deeper call site: the last line no longer reaches the statement's column
    [Test]
    public async Task DeepCallSiteTakesTheEscapedForm()
    {
        var source = Test(
            "    let inner () =\n" +
            "        Verifier.Verify(15).Snapshot().ToTask()\n" +
            "    inner ()");

        var status = TryApply(source, 6, InlinePatchMode.Set, null, "a\nb", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("Snapshot(\"a\\nb\").ToTask()");
    }

    [Test]
    public async Task ReplaceTripleQuotedLiteral()
    {
        var source = Test("    Verifier.Verify(15).Snapshot(\"\"\"old1\nold2\"\"\").ToTask() |> Async.AwaitTask");

        var status = TryApply(source, 5, InlinePatchMode.Set, "\"\"\"old1\nold2\"\"\"", "new1\nnew2", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).IsEqualTo(
            Test("    Verifier.Verify(15).Snapshot(\"\"\"new1\nnew2\"\"\").ToTask() |> Async.AwaitTask"));
    }

    [Test]
    public async Task ReplacementUsesFileEol()
    {
        var source = Test("    Verifier.Verify(15).Snapshot(\"old\").ToTask()").Replace("\n", "\r\n");

        var status = TryApply(source, 5, InlinePatchMode.Set, "\"old\"", "a\nb", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("Snapshot(\"\"\"a\r\nb\"\"\")");
        await Assert.That(newSource).DoesNotContain("a\nb");
    }

    [Test]
    public async Task AlreadyAppliedWhenLiteralMatches()
    {
        var source = Test("    Verifier.Verify(15).Snapshot(\"same\").ToTask()");

        var status = TryApply(source, 5, InlinePatchMode.Set, "\"same\"", "same", out _, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.AlreadyApplied);
    }

    // What is rendered has to read back as what it was rendered from, or the next run patches
    // a literal it thinks is different
    [Test]
    public async Task ReapplyingTheSameContentIsAlreadyApplied()
    {
        var source = Test("    Verifier.Verify(15).Snapshot().ToTask()");

        TryApply(source, 5, InlinePatchMode.Set, null, "a\nb", out var applied, out _);
        var status = TryApply(applied, 5, InlinePatchMode.Set, null, "a\nb", out _, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.AlreadyApplied);
    }

    // F# does not implement CallerArgumentExpression (FS0202), so an F# patch is anchored by the
    // previous value instead: the call whose literal still means what the test run saw
    [Test]
    public async Task ValueAnchorFindsTheCall()
    {
        var source = Test("    Verifier.Verify(15).Snapshot(\"old\").ToTask()");

        var status = TryApply(source, 5, InlinePatchMode.Set, null, "new", out var newSource, out _, originalValue: "old");

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).IsEqualTo(Test("    Verifier.Verify(15).Snapshot(\"new\").ToTask()"));
    }

    // The hint is stale by two lines and lands on another test's snapshot. The value is what says
    // which call this patch came from, so the wrong one is left alone
    [Test]
    public async Task ValueAnchorBeatsAStaleHint()
    {
        var source = string.Join(
            "\n",
            "module Tests",
            "",
            "let TestA () =",
            "    Verifier.Verify(a).Snapshot(\"a\").ToTask()",
            "",
            "let TestB () =",
            "    Verifier.Verify(b).Snapshot(\"b\").ToTask()");

        var status = TryApply(source, 4, InlinePatchMode.Set, null, "new", out var newSource, out _, originalValue: "b");

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("Verify(a).Snapshot(\"a\")");
        await Assert.That(newSource).Contains("Verify(b).Snapshot(\"new\")");
    }

    // The literal that value described is gone, so the patch is stale. Reporting is the whole
    // point of having an anchor: the call at the hint is not known to be the right one
    [Test]
    public async Task ValueAnchorThatMatchesNothingIsNotFound()
    {
        var source = Test("    Verifier.Verify(15).Snapshot(\"something else\").ToTask()");

        var status = TryApply(source, 5, InlinePatchMode.Set, null, "new", out _, out var reason, originalValue: "old");

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("Re-run the test");
    }

    // Another process accepted it between the run and this apply
    [Test]
    public async Task ValueAnchorGoneAndContentAlreadyThereIsAlreadyApplied()
    {
        var source = Test("    Verifier.Verify(15).Snapshot(\"new\").ToTask()");

        var status = TryApply(source, 5, InlinePatchMode.Set, null, "new", out _, out _, originalValue: "old");

        await Assert.That(status).IsEqualTo(PatchStatus.AlreadyApplied);
    }

    [Test]
    public async Task ValueAnchorAcrossAMultiLineLiteral()
    {
        var source = Test("    Verifier.Verify(15).Snapshot(\"\"\"old1\nold2\"\"\").ToTask()");

        var status = TryApply(source, 5, InlinePatchMode.Set, null, "new", out var newSource, out _, originalValue: "old1\nold2");

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("Snapshot(\"new\")");
    }

    static string TwoTests(string literalA, string literalB) =>
        string.Join(
            "\n",
            "module Tests",
            "",
            "let TestA () =",
            $"    Verifier.Verify(a).Snapshot({literalA}).ToTask()",
            "",
            "let TestB () =",
            $"    Verifier.Verify(b).Snapshot({literalB}).ToTask()");

    // A call above TestB's declaration is not inside TestB, whatever the hint says, so the
    // identical snapshot in the test above is not even a candidate
    [Test]
    public async Task MemberNameBoundsTheSearch()
    {
        var source = TwoTests("\"dup\"", "\"dup\"");

        var status = TryApply(source, 4, InlinePatchMode.Set, null, "new", out var newSource, out _, originalValue: "dup", memberName: "TestB");

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("Verify(a).Snapshot(\"dup\")");
        await Assert.That(newSource).Contains("Verify(b).Snapshot(\"new\")");
    }

    // With neither anchor the member is all that keeps an overwrite in the right test
    [Test]
    public async Task MemberNameScopesAnAnchorlessOverwrite()
    {
        var source = TwoTests("\"a\"", "\"b\"");

        var status = TryApply(source, 4, InlinePatchMode.Set, null, "new", out var newSource, out _, memberName: "TestB");

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("Verify(a).Snapshot(\"a\")");
        await Assert.That(newSource).Contains("Verify(b).Snapshot(\"new\")");
    }

    // With neither anchor - a producer that predates the value field - the literal at the hint is
    // all there is, and taking it as the changed snapshot is better than never updating one
    [Test]
    public async Task ChangedSnapshotIsReplacedWithNothingToAnchorOn()
    {
        var source = Test("    Verifier.Verify(15).Snapshot(\"old\").ToTask()");

        var status = TryApply(source, 5, InlinePatchMode.Set, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).IsEqualTo(Test("    Verifier.Verify(15).Snapshot(\"new\").ToTask()"));
    }

    [Test]
    public async Task InsertIntoEmptyArgumentList()
    {
        var source = Test("    Verifier.Verify(15).Snapshot().ToTask()");

        var status = TryApply(source, 5, InlinePatchMode.Set, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("Snapshot(\"new\")");
    }

    // F# binds an argument to a name with =, not :
    [Test]
    public async Task InsertBeforeAnotherNamedArgument()
    {
        var source = Test("    Verifier.Verify(15).Snapshot(file = myFile).ToTask()");

        var status = TryApply(source, 5, InlinePatchMode.Set, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("Snapshot(expected = \"new\", file = myFile)");
    }

    [Test]
    public async Task ReplaceANamedExpectedArgument()
    {
        var source = Test("    Verifier.Verify(15).Snapshot(expected = \"old\").ToTask()");

        var status = TryApply(source, 5, InlinePatchMode.Set, "\"old\"", "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("Snapshot(expected = \"new\")");
    }

    // F# does not apply the conversion that lets a SettingsTask be awaited, so the chain ends in
    // ToTask. Snapshot returns the SettingsTask, so it has to go in front of it
    [Test]
    public async Task AppendGoesInFrontOfToTask()
    {
        var source = Test("    Verifier.Verify(15).ToTask() |> Async.AwaitTask");

        var status = TryApply(source, 5, InlinePatchMode.Append, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).IsEqualTo(
            Test(
                "    Verifier.Verify(15)\n" +
                "        .Snapshot(\"new\").ToTask() |> Async.AwaitTask"));
    }

    [Test]
    public async Task AppendToAMultiLineChain()
    {
        var source = Test(
            "    Verifier\n" +
            "        .Verify(15)\n" +
            "        .UseMethodName(\"customName\")\n" +
            "        .ToTask()\n" +
            "    |> Async.AwaitTask");

        var status = TryApply(source, 6, InlinePatchMode.Append, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).IsEqualTo(
            Test(
                "    Verifier\n" +
                "        .Verify(15)\n" +
                "        .UseMethodName(\"customName\")\n" +
                "        .Snapshot(\"new\")\n" +
                "        .ToTask()\n" +
                "    |> Async.AwaitTask"));
    }

    // Awaited in a task expression instead, so there is no ToTask and the chain end is the
    // insertion point
    [Test]
    public async Task AppendWithNoToTask()
    {
        var source = Test(
            "    task {\n" +
            "        do! Verifier.Verify(15)\n" +
            "    }");

        var status = TryApply(source, 6, InlinePatchMode.Append, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).IsEqualTo(
            Test(
                "    task {\n" +
                "        do! Verifier.Verify(15)\n" +
                "            .Snapshot(\"new\")\n" +
                "    }"));
    }

    [Test]
    public async Task AppendMultiLineContent()
    {
        var source = Test("    Verifier.Verify(15).ToTask()");

        var status = TryApply(source, 5, InlinePatchMode.Append, null, "a\nb", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).IsEqualTo(
            Test(
                "    Verifier.Verify(15)\n" +
                "        .Snapshot(\"\"\"a\nb\"\"\").ToTask()"));
    }

    [Test]
    public async Task AppendIsRefusedWhenOneIsAlreadyChained()
    {
        var source = Test("    Verifier.Verify(15).Snapshot(\"already\").ToTask()");

        var status = TryApply(source, 5, InlinePatchMode.Append, null, "new", out _, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("already has a Snapshot call");
    }

    [Test]
    public async Task AppendSkipsAVerifyOnAnotherReceiver()
    {
        var source = Test("    Assert.isEmpty (ContentValidation.Verify(value))");

        var status = TryApply(source, 5, InlinePatchMode.Append, null, "new", out _, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("Could not find a Verify call");
    }

    [Test]
    public async Task RemoveTakesTheWholeLine()
    {
        var source = Test(
            "    Verifier.Verify(15)\n" +
            "        .Snapshot(\"\"\"old1\nold2\"\"\")\n" +
            "        .ToTask() |> Async.AwaitTask");

        var status = TryApply(source, 6, InlinePatchMode.Remove, null, "", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).IsEqualTo(
            Test(
                "    Verifier.Verify(15)\n" +
                "        .ToTask() |> Async.AwaitTask"));
    }

    [Test]
    public async Task RemoveFromASingleLineChain()
    {
        var source = Test("    Verifier.Verify(15).Snapshot(\"old\").ToTask()");

        var status = TryApply(source, 5, InlinePatchMode.Remove, null, "", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).IsEqualTo(Test("    Verifier.Verify(15).ToTask()"));
    }

    [Test]
    public async Task LineCommentedOutCallIsSkipped()
    {
        var source = string.Join(
            "\n",
            "module Tests",
            "",
            "// Verifier.Verify(x).Snapshot(\"doc example\")",
            "let MyTest () =",
            "    Verifier.Verify(x).Snapshot().ToTask()");

        var status = TryApply(source, 3, InlinePatchMode.Set, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("// Verifier.Verify(x).Snapshot(\"doc example\")\n");
        await Assert.That(newSource).Contains("    Verifier.Verify(x).Snapshot(\"new\").ToTask()");
    }

    [Test]
    public async Task BlockCommentedOutCallIsSkipped()
    {
        var source = string.Join(
            "\n",
            "module Tests",
            "",
            "(* Verifier.Verify(x).Snapshot(\"doc example\") *)",
            "let MyTest () =",
            "    Verifier.Verify(x).Snapshot().ToTask()");

        var status = TryApply(source, 3, InlinePatchMode.Set, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("(* Verifier.Verify(x).Snapshot(\"doc example\") *)\n");
        await Assert.That(newSource).Contains("    Verifier.Verify(x).Snapshot(\"new\").ToTask()");
    }

    // Block comments nest, so the inner close does not end the outer comment
    [Test]
    public async Task NestedBlockCommentIsOneComment()
    {
        var source = string.Join(
            "\n",
            "module Tests",
            "",
            "(* outer (* inner *) .Snapshot(\"commented\") *)",
            "let MyTest () =",
            "    Verifier.Verify(x).Snapshot().ToTask()");

        var status = TryApply(source, 3, InlinePatchMode.Set, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains(".Snapshot(\"commented\") *)\n");
        await Assert.That(newSource).Contains("    Verifier.Verify(x).Snapshot(\"new\").ToTask()");
    }

    // (*) is the multiplication operator, not an empty comment that would swallow the file
    [Test]
    public async Task MultiplyOperatorIsNotAComment()
    {
        var source = string.Join(
            "\n",
            "module Tests",
            "",
            "let multiply = (*)",
            "let MyTest () =",
            "    Verifier.Verify(x).Snapshot().ToTask()");

        var status = TryApply(source, 5, InlinePatchMode.Set, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("Snapshot(\"new\")");
    }

    [Test]
    public async Task CallInsideAStringIsSkipped()
    {
        var source = Test(
            "    let text = \"Verifier.Verify(x).Snapshot(\\\"y\\\")\"\n" +
            "    Verifier.Verify(text).Snapshot().ToTask()");

        var status = TryApply(source, 5, InlinePatchMode.Set, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("let text = \"Verifier.Verify(x).Snapshot(\\\"y\\\")\"\n");
        await Assert.That(newSource).Contains("Verifier.Verify(text).Snapshot(\"new\").ToTask()");
    }

    [Test]
    public async Task CallInsideATripleQuotedStringIsSkipped()
    {
        var source = Test(
            "    let text = \"\"\"Verifier.Verify(x).Snapshot(\"y\")\"\"\"\n" +
            "    Verifier.Verify(text).Snapshot().ToTask()");

        var status = TryApply(source, 5, InlinePatchMode.Set, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("let text = \"\"\"Verifier.Verify(x).Snapshot(\"y\")\"\"\"\n");
        await Assert.That(newSource).Contains("Verifier.Verify(text).Snapshot(\"new\").ToTask()");
    }

    // A type parameter is a tick with no closing tick, so reading one as a char literal would
    // take the rest of the file out of the scan
    [Test]
    public async Task TypeParameterIsNotACharLiteral()
    {
        var source = Test(
            "    let values : 'T list = []\n" +
            "    Verifier.Verify(values).Snapshot(\"old\").ToTask()");

        var status = TryApply(source, 6, InlinePatchMode.Set, "\"old\"", "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("Snapshot(\"new\")");
    }

    [Test]
    public async Task TickInAnIdentifierIsNotACharLiteral()
    {
        var source = Test(
            "    let value' = 15\n" +
            "    Verifier.Verify(value').Snapshot(\"old\").ToTask()");

        var status = TryApply(source, 6, InlinePatchMode.Set, "\"old\"", "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("Verifier.Verify(value').Snapshot(\"new\")");
    }

    // The quote inside the char literal must not open a string
    [Test]
    public async Task CharLiteralIsSkipped()
    {
        var source = Test(
            "    let quote = '\"'\n" +
            "    Verifier.Verify(quote).Snapshot(\"old\").ToTask()");

        var status = TryApply(source, 6, InlinePatchMode.Set, "\"old\"", "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("let quote = '\"'\n");
        await Assert.That(newSource).Contains("Snapshot(\"new\")");
    }

    [Test]
    public async Task LetDeclarationIsNotMistakenForACall()
    {
        var source = string.Join(
            "\n",
            "module Tests",
            "",
            "let Snapshot (expected: string) = expected");

        var status = TryApply(source, 3, InlinePatchMode.Set, null, "new", out _, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("Could not find a Snapshot call");
    }

    [Test]
    public async Task MemberDeclarationIsNotMistakenForACall()
    {
        var source = string.Join(
            "\n",
            "module Tests",
            "",
            "type Extensions =",
            "    member this.Snapshot (expected: string) = expected",
            "    static member Snapshot (expected: string, other: string) = expected");

        var status = TryApply(source, 4, InlinePatchMode.Set, null, "new", out _, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("Could not find a Snapshot call");
    }

    [Test]
    public async Task GenericSnapshotCall()
    {
        var source = Test("    Verifier.Verify(x).Snapshot<Thing>().ToTask()");

        var status = TryApply(source, 5, InlinePatchMode.Set, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains(".Snapshot<Thing>(\"new\")");
    }

    // The B is part of the literal token, so the expression search must not match through it
    [Test]
    public async Task ByteStringIsNotPatchedThroughItsQuote()
    {
        var source = Test("    Verifier.Verify(x).Snapshot(\"old\"B).ToTask()");

        var status = TryApply(source, 5, InlinePatchMode.Set, "\"old\"", "new", out _, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("not a string literal");
    }

    [Test]
    public async Task NoCallFound()
    {
        var source = Test("    Verifier.Verify(15).ToTask()");

        var status = TryApply(source, 5, InlinePatchMode.Set, null, "new", out _, out var reason);

        await Assert.That(status).IsEqualTo(PatchStatus.NotFound);
        await Assert.That(reason).Contains("Could not find a Snapshot call");
    }

    // Two tests in the same file producing the same result: both sites must end up patched, and
    // the second apply must not mistake the first for its own
    [Test]
    public async Task SequentialPatchesOfIdenticalLiterals()
    {
        var source = string.Join(
            "\n",
            "module Tests",
            "",
            "let TestA () =",
            "    Verifier.Verify(a).Snapshot(\"old\").ToTask()",
            "",
            "let TestB () =",
            "    Verifier.Verify(b).Snapshot(\"old\").ToTask()");

        var first = TryApply(source, 4, InlinePatchMode.Set, "\"old\"", "newA", out var afterFirst, out _);
        var second = TryApply(afterFirst, 7, InlinePatchMode.Set, "\"old\"", "newB", out var afterSecond, out _);

        await Assert.That(first).IsEqualTo(PatchStatus.Applied);
        await Assert.That(second).IsEqualTo(PatchStatus.Applied);
        await Assert.That(afterSecond).Contains("Verify(a).Snapshot(\"newA\")");
        await Assert.That(afterSecond).Contains("Verify(b).Snapshot(\"newB\")");
    }

    [Test]
    public async Task TabIndentedFileUsesTabUnit()
    {
        var source = string.Join(
            "\n",
            "module Tests",
            "",
            "let MyTest () =",
            "\tVerifier.Verify(15).ToTask()");

        var status = TryApply(source, 4, InlinePatchMode.Append, null, "new", out var newSource, out _);

        await Assert.That(status).IsEqualTo(PatchStatus.Applied);
        await Assert.That(newSource).Contains("\tVerifier.Verify(15)\n\t\t.Snapshot(\"new\").ToTask()");
    }
}
