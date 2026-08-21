public class FsStringLiteralTests
{
    static readonly string[] renderRoundTripCases =
    [
        "",
        " ",
        "abc",
        "abc\"\"\"",
        "\"\"\"\n\"\"\"",
        "a\nb",
        "\nabc",
        "abc\n",
        "\nabc\n",
        "a\n\n\nb",
        "\"",
        "\"\"",
        "\"\"\"",
        "\"\"\"\"\"\"",
        "\"\"\"starts with quotes",
        "ends with quote\"",
        "$ {value} {{x}}",
        "a\n   \nb",
        "trailing space  \nnext",
        "emoji 🎈 and unicode ☂",
        "line1\n    indented\nline3",
        "back\\slash\nsecond",
        "tab\there\nsecond",
        "{\n  \"name\": \"value\"\n}",
        "x" + lineSeparator + "y",
        "a\nb" + lineSeparator + "c",
        "a\nb" + nextLine + "c",
        "a\nb" + paragraphSeparator + "c"
    ];

    const char nextLine = (char) 0x85;
    const char lineSeparator = (char) 0x2028;
    const char paragraphSeparator = (char) 0x2029;

    // The same shape C# writes: the content indented under the call, with the first line and the
    // closing delimiter's indentation there to be taken back off
    [Test]
    public async Task RenderMultiLine()
    {
        var rendered = FsStringLiteral.Render("line one\nline two", "    ", "\n");
        await Assert.That(rendered).IsEqualTo("\"\"\"\n    line one\n    line two\n    \"\"\"");
    }

    [Test]
    public async Task RenderMultiLineMatchesCSharp()
    {
        foreach (var content in renderRoundTripCases)
        {
            if (content.IndexOf('\n') == -1 ||
                content.Contains("\"\"\"") ||
                content.StartsWith('"') ||
                content.EndsWith('"'))
            {
                continue;
            }

            var fsharp = FsStringLiteral.Render(content, "    ", "\n");
            var csharp = CsStringLiteral.Render(content, "    ", "\n");
            await Assert.That(fsharp).IsEqualTo(csharp);
        }
    }

    [Test]
    [Arguments(0x85)]
    [Arguments(0x2028)]
    [Arguments(0x2029)]
    public async Task RenderFallsBackToRegularWhenContentHoldsLineTerminator(int codePoint)
    {
        var terminator = (char) codePoint;
        var content = $"a\nb{terminator}c";
        var rendered = FsStringLiteral.Render(content, "    ", "\n");

        // Kept off the triple-quoted form for the reason C# is: the two write the one shape, and
        // that is what RenderMultiLineMatchesCSharp is asserting over these same cases
        await Assert.That(rendered.StartsWith("\"\"\"", StringComparison.Ordinal)).IsFalse();
        await Assert.That(rendered).DoesNotContain(terminator.ToString());
        await Assert.That(FsStringLiteral.TryParse(rendered, out var value)).IsTrue();
        await Assert.That(value).IsEqualTo(content);
    }

    [Test]
    public async Task RenderBlankLineHasNoTrailingWhitespace()
    {
        var rendered = FsStringLiteral.Render("a\n\nb", "    ", "\n");
        await Assert.That(rendered).IsEqualTo("\"\"\"\n    a\n\n    b\n    \"\"\"");
    }

    [Test]
    public async Task RenderCrlf()
    {
        var rendered = FsStringLiteral.Render("a\nb", "\t", "\r\n");
        await Assert.That(rendered).IsEqualTo("\"\"\"\r\n\ta\r\n\tb\r\n\t\"\"\"");
    }

    // F# cannot widen a delimiter the way C# can, so content that runs into one has no multi-line
    // form at all and takes a regular literal on one line
    [Test]
    [Arguments("has \"\"\" inside\nsecond", "\"has \\\"\\\"\\\" inside\\nsecond\"")]
    [Arguments("\"starts with a quote\nsecond", "\"\\\"starts with a quote\\nsecond\"")]
    [Arguments("ends with a quote\nsecond\"", "\"ends with a quote\\nsecond\\\"\"")]
    public async Task RenderFallsBackWhenTheDelimiterCannotHoldIt(string content, string expected)
    {
        var rendered = FsStringLiteral.Render(content, "    ", "\n");
        await Assert.That(rendered).IsEqualTo(expected);
    }

    // A single quote in the middle is no problem for the triple-quoted form
    [Test]
    public async Task RenderKeepsQuotesInTheMiddle()
    {
        var rendered = FsStringLiteral.Render("a \"quoted\" word\nsecond", "    ", "\n");
        await Assert.That(rendered).IsEqualTo("\"\"\"\n    a \"quoted\" word\n    second\n    \"\"\"");
    }

    [Test]
    [Arguments("a\r\nb")]
    [Arguments("a\rb")]
    [Arguments("a\r\nb\rc\nd")]
    public async Task RenderNormalizesCarriageReturns(string content)
    {
        // Content is meant to arrive \n normalized; a stray \r must not reach the literal
        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var eol in new[] { "\n", "\r\n" })
        {
            var rendered = FsStringLiteral.Render(content, "    ", eol);
            await Assert.That(rendered).IsEqualTo(FsStringLiteral.Render(normalized, "    ", eol));

            var parsed = FsStringLiteral.TryParse(rendered, out var value);
            await Assert.That(parsed).IsTrue();
            await Assert.That(value).IsEqualTo(normalized);
        }
    }

    [Test]
    [Arguments("abc", "\"abc\"")]
    [Arguments("", "\"\"")]
    [Arguments(" ", "\" \"")]
    [Arguments("has \"quotes\"", "\"has \\\"quotes\\\"\"")]
    [Arguments("back\\slash", "\"back\\\\slash\"")]
    [Arguments("tab\there", "\"tab\\there\"")]
    [Arguments("bell\a", "\"bell\\a\"")]
    // F# has no \0 or \e escape, so the \u form carries every other control character
    [Arguments("esc\u001b", "\"esc\\u001b\"")]
    [Arguments("null\0", "\"null\\u0000\"")]
    [Arguments("emoji 🎈 and unicode ☂", "\"emoji 🎈 and unicode ☂\"")]
    [Arguments("$ {value} {{x}}", "\"$ {value} {{x}}\"")]
    public async Task RenderSingleLineIsRegular(string content, string expected)
    {
        var rendered = FsStringLiteral.Render(content, "    ", "\n");
        await Assert.That(rendered).IsEqualTo(expected);
    }

    [Test]
    public async Task RenderRoundTrips()
    {
        foreach (var content in renderRoundTripCases)
        {
            foreach (var eol in new[] { "\n", "\r\n" })
            {
                foreach (var indent in new[] { "", "    ", "        " })
                {
                    var rendered = FsStringLiteral.Render(content, indent, eol);
                    var parsed = FsStringLiteral.TryParse(rendered, out var value);
                    await Assert.That(parsed).IsTrue();
                    await Assert.That(value).IsEqualTo(content);
                }
            }
        }
    }

    // What F# hands over for a rendered literal is the source text between the delimiters, so the
    // convention has to give the content back
    [Test]
    public async Task StripLayoutIsTheInverseOfRender()
    {
        foreach (var content in renderRoundTripCases)
        {
            foreach (var indent in new[] { "", "    ", "        " })
            {
                var rendered = FsStringLiteral.Render(content, indent, "\n");
                // What the F# compiler produces: everything between the delimiters, verbatim
                if (!rendered.StartsWith("\"\"\""))
                {
                    continue;
                }

                var compilerValue = rendered.Substring(3, rendered.Length - 6);
                await Assert.That(FsStringLiteral.StripLayout(compilerValue)).IsEqualTo(content);
            }
        }
    }

    [Test]
    public async Task StripLayout() =>
        await Assert.That(FsStringLiteral.StripLayout("\n    line one\n    line two\n    ")).IsEqualTo("line one\nline two");

    // A blank line before the closing delimiter is how content ending in a newline is written
    [Test]
    public async Task StripLayoutTrailingNewline() =>
        await Assert.That(FsStringLiteral.StripLayout("\n    line one\n\n    ")).IsEqualTo("line one\n");

    // Anything not in that shape is its own value: a single line, or a literal written some other
    // way, or a snapshot that only looks like layout
    [Test]
    [Arguments("the value")]
    [Arguments("line one\nline two")]
    [Arguments("\n    line one\n    not the indent")]
    [Arguments("")]
    public async Task StripLayoutLeavesOtherValuesAlone(string value) =>
        await Assert.That(FsStringLiteral.StripLayout(value)).IsEqualTo(value);

    [Test]
    [Arguments("\"a\"", "a")]
    [Arguments("\"\"", "")]
    [Arguments("\"a\\nb\"", "a\nb")]
    [Arguments("\"tab\\there\"", "tab\there")]
    [Arguments("\"quote\\\"q\"", "quote\"q")]
    [Arguments("\"back\\\\slash\"", "back\\slash")]
    [Arguments("\"\\u0041\"", "A")]
    [Arguments("\"\\x41\"", "A")]
    [Arguments("\"\\U0001F600\"", "😀")]
    [Arguments("\"\\065\"", "A")]
    [Arguments("@\"a\"\"b\"", "a\"b")]
    [Arguments("@\"\"", "")]
    // Verbatim has no triple-quoted form: the run after @" is an escaped quote
    [Arguments("@\"\"\"abc\"\"\"", "\"abc\"")]
    // Single line triple-quoted content is verbatim, with no layout to take off
    [Arguments("\"\"\"a\"b\"\"\"", "a\"b")]
    // Ordinary F# strings may span lines
    [Arguments("\"a\nb\"", "a\nb")]
    public async Task Parse(string expression, string expected)
    {
        var parsed = FsStringLiteral.TryParse(expression, out var value);
        await Assert.That(parsed).IsTrue();
        await Assert.That(value).IsEqualTo(expected);
    }

    // Half a surrogate pair, and a code point past the Unicode range. ConvertFromUtf32 throws on
    // both, and a throw here reaches the process applying the patch
    [Test]
    [Arguments("\"\\U0000D800\"")]
    [Arguments("\"\\U0000DFFF\"")]
    [Arguments("\"\\U00110000\"")]
    [Arguments("\"\\UFFFFFFFF\"")]
    public async Task ParseRejectsUnreadableCodePoint(string expression)
    {
        var parsed = FsStringLiteral.TryParse(expression, out _);
        await Assert.That(parsed).IsFalse();
    }

    [Test]
    public async Task ParseMultiLineTakesTheLayoutOff()
    {
        var expression = "\"\"\"\n      a\n\n      b\n      \"\"\"";
        var parsed = FsStringLiteral.TryParse(expression, out var value);
        await Assert.That(parsed).IsTrue();
        await Assert.That(value).IsEqualTo("a\n\nb");
    }

    // A backslash before a line break drops the break and the indentation that follows it
    [Test]
    public async Task ParseLineContinuation()
    {
        var parsed = FsStringLiteral.TryParse("\"a\\\n        b\"", out var value);
        await Assert.That(parsed).IsTrue();
        await Assert.That(value).IsEqualTo("ab");
    }

    [Test]
    public async Task ParseMultiLineVerbatim()
    {
        var parsed = FsStringLiteral.TryParse("@\"a\r\nb\"", out var value);
        await Assert.That(parsed).IsTrue();
        await Assert.That(value).IsEqualTo("a\nb");
    }

    [Test]
    [Arguments("$\"interpolated\"")]
    [Arguments("$\"\"\"interpolated\"\"\"")]
    [Arguments("nameof(x)")]
    [Arguments("\"a\" + \"b\"")]
    [Arguments("\"unterminated")]
    [Arguments("identifier")]
    [Arguments("")]
    // A byte string is not a string
    [Arguments("\"bytes\"B")]
    [Arguments("@\"bytes\"B")]
    // A trigraph is three digits or nothing
    [Arguments("\"\\0\"")]
    [Arguments("\"\\12\"")]
    // Not an F# escape
    [Arguments("\"\\e\"")]
    public async Task ParseRejects(string expression)
    {
        var parsed = FsStringLiteral.TryParse(expression, out _);
        await Assert.That(parsed).IsFalse();
    }

    // A triple-quoted literal that is not written to the layout convention. F# has no raw string
    // form, so this is a perfectly good literal and its value is simply what it says - which is
    // also what StripLayout returns for the same text, and the two have to agree or an
    // OriginalValue anchor can never match.
    [Test]
    [Arguments("\"\"\"\n  a\n      \"\"\"", "\n  a\n      ")]
    // The idiomatic hand-written shape: content starting on the opening line.
    [Arguments("\"\"\"{\n  \"a\": 1\n}\"\"\"", "{\n  \"a\": 1\n}")]
    public async Task ParseKeepsContentWithNoLayoutToStrip(string expression, string expected)
    {
        var parsed = FsStringLiteral.TryParse(expression, out var value);
        await Assert.That(parsed).IsTrue();
        await Assert.That(value).IsEqualTo(expected);
        // Whatever a producer sends as OriginalValue for this literal goes through StripLayout,
        // so the two readers have to land in the same place
        await Assert.That(FsStringLiteral.StripLayout(value!)).IsEqualTo(expected);
    }
}
