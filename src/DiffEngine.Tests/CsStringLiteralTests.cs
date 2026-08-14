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
        "line1\n    indented\nline3"
    ];

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
}
