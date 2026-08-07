namespace DiffEngine;

/// <summary>
/// Renders snapshot text as a C# raw string literal, and parses C# string literal
/// expressions back to their runtime values.
/// </summary>
public static class CsStringLiteral
{
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
        var delimiter = new string('"', Math.Max(3, LongestQuoteRun(content) + 1));
        var builder = new StringBuilder();
        builder.Append(delimiter);
        builder.Append(eol);
        if (content.Length > 0)
        {
            foreach (var line in content.Split('\n'))
            {
                if (line.Length > 0)
                {
                    builder.Append(indent);
                    builder.Append(line);
                }

                builder.Append(eol);
            }
        }

        builder.Append(indent);
        builder.Append(delimiter);
        return builder.ToString();
    }

    static int LongestQuoteRun(string content)
    {
        var longest = 0;
        var current = 0;
        foreach (var ch in content)
        {
            if (ch == '"')
            {
                current++;
                if (current > longest)
                {
                    longest = current;
                }
            }
            else
            {
                current = 0;
            }
        }

        return longest;
    }

    /// <summary>
    /// Parses a C# string literal expression back to its runtime value.
    /// Supports raw ("""..."""), verbatim (@"...") and regular ("...") literals.
    /// Returns false for interpolated strings, concatenations, or any other expression.
    /// Newlines in the returned value are normalized to \n.
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

        // The scan must consume the whole expression (rejects "a" + "b" etc.)
        if (end != text.Length)
        {
            value = null;
            return false;
        }

        value = NormalizeNewlines(value!);
        return true;
    }

    internal static string NormalizeNewlines(string value) =>
        value
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');

    /// <summary>
    /// Scans one string literal starting at <paramref name="start"/> (which must point at the
    /// first character of the literal: '"' or '@'). On success <paramref name="end"/> is the
    /// index one past the closing quote. The value is NOT newline normalized.
    /// </summary>
    internal static bool TryScanLiteral(string text, int start, out string? value, out int end)
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

        var quotes = QuoteRunLength(text, index);
        if (quotes >= 3)
        {
            if (verbatim)
            {
                return false;
            }

            return TryScanRaw(text, index, quotes, out value, out end);
        }

        if (verbatim)
        {
            return TryScanVerbatim(text, index + 1, out value, out end);
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

    static bool TryScanRaw(string text, int start, int quotes, out string? value, out int end)
    {
        value = null;
        end = start;
        var contentStart = start + quotes;
        // Find the closing delimiter: a run of quotes with length >= quotes.
        // Content quote runs are shorter than the delimiter by the language rules.
        var index = contentStart;
        while (true)
        {
            if (index >= text.Length)
            {
                return false;
            }

            if (text[index] != '"')
            {
                index++;
                continue;
            }

            var run = QuoteRunLength(text, index);
            if (run >= quotes)
            {
                break;
            }

            index += run;
        }

        var contentEnd = index;
        end = index + quotes;
        var content = text.Substring(contentStart, contentEnd - contentStart);
        if (!content.Contains('\n'))
        {
            // Single line raw string: content is verbatim.
            value = content;
            return true;
        }

        // Multi line raw string:
        // * first line (after the opening quotes) must be whitespace only and is dropped
        // * the last line holds the closing quotes; its leading whitespace is the indent
        //   stripped from every content line, and the line itself is dropped
        var normalized = NormalizeNewlines(content);
        var lines = normalized.Split('\n');
        var first = lines[0];
        if (first.Trim().Length > 0)
        {
            return false;
        }

        var closeIndent = lines[^1];
        if (closeIndent.Trim().Length > 0)
        {
            return false;
        }

        var builder = new StringBuilder();
        for (var lineIndex = 1; lineIndex < lines.Length - 1; lineIndex++)
        {
            if (lineIndex > 1)
            {
                builder.Append('\n');
            }

            var line = lines[lineIndex];
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith(closeIndent, StringComparison.Ordinal))
            {
                builder.Append(line, closeIndent.Length, line.Length - closeIndent.Length);
                continue;
            }

            if (line.Trim().Length == 0)
            {
                // Whitespace-only line shorter than the indent
                continue;
            }

            // Malformed indentation
            return false;
        }

        value = builder.ToString();
        return true;
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

            if (ch == '\n' ||
                ch == '\r')
            {
                // Regular strings cannot span lines
                return false;
            }

            if (ch != '\\')
            {
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
                case '0':
                    builder.Append('\0');
                    break;
                case 'a':
                    builder.Append('\a');
                    break;
                case 'b':
                    builder.Append('\b');
                    break;
                case 'e':
                    builder.Append('');
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
                    if (!TryReadHex(text, ref index, 4, 4, out var utf16))
                    {
                        return false;
                    }

                    builder.Append((char)utf16);
                    break;
                case 'x':
                    if (!TryReadHex(text, ref index, 1, 4, out var variable))
                    {
                        return false;
                    }

                    builder.Append((char)variable);
                    break;
                case 'U':
                    if (!TryReadHex(text, ref index, 8, 8, out var codePoint))
                    {
                        return false;
                    }

                    if (codePoint > 0x10FFFF)
                    {
                        return false;
                    }

                    builder.Append(char.ConvertFromUtf32((int)codePoint));
                    break;
                default:
                    return false;
            }
        }

        return false;
    }

    static bool TryReadHex(string text, ref int index, int min, int max, out uint result)
    {
        result = 0;
        var count = 0;
        while (count < max &&
               index < text.Length &&
               Uri.IsHexDigit(text[index]))
        {
            result = (result << 4) + (uint)Uri.FromHex(text[index]);
            index++;
            count++;
        }

        return count >= min;
    }
}
