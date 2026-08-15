public class CsStringLiteralTests
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
        "x" + nextLine + "y",
        "x" + lineSeparator + "y",
        "x" + paragraphSeparator + "y",
        "a\nb" + lineSeparator + "c",
        lineSeparator + "leading",
        "trailing" + paragraphSeparator
    ];

    // Line terminators to the C# lexer, past \n and \r. Named by code point rather than written
    // into a literal, so nothing between here and the compiler can normalize them away
    const char nextLine = (char) 0x85;
    const char lineSeparator = (char) 0x2028;
    const char paragraphSeparator = (char) 0x2029;

    [Test]
    public async Task RenderSimple()
    {
        var rendered = CsStringLiteral.RenderRaw("abc", "    ", "\n");
        await Assert.That(rendered).IsEqualTo("\"\"\"\n    abc\n    \"\"\"");
    }

    [Test]
    public async Task RenderMultiLine()
    {
        var rendered = CsStringLiteral.RenderRaw("a\nb", "    ", "\n");
        await Assert.That(rendered).IsEqualTo("\"\"\"\n    a\n    b\n    \"\"\"");
    }

    [Test]
    public async Task RenderEmpty()
    {
        // A multi-line raw string requires at least one line of content (CS9002)
        var rendered = CsStringLiteral.RenderRaw("", "    ", "\n");
        await Assert.That(rendered).IsEqualTo("\"\"");
    }

    [Test]
    public async Task RenderBlankLineHasNoTrailingWhitespace()
    {
        var rendered = CsStringLiteral.RenderRaw("a\n\nb", "    ", "\n");
        await Assert.That(rendered).IsEqualTo("\"\"\"\n    a\n\n    b\n    \"\"\"");
    }

    [Test]
    public async Task RenderQuoteRunEscalatesDelimiter()
    {
        var rendered = CsStringLiteral.RenderRaw("has \"\"\" inside", "", "\n");
        await Assert.That(rendered).IsEqualTo("\"\"\"\"\nhas \"\"\" inside\n\"\"\"\"");
    }

    [Test]
    [Arguments(0x85)]
    [Arguments(0x2028)]
    [Arguments(0x2029)]
    public async Task RenderEscapesLineTerminator(int codePoint)
    {
        var terminator = (char) codePoint;
        var rendered = CsStringLiteral.Render($"x{terminator}y", "    ", "\n");

        // Carried as an escape. Left as itself it ends the literal, and the file stops compiling
        // (CS1010)
        await Assert.That(rendered).IsEqualTo($"\"x\\u{codePoint:x4}y\"");
        await Assert.That(rendered).DoesNotContain(terminator.ToString());
    }

    [Test]
    [Arguments(0x85)]
    [Arguments(0x2028)]
    [Arguments(0x2029)]
    public async Task RenderFallsBackToRegularWhenRawCannotHoldLineTerminator(int codePoint)
    {
        var terminator = (char) codePoint;
        var content = $"a\nb{terminator}c";
        var rendered = CsStringLiteral.Render(content, "    ", "\n");

        // Content over several lines is otherwise raw, but no delimiter width makes a raw string
        // able to hold this: the compiler reads the terminator as a line break, and the line it
        // starts does not carry the closing delimiter's indentation (CS8999)
        await Assert.That(rendered.StartsWith("\"\"\"", StringComparison.Ordinal)).IsFalse();
        await Assert.That(rendered).DoesNotContain(terminator.ToString());
        await Assert.That(CsStringLiteral.TryParse(rendered, out var value)).IsTrue();
        await Assert.That(value).IsEqualTo(content);
    }

    [Test]
    public async Task RenderRawFallsBackWhenContentHoldsLineTerminator()
    {
        // The fallback sits in RenderRaw rather than in Render, so asking for the raw form
        // directly still produces source that compiles
        var content = $"a\nb{lineSeparator}c";
        await Assert.That(CsStringLiteral.RenderRaw(content, "    ", "\n"))
            .IsEqualTo(StringLiteral.RenderRegular(content));
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
            var rendered = CsStringLiteral.RenderRaw(content, "    ", eol);
            await Assert.That(rendered).IsEqualTo(CsStringLiteral.RenderRaw(normalized, "    ", eol));

            var parsed = CsStringLiteral.TryParse(rendered, out var value);
            await Assert.That(parsed).IsTrue();
            await Assert.That(value).IsEqualTo(normalized);
        }
    }

    [Test]
    public async Task RenderCrlf()
    {
        var rendered = CsStringLiteral.RenderRaw("a\nb", "\t", "\r\n");
        await Assert.That(rendered).IsEqualTo("\"\"\"\r\n\ta\r\n\tb\r\n\t\"\"\"");
    }

    [Test]
    public async Task RenderRoundTrips()
    {
        foreach (var content in renderRoundTripCases)
        {
            foreach (var eol in new[] { "\n", "\r\n" })
            {
                foreach (var indent in new[] { "", "    ", "\t\t" })
                {
                    var rendered = CsStringLiteral.RenderRaw(content, indent, eol);
                    var parsed = CsStringLiteral.TryParse(rendered, out var value);
                    await Assert.That(parsed).IsTrue();
                    await Assert.That(value).IsEqualTo(content);
                }
            }
        }
    }

    // A single line snapshot says nothing a raw string can say, and costs three lines to say it
    [Test]
    [Arguments("abc", "\"abc\"")]
    [Arguments("", "\"\"")]
    [Arguments(" ", "\" \"")]
    [Arguments("has \"quotes\"", "\"has \\\"quotes\\\"\"")]
    [Arguments("back\\slash", "\"back\\\\slash\"")]
    [Arguments("tab\there", "\"tab\\there\"")]
    [Arguments("bell\a", "\"bell\\a\"")]
    [Arguments("esc\u001b", "\"esc\\u001b\"")]
    [Arguments("emoji 🎈 and unicode ☂", "\"emoji 🎈 and unicode ☂\"")]
    [Arguments("$ {value} {{x}}", "\"$ {value} {{x}}\"")]
    public async Task RenderSingleLineIsRegular(string content, string expected)
    {
        var rendered = CsStringLiteral.Render(content, "    ", "\n");
        await Assert.That(rendered).IsEqualTo(expected);
    }

    [Test]
    [Arguments("a\nb")]
    [Arguments("a\rb")]
    [Arguments("a\r\nb")]
    [Arguments("\nabc")]
    [Arguments("abc\n")]
    public async Task RenderMultiLineIsRaw(string content)
    {
        var rendered = CsStringLiteral.Render(content, "    ", "\n");
        await Assert.That(rendered).IsEqualTo(CsStringLiteral.RenderRaw(content, "    ", "\n"));
    }

    [Test]
    public async Task RenderRoundTripsWhicheverFormItPicks()
    {
        foreach (var content in renderRoundTripCases)
        {
            foreach (var eol in new[] { "\n", "\r\n" })
            {
                foreach (var indent in new[] { "", "    ", "\t\t" })
                {
                    var rendered = CsStringLiteral.Render(content, indent, eol);
                    var parsed = CsStringLiteral.TryParse(rendered, out var value);
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
    [Arguments("@\"a\"\"b\"", "a\"b")]
    [Arguments("\"\"\"a\"b\"\"\"", "a\"b")]
    [Arguments("\"\"\"\"has \"\"\" inside\"\"\"\"", "has \"\"\" inside")]
    public async Task Parse(string expression, string expected)
    {
        var parsed = CsStringLiteral.TryParse(expression, out var value);
        await Assert.That(parsed).IsTrue();
        await Assert.That(value).IsEqualTo(expected);
    }

    // A verbatim literal opening on an escaped quote. The quote run is not a delimiter here, and
    // reading it as one rejected a literal the compiler accepts
    [Test]
    [Arguments("@\"\"\"x\"\"\"", "\"x\"")]
    [Arguments("@\"\"\"\"", "\"")]
    [Arguments("@\"\"\"a\"\"b\"\"\"", "\"a\"b\"")]
    public async Task ParseVerbatimOpeningOnQuote(string expression, string expected)
    {
        var parsed = CsStringLiteral.TryParse(expression, out var value);
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
        var parsed = CsStringLiteral.TryParse(expression, out _);
        await Assert.That(parsed).IsFalse();
    }

    [Test]
    public async Task ParseMultiLineVerbatim()
    {
        var parsed = CsStringLiteral.TryParse("@\"a\r\nb\"", out var value);
        await Assert.That(parsed).IsTrue();
        await Assert.That(value).IsEqualTo("a\nb");
    }

    [Test]
    public async Task ParseMultiLineRawStripsIndent()
    {
        var expression = "\"\"\"\n      a\n\n      b\n      \"\"\"";
        var parsed = CsStringLiteral.TryParse(expression, out var value);
        await Assert.That(parsed).IsTrue();
        await Assert.That(value).IsEqualTo("a\n\nb");
    }

    [Test]
    [Arguments("$\"interpolated\"")]
    [Arguments("$$\"\"\"raw interpolated\"\"\"")]
    [Arguments("nameof(x)")]
    [Arguments("\"a\" + \"b\"")]
    [Arguments("\"unterminated")]
    [Arguments("identifier")]
    [Arguments("")]
    [Arguments("@$\"combined\"")]
    public async Task ParseRejects(string expression)
    {
        var parsed = CsStringLiteral.TryParse(expression, out _);
        await Assert.That(parsed).IsFalse();
    }

    [Test]
    public async Task ParseRejectsMalformedRawIndent()
    {
        // Content line is less indented than the closing delimiter
        var expression = "\"\"\"\n  a\n      \"\"\"";
        var parsed = CsStringLiteral.TryParse(expression, out _);
        await Assert.That(parsed).IsFalse();
    }

    // The indent is stripped by ordinal prefix, so a tab and the spaces it displays as
    // are not interchangeable, whichever side each is on
    [Test]
    [Arguments("\"\"\"\n\ta\n    \"\"\"")]
    [Arguments("\"\"\"\n    a\n\t\"\"\"")]
    [Arguments("\"\"\"\n\t    a\n    \t\"\"\"")]
    public async Task ParseRejectsMixedIndentCharacters(string expression)
    {
        var parsed = CsStringLiteral.TryParse(expression, out _);
        await Assert.That(parsed).IsFalse();
    }
}
