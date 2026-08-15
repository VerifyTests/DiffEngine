namespace DiffEngine;

/// <summary>
/// Renders snapshot text as an F# string literal, and parses F# string literal expressions back to
/// their runtime values.
/// <para>
/// The peer of <see cref="CsStringLiteral"/>, and its indent means something else entirely. F# has
/// no raw string: a triple-quoted one is verbatim from the character after the opening delimiter
/// to the one before the closing delimiter, with no first line dropped and no common indentation
/// stripped, so an indent written into it would be snapshot content. Multi-line content is
/// therefore written hard against the left margin, and the indent is a floor the result has to
/// clear rather than a prefix to add.
/// </para>
/// </summary>
public static class FsStringLiteral
{
    /// <summary>
    /// Renders <paramref name="content"/> (\n newlines) as an F# string literal expression: a
    /// verbatim multi-line one where the layout allows it, and a regular literal - one source
    /// line, newlines escaped - everywhere else.
    /// </summary>
    /// <param name="content">Snapshot text with \n newlines.</param>
    /// <param name="indent">
    /// The indentation the literal is being spliced at. Not a prefix: the column that whatever
    /// follows the literal has to reach. See <see cref="ClearsOffsideLine"/>.
    /// </param>
    /// <param name="eol">The target file's line ending ("\r\n" or "\n").</param>
    public static string Render(string content, string indent, string eol)
    {
        if (content.IndexOf('\n') == -1 &&
            content.IndexOf('\r') == -1)
        {
            return RenderRegular(content);
        }

        var multiLine = RenderMultiLine(content, eol);
        if (ClearsOffsideLine(multiLine, indent))
        {
            return multiLine;
        }

        return RenderContinued(content, indent, eol);
    }

    /// <summary>
    /// Whether a multi-line literal can be spliced at <paramref name="indent"/> without breaking
    /// F#'s offside rule.
    /// <para>
    /// The content is verbatim, so its last line decides where the closing delimiter sits, and
    /// therefore where the closing paren and any chained call after it sit. F# requires those to
    /// be at or right of the column the statement started in, and a snapshot whose last line is
    /// short - anything ending in a newline, for a start - puts them left of it. That is not a
    /// formatting complaint: the file no longer compiles.
    /// </para>
    /// <para>
    /// <paramref name="indent"/> is the splice site's indentation, which is at or right of the
    /// statement's own, so measuring against it is the conservative side of the rule. Measured in
    /// characters because F# rejects tabs in source outright (FS1161), which makes a column count
    /// and a character count the same number.
    /// </para>
    /// </summary>
    public static bool ClearsOffsideLine(string rendered, string indent) =>
        rendered.Length - (rendered.LastIndexOf('\n') + 1) >= indent.Length;

    /// <summary>
    /// Renders <paramref name="content"/> (\n newlines) as a regular literal spread over one
    /// source line per snapshot line, each break carrying an escaped newline and a continuation.
    /// <para>
    /// The form for content the verbatim one cannot hold at this indentation. A backslash before
    /// a line break drops the break and the indentation after it, so the literal reads a line at
    /// a time while its value stays exactly the content - and, since every line is indented, it
    /// has no way to break the layout it sits in. The cost is escaping: quotes, backslashes and
    /// control characters, and a leading space per line, which the continuation would otherwise
    /// eat along with the indentation it cannot tell that space from.
    /// </para>
    /// </summary>
    /// <param name="content">Snapshot text with \n newlines.</param>
    /// <param name="indent">Whitespace prefix for the continued lines. Layout only: F# drops it.</param>
    /// <param name="eol">The target file's line ending ("\r\n" or "\n").</param>
    public static string RenderContinued(string content, string indent, string eol)
    {
        var lines = SourceLanguage.NormalizeNewlines(content).Split('\n');
        var sourceLines = new List<string>(lines.Length);
        for (var index = 0; index < lines.Length; index++)
        {
            var builder = new StringBuilder();
            // The first line follows the opening quote, so nothing has been dropped in front of it
            AppendEscaped(builder, lines[index], escapeLeadingSpace: index > 0);
            if (index < lines.Length - 1)
            {
                builder.Append("\\n");
            }

            sourceLines.Add(builder.ToString());
        }

        // Content ending in a newline leaves an empty last line, and its break is already on the
        // line above. Continuing to it would put the closing quote alone on a line of its own
        if (sourceLines[sourceLines.Count - 1].Length == 0)
        {
            sourceLines.RemoveAt(sourceLines.Count - 1);
        }

        return $"\"{string.Join($"\\{eol}{indent}", sourceLines)}\"";
    }

    /// <summary>
    /// Renders content as a regular literal on one source line, escaping what the form cannot hold
    /// verbatim - newlines included.
    /// </summary>
    static string RenderRegular(string content)
    {
        var builder = new StringBuilder(content.Length + 2);
        builder.Append('"');
        AppendEscaped(builder, content, escapeLeadingSpace: false);
        builder.Append('"');
        return builder.ToString();
    }

    static void AppendEscaped(StringBuilder builder, string content, bool escapeLeadingSpace)
    {
        if (escapeLeadingSpace &&
            content.Length > 0 &&
            content[0] == ' ')
        {
            // Written as an escape so the continuation's whitespace skipping stops on it. Only the
            // first one has to be: the skip ends at the backslash, and every space after it is
            // content. \x takes exactly two digits in F#, so nothing that follows extends it
            builder.Append("\\x20");
            content = content.Substring(1);
        }

        foreach (var ch in content)
        {
            switch (ch)
            {
                case '\\':
                    builder.Append("\\\\");
                    continue;
                case '"':
                    builder.Append("\\\"");
                    continue;
                case '\n':
                    builder.Append("\\n");
                    continue;
                case '\r':
                    builder.Append("\\r");
                    continue;
                case '\a':
                    builder.Append("\\a");
                    continue;
                case '\b':
                    builder.Append("\\b");
                    continue;
                case '\f':
                    builder.Append("\\f");
                    continue;
                case '\t':
                    builder.Append("\\t");
                    continue;
                case '\v':
                    builder.Append("\\v");
                    continue;
            }

            // Everything else a literal cannot carry as itself. F# has no \0 or \e escape, so the
            // \u form covers every remaining control character rather than only the exotic ones
            if (ch < ' ' || ch == '\u007f')
            {
                builder.Append("\\u");
                builder.Append(((int) ch).ToString("x4"));
                continue;
            }

            builder.Append(ch);
        }
    }

    /// <summary>
    /// Renders <paramref name="content"/> (\n newlines) as a multi-line literal. Triple-quoted
    /// where it can be, since that form escapes nothing at all and so carries a snapshot as it
    /// reads; verbatim where the content would collide with the delimiter.
    /// <para>
    /// The content lines are written as they are, with no indentation added: F# takes them
    /// verbatim, so an indent would be snapshot content rather than layout.
    /// </para>
    /// </summary>
    /// <param name="content">Snapshot text with \n newlines.</param>
    /// <param name="eol">The target file's line ending ("\r\n" or "\n").</param>
    public static string RenderMultiLine(string content, string eol)
    {
        if (content.Length == 0)
        {
            return "\"\"";
        }

        if (content.IndexOf('\r') != -1)
        {
            // Content is meant to arrive \n normalized. Be defensive: a stray \r would
            // otherwise be emitted into the literal as content, corrupting the snapshot
            content = SourceLanguage.NormalizeNewlines(content);
        }

        var body = content.Replace("\n", eol);
        if (CanTripleQuote(content))
        {
            return $"\"\"\"{body}\"\"\"";
        }

        return $"@\"{body.Replace("\"", "\"\"")}\"";
    }

    /// <summary>
    /// A triple-quoted literal has no way to escape its own delimiter and no way to widen it, so
    /// content that runs into the delimiter has to take the verbatim form instead. A quote at
    /// either end counts: it would sit against the delimiter and be read as part of it.
    /// </summary>
    static bool CanTripleQuote(string content) =>
        content[0] != '"' &&
        content[content.Length - 1] != '"' &&
        content.IndexOf("\"\"\"", StringComparison.Ordinal) == -1;

    /// <summary>
    /// Parses an F# string literal expression back to its runtime value.
    /// Supports triple-quoted ("""..."""), verbatim (@"...") and regular ("...") literals.
    /// Returns false for interpolated strings, byte strings, concatenations, or any other
    /// expression. Newlines in the returned value are normalized to \n.
    /// </summary>
    public static bool TryParse(string expression, [NotNullWhen(true)] out string? value)
    {
        value = null;
        var text = expression.Trim();
        if (text.Length == 0)
        {
            return false;
        }

        if (!TryScanLiteral(text, 0, out value, out var end))
        {
            return false;
        }

        // The scan must consume the whole expression (rejects "a" + "b", "abc"B etc.)
        if (end != text.Length)
        {
            value = null;
            return false;
        }

        value = SourceLanguage.NormalizeNewlines(value!);
        return true;
    }

    /// <summary>
    /// Scans one string literal starting at <paramref name="start"/> (which must point at the
    /// first character of the literal: '"' or '@'). On success <paramref name="end"/> is the
    /// index one past the closing quote. The value is NOT newline normalized.
    /// </summary>
    static bool TryScanLiteral(string text, int start, out string? value, out int end)
    {
        value = null;
        end = start;
        if (start >= text.Length)
        {
            return false;
        }

        var index = start;
        var verbatim = false;
        if (text[index] == '@')
        {
            verbatim = true;
            index++;
        }

        if (index >= text.Length || text[index] != '"')
        {
            // Interpolated ($) and everything else is unsupported.
            return false;
        }

        if (verbatim)
        {
            // There is no verbatim triple-quoted form, so a run of quotes after @" is an escaped
            // quote and the rest of the string, not a delimiter
            return TryScanVerbatim(text, index + 1, out value, out end);
        }

        var quotes = QuoteRunLength(text, index);
        if (quotes >= 3)
        {
            return TryScanTripleQuoted(text, index, out value, out end);
        }

        if (quotes == 2)
        {
            // Empty regular string ""
            value = "";
            end = index + 2;
            return true;
        }

        return TryScanRegular(text, index + 1, out value, out end);
    }

    static int QuoteRunLength(string text, int index)
    {
        var count = 0;
        while (index + count < text.Length &&
               text[index + count] == '"')
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Content is whatever sits between the delimiters, exactly. Unlike a C# raw string there is
    /// no first line to drop and no indent to strip.
    /// </summary>
    static bool TryScanTripleQuoted(string text, int start, out string? value, out int end)
    {
        value = null;
        end = start;
        var contentStart = start + 3;
        var index = contentStart;
        while (index < text.Length)
        {
            if (text[index] == '"' &&
                QuoteRunLength(text, index) >= 3)
            {
                value = text.Substring(contentStart, index - contentStart);
                end = index + 3;
                return true;
            }

            index++;
        }

        return false;
    }

    static bool TryScanVerbatim(string text, int start, out string? value, out int end)
    {
        value = null;
        end = start;
        var builder = new StringBuilder();
        var index = start;
        while (index < text.Length)
        {
            var ch = text[index];
            if (ch == '"')
            {
                if (index + 1 < text.Length &&
                    text[index + 1] == '"')
                {
                    builder.Append('"');
                    index += 2;
                    continue;
                }

                value = builder.ToString();
                end = index + 1;
                return true;
            }

            builder.Append(ch);
            index++;
        }

        return false;
    }

    static bool TryScanRegular(string text, int start, out string? value, out int end)
    {
        value = null;
        end = start;
        var builder = new StringBuilder();
        var index = start;
        while (index < text.Length)
        {
            var ch = text[index];
            if (ch == '"')
            {
                value = builder.ToString();
                end = index + 1;
                return true;
            }

            if (ch != '\\')
            {
                // An ordinary F# string may span lines, so a newline here is content
                builder.Append(ch);
                index++;
                continue;
            }

            index++;
            if (index >= text.Length)
            {
                return false;
            }

            var escape = text[index];
            index++;
            switch (escape)
            {
                case '\\':
                    builder.Append('\\');
                    break;
                case '"':
                    builder.Append('"');
                    break;
                case '\'':
                    builder.Append('\'');
                    break;
                case 'a':
                    builder.Append('\a');
                    break;
                case 'b':
                    builder.Append('\b');
                    break;
                case 'f':
                    builder.Append('\f');
                    break;
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'v':
                    builder.Append('\v');
                    break;
                case 'u':
                    if (!TryReadHex(text, ref index, 4, out var utf16))
                    {
                        return false;
                    }

                    builder.Append((char) utf16);
                    break;
                case 'x':
                    if (!TryReadHex(text, ref index, 2, out var byteValue))
                    {
                        return false;
                    }

                    builder.Append((char) byteValue);
                    break;
                case 'U':
                    if (!TryReadHex(text, ref index, 8, out var codePoint))
                    {
                        return false;
                    }

                    if (codePoint > 0x10FFFF)
                    {
                        return false;
                    }

                    builder.Append(char.ConvertFromUtf32((int) codePoint));
                    break;
                case '\r':
                case '\n':
                    // Line continuation: the newline and the indentation that follows it are
                    // layout, not content
                    if (escape == '\r' &&
                        index < text.Length &&
                        text[index] == '\n')
                    {
                        index++;
                    }

                    while (index < text.Length &&
                           (text[index] == ' ' || text[index] == '\t'))
                    {
                        index++;
                    }

                    break;
                default:
                    // Trigraph: \DDD, three decimal digits
                    if (!char.IsDigit(escape))
                    {
                        return false;
                    }

                    if (!TryReadTrigraph(text, ref index, escape, out var trigraph))
                    {
                        return false;
                    }

                    builder.Append(trigraph);
                    break;
            }
        }

        return false;
    }

    static bool TryReadHex(string text, ref int index, int count, out uint result)
    {
        result = 0;
        var read = 0;
        while (read < count &&
               index < text.Length &&
               Uri.IsHexDigit(text[index]))
        {
            result = (result << 4) + (uint) Uri.FromHex(text[index]);
            index++;
            read++;
        }

        return read == count;
    }

    static bool TryReadTrigraph(string text, ref int index, char first, out char result)
    {
        result = '\0';
        if (index + 1 >= text.Length ||
            !char.IsDigit(text[index]) ||
            !char.IsDigit(text[index + 1]))
        {
            return false;
        }

        var value = (first - '0') * 100 + (text[index] - '0') * 10 + (text[index + 1] - '0');
        index += 2;
        if (value > 255)
        {
            return false;
        }

        result = (char) value;
        return true;
    }
}
