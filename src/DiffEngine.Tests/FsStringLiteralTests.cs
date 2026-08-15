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
        "tab\there\nsecond"
    ];

    // Content is verbatim, so it starts on the delimiter's line and stays at the left margin
    [Test]
    public async Task RenderMultiLine()
    {
        var rendered = FsStringLiteral.RenderMultiLine("a\nb", "\n");
        await Assert.That(rendered).IsEqualTo("\"\"\"a\nb\"\"\"");
    }

    [Test]
    public async Task RenderMultiLineIndentIsContent()
    {
        var rendered = FsStringLiteral.RenderMultiLine("a\n    indented\nb", "\n");
        await Assert.That(rendered).IsEqualTo("\"\"\"a\n    indented\nb\"\"\"");
    }

    [Test]
    public async Task RenderEmpty()
    {
        var rendered = FsStringLiteral.RenderMultiLine("", "\n");
        await Assert.That(rendered).IsEqualTo("\"\"");
    }

    [Test]
    public async Task RenderCrlf()
    {
        var rendered = FsStringLiteral.RenderMultiLine("a\nb", "\r\n");
        await Assert.That(rendered).IsEqualTo("\"\"\"a\r\nb\"\"\"");
    }

    // F# cannot widen a triple-quoted delimiter, so content that runs into one takes the
    // verbatim form instead
    [Test]
    [Arguments("has \"\"\" inside\nsecond", "@\"has \"\"\"\"\"\" inside\nsecond\"")]
    [Arguments("\"starts with a quote\nsecond", "@\"\"\"starts with a quote\nsecond\"")]
    [Arguments("ends with a quote\nsecond\"", "@\"ends with a quote\nsecond\"\"\"")]
    public async Task RenderMultiLineFallsBackToVerbatim(string content, string expected)
    {
        var rendered = FsStringLiteral.RenderMultiLine(content, "\n");
        await Assert.That(rendered).IsEqualTo(expected);
    }

    // A single quote in the middle is no problem for the triple-quoted form
    [Test]
    public async Task RenderMultiLineKeepsQuotesInTheMiddle()
    {
        var rendered = FsStringLiteral.RenderMultiLine("a \"quoted\" word\nsecond", "\n");
        await Assert.That(rendered).IsEqualTo("\"\"\"a \"quoted\" word\nsecond\"\"\"");
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
            var rendered = FsStringLiteral.RenderMultiLine(content, eol);
            await Assert.That(rendered).IsEqualTo(FsStringLiteral.RenderMultiLine(normalized, eol));

            var parsed = FsStringLiteral.TryParse(rendered, out var value);
            await Assert.That(parsed).IsTrue();
            await Assert.That(value).IsEqualTo(normalized);
        }
    }

    [Test]
    public async Task RenderRoundTrips()
    {
        foreach (var content in renderRoundTripCases)
        {
            foreach (var eol in new[] { "\n", "\r\n" })
            {
                var rendered = FsStringLiteral.RenderMultiLine(content, eol);
                var parsed = FsStringLiteral.TryParse(rendered, out var value);
                await Assert.That(parsed).IsTrue();
                await Assert.That(value).IsEqualTo(content);
            }
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
    [Arguments("a\nb")]
    [Arguments("a\rb")]
    [Arguments("a\r\nb")]
    [Arguments("\nabc")]
    [Arguments("abc\n")]
    public async Task RenderMultiLineIsVerbatim(string content)
    {
        var rendered = FsStringLiteral.Render(content, "", "\n");
        await Assert.That(rendered).IsEqualTo(FsStringLiteral.RenderMultiLine(content, "\n"));
    }

    // The closing delimiter lands left of the column the statement started in, which F# reads as
    // the end of the statement rather than as part of it. Continuations instead: a line at a time,
    // and every line indented, so there is nothing for the layout to trip over
    [Test]
    [Arguments("a\nb", "\"a\\n\\\n        b\"")]
    [Arguments("has \"quotes\" and a \\\nx", "\"has \\\"quotes\\\" and a \\\\\\n\\\n        x\"")]
    // A trailing newline leaves nothing to continue to, so its break stays on the line above
    [Arguments("abc\n", "\"abc\\n\"")]
    public async Task RenderMultiLineFallsBackWhenItWouldBreakTheLayout(string content, string expected)
    {
        var rendered = FsStringLiteral.Render(content, "        ", "\n");
        await Assert.That(rendered).IsEqualTo(expected);
    }

    [Test]
    public async Task RenderContinued()
    {
        var rendered = FsStringLiteral.RenderContinued("line one\nline two", "    ", "\n");
        await Assert.That(rendered).IsEqualTo("\"line one\\n\\\n    line two\"");
    }

    // The continuation drops the newline and the whitespace after it, and cannot tell the
    // indentation it was written with from a space the snapshot starts with. So the first one is
    // an escape, which is where the skipping stops, and the rest are content
    [Test]
    public async Task RenderContinuedEscapesALeadingSpace()
    {
        var rendered = FsStringLiteral.RenderContinued("a\n    indented\nb", "    ", "\n");
        await Assert.That(rendered).IsEqualTo("\"a\\n\\\n    \\x20   indented\\n\\\n    b\"");
    }

    [Test]
    public async Task RenderContinuedBlankLine()
    {
        var rendered = FsStringLiteral.RenderContinued("a\n\nb", "    ", "\n");
        await Assert.That(rendered).IsEqualTo("\"a\\n\\\n    \\n\\\n    b\"");
    }

    [Test]
    public async Task RenderContinuedTrailingNewline()
    {
        var rendered = FsStringLiteral.RenderContinued("a\nb\n", "    ", "\n");
        await Assert.That(rendered).IsEqualTo("\"a\\n\\\n    b\\n\"");
    }

    [Test]
    public async Task RenderContinuedCrlf()
    {
        var rendered = FsStringLiteral.RenderContinued("a\nb", "\t", "\r\n");
        await Assert.That(rendered).IsEqualTo("\"a\\n\\\r\n\tb\"");
    }

    [Test]
    public async Task RenderContinuedRoundTrips()
    {
        foreach (var content in renderRoundTripCases)
        {
            foreach (var eol in new[] { "\n", "\r\n" })
            {
                foreach (var indent in new[] { "", "    ", "        " })
                {
                    var rendered = FsStringLiteral.RenderContinued(content, indent, eol);
                    var parsed = FsStringLiteral.TryParse(rendered, out var value);
                    await Assert.That(parsed).IsTrue();
                    await Assert.That(value).IsEqualTo(SourceLanguage.NormalizeNewlines(content));
                }
            }
        }
    }

    // The last line reaches the splice column, so the verbatim form stands
    [Test]
    public async Task RenderMultiLineKeptWhenTheLastLineIsLongEnough()
    {
        var rendered = FsStringLiteral.Render("first\nlong enough", "        ", "\n");
        await Assert.That(rendered).IsEqualTo("\"\"\"first\nlong enough\"\"\"");
    }

    [Test]
    [Arguments("first\nabcde", "        ", true)]
    [Arguments("first\nabcd", "        ", false)]
    [Arguments("first\na", "    ", true)]
    [Arguments("first\n", "    ", false)]
    [Arguments("first\n", "", true)]
    public async Task ClearsOffsideLine(string content, string indent, bool expected)
    {
        var rendered = FsStringLiteral.RenderMultiLine(content, "\n");
        await Assert.That(FsStringLiteral.ClearsOffsideLine(rendered, indent)).IsEqualTo(expected);
    }

    [Test]
    public async Task RenderRoundTripsWhicheverFormItPicks()
    {
        foreach (var content in renderRoundTripCases)
        {
            foreach (var eol in new[] { "\n", "\r\n" })
            {
                foreach (var indent in new[] { "", "    ", "            " })
                {
                    var rendered = FsStringLiteral.Render(content, indent, eol);
                    var parsed = FsStringLiteral.TryParse(rendered, out var value);
                    await Assert.That(parsed).IsTrue();
                    await Assert.That(value).IsEqualTo(content);
                }
            }
        }
    }

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
    [Arguments("\"\"\"a\"b\"\"\"", "a\"b")]
    // Ordinary F# strings may span lines
    [Arguments("\"a\nb\"", "a\nb")]
    public async Task Parse(string expression, string expected)
    {
        var parsed = FsStringLiteral.TryParse(expression, out var value);
        await Assert.That(parsed).IsTrue();
        await Assert.That(value).IsEqualTo(expected);
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

    // Where C# would drop the first line and strip the closing delimiter's indent, F# keeps
    // every character between the delimiters
    [Test]
    public async Task ParseTripleQuotedIsVerbatim()
    {
        var expression = "\"\"\"\n      a\n\n      b\n      \"\"\"";
        var parsed = FsStringLiteral.TryParse(expression, out var value);
        await Assert.That(parsed).IsTrue();
        await Assert.That(value).IsEqualTo("\n      a\n\n      b\n      ");
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
}
