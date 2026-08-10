/// <summary>
/// A one pass lexical map of a C# file: where the comments, strings and char literals are, and
/// therefore which offsets are code.
/// <para>
/// The patcher finds its call sites by scanning text, and a text scan that cannot see a comment
/// or a string patches a commented out example, or the middle of another test's snapshot content,
/// as readily as the real call. Lexing once and asking the map is cheaper than lexing per search,
/// and it is one implementation: every search agrees on what a string is because there is only
/// one answer to ask.
/// </para>
/// </summary>
sealed class CsScan
{
    readonly string source;
    readonly bool[] code;

    /// <summary>
    /// Start of a comment or literal to the offset just past it.
    /// </summary>
    readonly Dictionary<int, int> skips = new();

    /// <summary>
    /// The same spans keyed the other way round, for a scan working backwards. Ends are unique
    /// because the spans cannot overlap.
    /// </summary>
    readonly Dictionary<int, int> skipEnds = new();

    public CsScan(string source)
    {
        this.source = source;
        code = new bool[source.Length];
        var index = 0;
        while (index < source.Length)
        {
            var start = index;
            switch (source[index])
            {
                case '/':
                    if (TrySkipComment(source, ref index))
                    {
                        AddSkip(start, index);
                        continue;
                    }

                    break;
                case '\'':
                    if (TrySkipCharLiteral(source, ref index))
                    {
                        AddSkip(start, index);
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

                        AddSkip(start, index);
                        continue;
                    }

                    break;
            }

            code[index] = true;
            index++;
        }
    }

    void AddSkip(int start, int end)
    {
        skips.Add(start, end);
        skipEnds[end] = start;
    }

    /// <summary>
    /// True when the offset is outside every comment, string and char literal.
    /// </summary>
    public bool IsCode(int index) =>
        index >= 0 &&
        index < code.Length &&
        code[index];

    /// <summary>
    /// When a comment or literal starts at <paramref name="index"/>, <paramref name="end"/> is the
    /// offset just past it. Lets a structural scan step over trivia without lexing it again.
    /// </summary>
    public bool TryGetSkip(int index, out int end) =>
        skips.TryGetValue(index, out end);

    /// <summary>
    /// True when a comment ends at <paramref name="end"/>, with <paramref name="start"/> set to
    /// where it began. Only comments: a literal is content, and trimming one off a span would be
    /// trimming off the value.
    /// </summary>
    public bool TryGetCommentEndingAt(int end, out int start) =>
        skipEnds.TryGetValue(end, out start) &&
        source[start] == '/';

    /// <summary>
    /// Advances past whitespace and comments.
    /// </summary>
    public void SkipTrivia(ref int index)
    {
        while (index < source.Length)
        {
            if (char.IsWhiteSpace(source[index]))
            {
                index++;
                continue;
            }

            if (source[index] == '/' &&
                skips.TryGetValue(index, out var end))
            {
                index = end;
                continue;
            }

            return;
        }
    }

    /// <summary>
    /// True when the identifier at <paramref name="nameStart"/> is being declared rather than
    /// called. The two are otherwise identical - name, parens, body - so the tell is the token in
    /// front: a declaration is preceded by its return type, a call by a dot, an operator, or one
    /// of the keywords that can introduce an expression.
    /// </summary>
    public bool IsDeclaration(int nameStart)
    {
        var index = PreviousSignificant(nameStart);
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

        var start = index;
        while (start > 0 &&
               IsIdentifierChar(source[start - 1]))
        {
            start--;
        }

        return !callablePredecessors.Contains(source.Substring(start, index - start + 1));
    }

    /// <summary>
    /// The keywords that can sit immediately before a call. Anything else that reads as an
    /// identifier there is a return type or a modifier, which makes what follows a declaration.
    /// </summary>
    static readonly HashSet<string> callablePredecessors = new(StringComparer.Ordinal)
    {
        "and", "as", "await", "by", "case", "catch", "checked", "default", "do", "else",
        "equals", "fixed", "foreach", "from", "goto", "group", "if", "in", "into", "is",
        "join", "let", "lock", "new", "not", "on", "or", "orderby", "out", "params", "ref",
        "return", "select", "stackalloc", "switch", "throw", "unchecked", "using", "when",
        "where", "while", "with", "yield"
    };

    /// <summary>
    /// The offset of the last character before <paramref name="index"/> that is neither
    /// whitespace nor inside a comment, or -1 when there is none.
    /// </summary>
    int PreviousSignificant(int index)
    {
        index--;
        while (index >= 0 &&
               (char.IsWhiteSpace(source[index]) || !code[index]))
        {
            index--;
        }

        return index;
    }

    /// <summary>
    /// Advances past a type argument list, so Foo&lt;Bar&gt;(...) is located as readily as
    /// Foo(...). Only the characters a type argument list can hold are accepted, and the caller
    /// still has to find a '(' after it, so a comparison cannot be mistaken for one.
    /// </summary>
    public static bool TrySkipTypeArguments(string source, ref int index)
    {
        var cursor = index;
        if (cursor >= source.Length ||
            source[cursor] != '<')
        {
            return false;
        }

        cursor++;
        var depth = 1;
        while (cursor < source.Length)
        {
            var ch = source[cursor];
            if (ch == '<')
            {
                depth++;
                cursor++;
                continue;
            }

            if (ch == '>')
            {
                depth--;
                cursor++;
                if (depth == 0)
                {
                    index = cursor;
                    return true;
                }

                continue;
            }

            if (IsIdentifierChar(ch) ||
                ch is ',' or '.' or '?' or '[' or ']' or ':' or ' ' or '\t')
            {
                cursor++;
                continue;
            }

            return false;
        }

        return false;
    }

    public static bool IsIdentifierChar(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_';

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
            // Raw string (interpolated or not): skip blindly to a closing run of >= quotes.
            // Interpolation holes are skipped as part of the content.
            var search = cursor + quotes;
            while (true)
            {
                if (search >= source.Length)
                {
                    index = source.Length;
                    return true;
                }

                if (source[search] != '"')
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
