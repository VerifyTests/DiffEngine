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
}
