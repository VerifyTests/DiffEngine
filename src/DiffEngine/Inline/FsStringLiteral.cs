namespace DiffEngine;

/// <summary>
/// Renders snapshot text as an F# string literal, and reads one back as the snapshot it holds.
/// <para>
/// The peer of <see cref="CsStringLiteral"/>, and it writes the same shapes: one line for one
/// line, and a triple-quoted literal with its content indented under the call otherwise. The
/// difference is who takes the layout off. C# has raw strings, so its compiler drops the first
/// line and the closing delimiter's indentation and hands the caller the snapshot. F# has no such
/// form: a triple-quoted literal is verbatim, so what F# hands over still carries the line break
/// after the opening delimiter and the indentation of every line.
/// </para>
/// <para>
/// So that trimming is a convention between whoever writes the literal and whoever reads it, and
/// this class is both ends of it: <see cref="Render"/> writes the shape and
/// <see cref="SourceLanguage.SnapshotValue"/> takes it back off. A test library comparing an F#
/// expected argument must go through that, or every F# snapshot differs from itself by an indent
/// and never passes. The alternative was writing snapshots at the left margin, which F#'s offside
/// rule then rejects for anything ending in a newline.
/// </para>
/// </summary>
public static class FsStringLiteral
{
    /// <summary>
    /// Renders <paramref name="content"/> (\n newlines) as an F# string literal expression: a
    /// regular literal when it is a single line, and an indented triple-quoted one otherwise.
    /// </summary>
    /// <param name="content">Snapshot text with \n newlines.</param>
    /// <param name="indent">Whitespace prefix for content lines and the closing delimiter.</param>
    /// <param name="eol">The target file's line ending ("\r\n" or "\n").</param>
    public static string Render(string content, string indent, string eol)
    {
        if (content.IndexOf('\n') == -1 &&
            content.IndexOf('\r') == -1)
        {
            return StringLiteral.RenderRegular(content);
        }

        if (!CanTripleQuote(content))
        {
            // Content the multi-line form cannot hold: a quote run, which F# cannot widen a
            // delimiter past the way C# can (FS1232), or a line terminator, which no delimiter
            // helps with. A regular literal on one source line always works, whatever it costs in
            // escapes
            return StringLiteral.RenderRegular(SourceLanguage.NormalizeNewlines(content));
        }

        return StringLiteral.RenderMultiLine(content, indent, eol, "\"\"\"");
    }

    /// <summary>
    /// Whether a triple-quoted literal can hold this content. A quote at either end would sit
    /// against the delimiter and be read as part of it, and a run of three anywhere would close
    /// the literal early.
    /// <para>
    /// A line terminator rules it out as well, for the reason
    /// <see cref="StringLiteral.IsLineTerminator" /> gives. F# is stricter here than it has to be:
    /// what sends C# to a regular literal is its own lexer reading one as a line break, and
    /// nothing says F# does. It keeps the two languages writing the one shape, which is what
    /// everything downstream of a render is written against.
    /// </para>
    /// </summary>
    static bool CanTripleQuote(string content) =>
        content[0] != '"' &&
        content[^1] != '"' &&
        content.IndexOf("\"\"\"", StringComparison.Ordinal) == -1 &&
        !StringLiteral.HasLineTerminator(content);

    /// <summary>
    /// The snapshot a triple-quoted literal was written to hold: the value F# produced for it,
    /// with the line break after the opening delimiter and the closing delimiter's indentation
    /// taken back off.
    /// <para>
    /// Applied to a value rather than to source text, because F# does not implement
    /// <see cref="CallerArgumentExpressionAttribute"/> and a test library never sees the literal
    /// it was handed. A value not in that shape is returned unchanged: a single line snapshot, a
    /// literal written some other way, or a snapshot that genuinely looks like layout and is
    /// therefore not one this ever wrote.
    /// </para>
    /// </summary>
    public static string StripLayout(string value) =>
        StringLiteral.TryStripLayout(SourceLanguage.NormalizeNewlines(value), out var stripped)
            ? stripped
            : value;

    /// <summary>
    /// Parses an F# string literal expression back to the snapshot it holds: triple-quoted
    /// ("""..."""), with the layout taken off when it is written to that convention and
    /// verbatim when it is not; verbatim (@"...") and regular ("..."), which carry their value as
    /// it is. Returns false for interpolated strings, byte strings, concatenations, or any other
    /// expression. Newlines in the returned value are normalized to \n.
    /// </summary>
    public static bool TryParse(string expression, [NotNullWhen(true)] out string? value) =>
        StringLiteral.TryParse(expression, TryScanLiteral, out value);
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
            return StringLiteral.TryScanVerbatim(text, index + 1, out value, out end);
        }

        var quotes = StringLiteral.QuoteRunLength(text, index);
        if (quotes >= 3)
        {
            // F# reads the closing delimiter as exactly three quotes, so a longer run is content
            // it cannot hold and never something this wrote
            return StringLiteral.TryScanMultiLine(text, index, 3, false, out value, out end);
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

    // An ordinary F# string may span lines, so a newline in one is content
    static bool TryScanRegular(string text, int start, out string? value, out int end) =>
        StringLiteral.TryScanRegular(text, start, false, TryEscape, out value, out end);

    /// <summary>
    /// What F# spells its own way: <c>\x</c> is exactly two hex digits, a backslash before a line
    /// break continues the string, and a backslash before three digits is a trigraph. F# has no
    /// <c>\0</c> or <c>\e</c>, so those fall through to being no escape at all.
    /// </summary>
    static bool TryEscape(string text, ref int index, char escape, StringBuilder builder)
    {
        switch (escape)
        {
            case 'x':
                if (!StringLiteral.TryReadHex(text, ref index, 2, 2, out var byteValue))
                {
                    return false;
                }

                builder.Append((char) byteValue);
                return true;
            case '\r':
            case '\n':
                // Line continuation: the newline and the indentation that follows it are layout,
                // not content
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

                return true;
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
                return true;
        }
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
