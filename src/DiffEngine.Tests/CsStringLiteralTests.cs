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
