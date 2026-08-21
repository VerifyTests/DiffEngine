namespace DiffEngine;

/// <summary>
/// Renders snapshot text as a C# raw string literal, and parses C# string literal
/// expressions back to their runtime values.
/// <para>
/// The shapes it writes, and most of the reading, are shared with <see cref="FsStringLiteral"/>
/// through <see cref="StringLiteral"/>. What is C#'s own is here: a delimiter that widens to hold
/// any content, and the escapes a regular literal can carry.
/// </para>
/// </summary>
public static class CsStringLiteral
{
    /// <summary>
    /// Renders <paramref name="content"/> (\n newlines) as a C# string literal expression: a
    /// regular literal when it is a single line, since a raw string spends three lines and an
    /// indentation rule to say the same thing, and a multi-line raw literal otherwise - except
    /// where the raw form cannot hold the content at all, which <see cref="RenderRaw"/> answers.
    /// </summary>
    /// <param name="content">Snapshot text with \n newlines.</param>
    /// <param name="indent">Whitespace prefix for content lines and the closing delimiter.</param>
    /// <param name="eol">The target file's line ending ("\r\n" or "\n").</param>
    public static string Render(string content, string indent, string eol) =>
        content.IndexOf('\n') == -1 &&
        content.IndexOf('\r') == -1
            ? StringLiteral.RenderRegular(content)
            : RenderRaw(content, indent, eol);

    /// <summary>
    /// Renders <paramref name="content"/> (\n newlines) as a multi-line raw string literal.
    /// The returned text starts with the opening quotes (no leading indent on the first line)
    /// and ends with the closing quotes (no trailing newline).
    /// </summary>
    /// <param name="content">Snapshot text with \n newlines.</param>
    /// <param name="indent">Whitespace prefix for content lines and the closing delimiter.</param>
    /// <param name="eol">The target file's line ending ("\r\n" or "\n").</param>
    public static string RenderRaw(string content, string indent, string eol)
    {
        if (content.Length == 0)
        {
            // A multi-line raw string requires at least one line of content (CS9002),
            // so empty content cannot use the raw form
            return "\"\"";
        }

        if (StringLiteral.HasLineTerminator(content))
        {
            // No delimiter can hold one of these, however wide: the compiler reads it as a line
            // break, and the line it starts does not carry the closing delimiter's indentation
            // (CS8999). Only a regular literal can say it, as an escape
            return StringLiteral.RenderRegular(content);
        }

        // Three quotes, or one more than the longest run in the content, which is the widening
        // F# does not have and the reason a quote run sends it to a regular literal where C# stays
        // raw
        var delimiter = new string('"', Math.Max(3, StringLiteral.LongestQuoteRun(content) + 1));
        return StringLiteral.RenderMultiLine(content, indent, eol, delimiter);
    }

    /// <summary>
    /// Parses a C# string literal expression back to its runtime value.
    /// Supports raw ("""..."""), verbatim (@"...") and regular ("...") literals.
    /// Returns false for interpolated strings, concatenations, or any other expression.
    /// Newlines in the returned value are normalized to \n.
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
            // Asked before the quote run is measured, because there is no verbatim raw form: a
            // run of quotes after @" is an escaped quote and the start of the content, not a
            // delimiter. Measuring first read @"""x""" as a raw string that happened to carry an
            // @, and rejected a literal C# is perfectly happy with
            return StringLiteral.TryScanVerbatim(text, index + 1, out value, out end);
        }

        var quotes = StringLiteral.QuoteRunLength(text, index);
        if (quotes >= 3)
        {
            return StringLiteral.TryScanMultiLine(text, index, quotes, true, out value, out end);
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

    // A regular literal cannot span lines
    static bool TryScanRegular(string text, int start, out string? value, out int end) =>
        StringLiteral.TryScanRegular(text, start, true, TryEscape, out value, out end);

    /// <summary>
    /// The escapes C# has that F# does not, plus <c>\x</c>, which both have and size differently:
    /// one to four hex digits here, exactly two there.
    /// </summary>
    static bool TryEscape(string text, ref int index, char escape, StringBuilder builder)
    {
        switch (escape)
        {
            case '0':
                builder.Append('\0');
                return true;
            case 'e':
                builder.Append('\u001b');
                return true;
            case 'x':
                if (!StringLiteral.TryReadHex(text, ref index, 1, 4, out var value))
                {
                    return false;
                }

                builder.Append((char) value);
                return true;
            default:
                return false;
        }
    }
}