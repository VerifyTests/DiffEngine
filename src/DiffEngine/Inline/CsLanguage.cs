/// <summary>
/// C#: the lexing that fills a <see cref="SourceScan"/>, and the syntax the patcher has to write.
/// </summary>
sealed class CsLanguage : SourceLanguage
{
    public override string Render(string content, string indent, string eol) =>
        CsStringLiteral.Render(content, indent, eol);

    /// <summary>
    /// The compiler already did it: a raw string arrives with its first line and its closing
    /// indentation gone.
    /// </summary>
    public override string SnapshotValue(string literalValue) => literalValue;

    public override bool TryParse(string expression, [NotNullWhen(true)] out string? value) =>
        CsStringLiteral.TryParse(expression, out value);

    internal override string NamePrefix(string name) => $"{name}: ";

    internal override char NameSeparator => ':';

    internal override bool IsIdentifierChar(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_';

    internal override SourceScan Scan(string source)
    {
        var scan = new SourceScan(this, source);
        var index = 0;
        while (index < source.Length)
        {
            var start = index;
            switch (source[index])
            {
                case '/':
                    if (TrySkipComment(source, ref index))
                    {
                        scan.AddSkip(start, index, comment: true);
                        continue;
                    }

                    break;
                case '\'':
                    if (TrySkipCharLiteral(source, ref index))
                    {
                        scan.AddSkip(start, index, comment: false);
                        continue;
                    }

                    // Unterminated: take the quote as code rather than swallowing the rest of
                    // the file over one stray character
                    index = start;
                    break;
                case '"':
                case '@':
                case '$':
                    if (TrySkipStringLike(source, ref index))
                    {
                        // A suffix (u8) is part of the literal token, so a search for "x" cannot
                        // match "x"u8 and splice over only the quoted part
                        while (index < source.Length &&
                               IsIdentifierChar(source[index]))
                        {
                            index++;
                        }

                        scan.AddSkip(start, index, comment: false);
                        continue;
                    }

                    break;
            }

            scan.MarkCode(index);
            index++;
        }

        return scan;
    }

    /// <summary>
    /// A declaration is preceded by its return type, a call by a dot, an operator, or one of the
    /// keywords that can introduce an expression.
    /// </summary>
    internal override bool IsDeclaration(SourceScan scan, int nameStart)
    {
        var source = scan.Source;
        var index = scan.PreviousSignificant(nameStart);
        if (index < 0)
        {
            return false;
        }

        var ch = source[index];
        if (ch == '>')
        {
            // Close of a generic return type (Task<int> Name), unless it is a lambda arrow
            return index == 0 ||
                   source[index - 1] != '=';
        }

        if (!IsIdentifierChar(ch))
        {
            return false;
        }

        return !callablePredecessors.Contains(scan.WordEndingAt(index));
    }

    /// <summary>
    /// The keywords that can sit immediately before a call. Anything else that reads as an
    /// identifier there is a return type or a modifier, which makes what follows a declaration.
    /// </summary>
    static readonly HashSet<string> callablePredecessors =
    [
        with(StringComparer.Ordinal),
        "and", "as", "await", "by", "case", "catch", "checked", "default", "do", "else",
        "equals", "fixed", "foreach", "from", "goto", "group", "if", "in", "into", "is",
        "join", "let", "lock", "new", "not", "on", "or", "orderby", "out", "params", "ref",
        "return", "select", "stackalloc", "switch", "throw", "unchecked", "using", "when",
        "where", "while", "with", "yield"
    ];

    static bool TrySkipComment(string source, ref int index)
    {
        if (index + 1 >= source.Length)
        {
            return false;
        }

        var next = source[index + 1];
        if (next == '/')
        {
            var end = source.IndexOf('\n', index);
            index = end < 0 ? source.Length : end + 1;
            return true;
        }

        if (next == '*')
        {
            var end = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
            index = end < 0 ? source.Length : end + 2;
            return true;
        }

        return false;
    }

    static bool TrySkipCharLiteral(string source, ref int index)
    {
        // index at the opening quote
        index++;
        while (index < source.Length)
        {
            var ch = source[index];
            if (ch == '\\')
            {
                index += 2;
                continue;
            }

            if (ch == '\'')
            {
                index++;
                return true;
            }

            if (ch == '\n')
            {
                return false;
            }

            index++;
        }

        return false;
    }

    // index at '$', '@' or '"'. Returns false when the characters do not start a string literal
    // (eg '@identifier'); the caller then advances by one.
    static bool TrySkipStringLike(string source, ref int index)
    {
        var cursor = index;
        var dollars = 0;
        var verbatim = false;
        while (cursor < source.Length)
        {
            var ch = source[cursor];
            if (ch == '$')
            {
                dollars++;
                cursor++;
                continue;
            }

            if (ch == '@')
            {
                verbatim = true;
                cursor++;
                continue;
            }

            break;
        }

        if (cursor >= source.Length || source[cursor] != '"')
        {
            return false;
        }

        var quotes = QuoteRun(source, cursor);
        if (quotes >= 3)
        {
            // Raw string: scan to a closing run of >= quotes, stepping over any interpolation
            // hole whole. Skipping holes as content read the quotes of a literal inside one -
            // $"""{Render("""x""")}""" - as the end of the outer string, after which the rest of
            // the line lexed as code and a stray delimiter opened a string that could swallow a
            // real call
            var search = cursor + quotes;
            while (true)
            {
                if (search >= source.Length)
                {
                    index = source.Length;
                    return true;
                }

                var ch = source[search];
                if (dollars > 0 && ch == '{')
                {
                    // A run of fewer than one brace per dollar is content, which is how a raw
                    // interpolated string carries a literal brace
                    var braces = BraceRun(source, search);
                    if (braces < dollars)
                    {
                        search += braces;
                        continue;
                    }

                    if (!TrySkipHole(source, ref search))
                    {
                        index = source.Length;
                        return true;
                    }

                    continue;
                }

                if (ch != '"')
                {
                    search++;
                    continue;
                }

                var run = QuoteRun(source, search);
                if (run >= quotes)
                {
                    index = search + run;
                    return true;
                }

                search += run;
            }
        }

        cursor += quotes == 2 ? 2 : 1;
        if (quotes == 2)
        {
            // Empty string "" or interpolated empty string $""
            index = cursor;
            return true;
        }

        while (cursor < source.Length)
        {
            var ch = source[cursor];
            if (ch == '"')
            {
                if (verbatim &&
                    cursor + 1 < source.Length &&
                    source[cursor + 1] == '"')
                {
                    cursor += 2;
                    continue;
                }

                index = cursor + 1;
                return true;
            }

            if (!verbatim && ch == '\\')
            {
                cursor += 2;
                continue;
            }

            if (!verbatim && ch == '\n')
            {
                // Malformed: unterminated regular string. Stop at the line end.
                index = cursor;
                return true;
            }

            if (dollars > 0 && ch == '{')
            {
                if (cursor + 1 < source.Length && source[cursor + 1] == '{')
                {
                    cursor += 2;
                    continue;
                }

                if (!TrySkipHole(source, ref cursor))
                {
                    index = source.Length;
                    return true;
                }

                continue;
            }

            if (dollars > 0 && ch == '}' &&
                cursor + 1 < source.Length && source[cursor + 1] == '}')
            {
                cursor += 2;
                continue;
            }

            cursor++;
        }

        index = source.Length;
        return true;
    }

    // cursor at '{' of an interpolation hole; skips past the matching '}'
    static bool TrySkipHole(string source, ref int cursor)
    {
        var depth = 1;
        cursor++;
        while (cursor < source.Length)
        {
            var ch = source[cursor];
            switch (ch)
            {
                case '/':
                    if (!TrySkipComment(source, ref cursor))
                    {
                        cursor++;
                    }

                    continue;
                case '\'':
                    if (!TrySkipCharLiteral(source, ref cursor))
                    {
                        return false;
                    }

                    continue;
                case '"':
                case '@':
                case '$':
                    if (!TrySkipStringLike(source, ref cursor))
                    {
                        cursor++;
                    }

                    continue;
                case '{':
                    depth++;
                    cursor++;
                    continue;
                case '}':
                    depth--;
                    cursor++;
                    if (depth == 0)
                    {
                        return true;
                    }

                    continue;
                default:
                    cursor++;
                    continue;
            }
        }

        return false;
    }

    static int BraceRun(string source, int index)
    {
        var count = 0;
        while (index + count < source.Length &&
               source[index + count] == '{')
        {
            count++;
        }

        return count;
    }

    static int QuoteRun(string source, int index)
    {
        var count = 0;
        while (index + count < source.Length &&
               source[index + count] == '"')
        {
            count++;
        }

        return count;
    }
}
