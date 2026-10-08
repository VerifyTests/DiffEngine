namespace DiffEngine;

/// <summary>
/// F#: the lexing that fills a <see cref="SourceScan"/>, and the syntax the patcher has to write.
/// <para>
/// Three things differ from C# beyond the obvious. Comments are <c>(* *)</c> and they nest. A tick
/// is a char literal in one place and part of a name (<c>value'</c>) or a type parameter
/// (<c>'T</c>) in others, so it cannot simply open a literal. And a name is a declaration only
/// when a keyword says so - F# has no return type in front of a name to tell the two apart, so the
/// C# rule inverts here: assume a call, and let <c>let</c> or <c>member</c> say otherwise.
/// </para>
/// </summary>
sealed class FsLanguage : SourceLanguage
{
    public override string Render(string content, string indent, string eol) =>
        FsStringLiteral.Render(content, indent, eol);

    public override string SnapshotValue(string literalValue) =>
        FsStringLiteral.StripLayout(literalValue);

    public override bool TryParse(string expression, [NotNullWhen(true)] out string? value) =>
        FsStringLiteral.TryParse(expression, out value);

    internal override string NamePrefix(string name) => $"{name} = ";

    internal override char NameSeparator => '=';

    /// <summary>
    /// The offside rule. A token that starts a line carries on the expression above it only when
    /// it sits right of the column that expression started at; at that column it is read as the
    /// next statement, and left of it as the end of the block. And that column is where the
    /// expression's first token is, which is not the line's own indentation once something comes
    /// before it: <c>do!</c>, <c>let! x =</c>, <c>let x =</c>, or a binding written on one line.
    /// </summary>
    internal override bool IndentationIsSyntax => true;

    /// <summary>
    /// The F# compiler does not implement <see cref="CallerArgumentExpressionAttribute"/> - it
    /// warns FS0202 and leaves the parameter at its default - so an F# patch never carries the
    /// expression its C# equivalent is anchored to, and is located by line hint alone.
    /// </summary>
    internal override bool SuppliesArgumentExpressions => false;

    internal override bool IsIdentifierChar(char ch) =>
        IsNameChar(ch);

    static bool IsNameChar(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_' || ch == '\'';

    internal override bool IsTypeArgumentChar(char ch) =>
        base.IsTypeArgumentChar(ch) ||
        // Tuple types (Foo<int * string>) and statically resolved type parameters (^T)
        ch is '*' or '^';

    internal override void Lex(SourceScan scan, int from)
    {
        var source = scan.Source;
        var rejoinFrom = scan.RejoinFrom;
        var index = from;
        while (index < source.Length)
        {
            if (index >= rejoinFrom &&
                scan.TryRejoin(index))
            {
                return;
            }

            var start = index;
            switch (source[index])
            {
                case '/':
                    if (TrySkipLineComment(source, ref index))
                    {
                        scan.AddSkip(start, index, comment: true);
                        continue;
                    }

                    break;
                case '(':
                    if (TrySkipBlockComment(source, ref index))
                    {
                        scan.AddSkip(start, index, comment: true);
                        continue;
                    }

                    break;
                case '#':
                    if (TrySkipDirective(source, ref index))
                    {
                        scan.AddSkip(start, index, comment: true);
                        continue;
                    }

                    break;
                case '`':
                    if (TrySkipQuotedIdentifier(source, ref index))
                    {
                        // Stepped over and not recorded: it is a name, which a search for a
                        // member has to find in code
                        continue;
                    }

                    break;
                case '\'':
                    // Only where the tick cannot be part of the name in front of it, and only
                    // where a closing tick follows within a literal's length. Everything else is
                    // a type parameter, which is code
                    if (!IsIdentifierChar(index > 0 ? source[index - 1] : ' ') &&
                        TrySkipCharLiteral(source, ref index))
                    {
                        scan.AddSkip(start, index, comment: false);
                        continue;
                    }

                    break;
                case '"':
                case '@':
                case '$':
                    if (TrySkipStringLike(source, ref index))
                    {
                        // The B of a byte string is part of the literal token, so a search for "x"
                        // cannot match "x"B and splice over only the quoted part
                        while (index < source.Length &&
                               (char.IsLetterOrDigit(source[index]) || source[index] == '_'))
                        {
                            index++;
                        }

                        scan.AddSkip(start, index, comment: false);
                        continue;
                    }

                    break;
            }

            // Code, which is whatever the map was not told otherwise about
            index++;
        }
    }

    internal override bool IsDeclaration(SourceScan scan, int nameStart)
    {
        var source = scan.Source;
        var index = scan.PreviousSignificant(nameStart);
        if (index < 0)
        {
            return false;
        }

        if (source[index] == '.')
        {
            // member this.Snapshot, which otherwise reads exactly like the receiver of a call.
            // Step back over the self identifier and judge by what introduced it
            var receiver = scan.PreviousSignificant(index);
            if (receiver < 0 ||
                !IsIdentifierChar(source[receiver]))
            {
                return false;
            }

            index = scan.PreviousSignificant(scan.WordStart(receiver));
            if (index < 0)
            {
                return false;
            }
        }

        if (!IsIdentifierChar(source[index]))
        {
            return false;
        }

        return declarationKeywords.Contains(scan.WordEndingAt(index));
    }

    /// <summary>
    /// The keywords that introduce a binding. A name preceded by one of them is being declared;
    /// anything else in front of a name - an operator, a bracket, or a keyword that introduces an
    /// expression - leaves it a call.
    /// </summary>
    static readonly HashSet<string> declarationKeywords =
    [
        with(StringComparer.Ordinal),
        "abstract", "and", "default", "inline", "internal", "let", "member", "mutable",
        "override", "private", "public", "rec", "static", "use", "val"
    ];

    static bool TrySkipLineComment(string source, ref int index)
    {
        if (index + 1 >= source.Length ||
            source[index + 1] != '/')
        {
            return false;
        }

        var end = source.IndexOf('\n', index);
        index = end < 0 ? source.Length : end + 1;
        return true;
    }

    /// <summary>
    /// Block comments nest, so the scan counts them rather than stopping at the first close.
    /// <para>
    /// And F# lexes inside one, which is what lets a comment hold commented out code: a string in
    /// a comment is a string, so <c>(* returns "*)" when closed *)</c> and <c>(* see "(*" *)</c>
    /// are one comment each, and <c>(*)</c> inside one is the operator, neither opening nor
    /// closing anything. Read as text, the first ended at the quoted close with a string opening
    /// after it, and the other two never ended, and either way the calls below were inside
    /// something and not found. What fsi was seen to take as a token in a comment is what is
    /// stepped over here: a regular string with its escapes, a verbatim one only as <c>@"</c>, a
    /// triple quoted one, and a char literal, which is where a quote that opens no string is
    /// written. No interpolation, where <c>$"</c> is a dollar and then a regular string, and no
    /// backticks, which are text in a comment. <c>FsCompilerRoundTripTests</c> holds each shape to
    /// the compiler.
    /// </para>
    /// </summary>
    static bool TrySkipBlockComment(string source, ref int index)
    {
        if (!StartsBlockComment(source, index))
        {
            return false;
        }

        var cursor = index + 2;
        var depth = 1;
        while (cursor < source.Length)
        {
            var ch = source[cursor];
            var next = cursor + 1 < source.Length ? source[cursor + 1] : '\0';
            if (ch == '(' &&
                next == '*')
            {
                if (cursor + 2 < source.Length &&
                    source[cursor + 2] == ')')
                {
                    // The operator, as it is in code. Stepped over whole, or its last two
                    // characters would close the comment
                    cursor += 3;
                    continue;
                }

                depth++;
                cursor += 2;
                continue;
            }

            if (ch == '*' &&
                next == ')')
            {
                depth--;
                cursor += 2;
                if (depth == 0)
                {
                    index = cursor;
                    return true;
                }

                continue;
            }

            if (ch == '"' ||
                (ch == '@' && next == '"'))
            {
                // Never false from here: both start a string. One left open runs to the end of
                // the file, which the compiler refuses outright
                TrySkipStringLike(source, ref cursor);
                continue;
            }

            if (ch == '\'' &&
                TrySkipCharLiteral(source, ref cursor))
            {
                continue;
            }

            cursor++;
        }

        // Unterminated: the rest of the file is comment, which is what the compiler sees too
        index = source.Length;
        return true;
    }

    /// <summary>
    /// A double backticked identifier, which is how an F# test is usually named and may hold
    /// anything a line can but a tab and two backticks together:
    /// <c>``returns "x" (* when asked``</c>. Nothing inside one opens a string or a comment.
    /// </summary>
    static bool TrySkipQuotedIdentifier(string source, ref int index)
    {
        if (index + 1 >= source.Length ||
            source[index + 1] != '`')
        {
            return false;
        }

        var cursor = index + 2;
        while (cursor < source.Length)
        {
            var ch = source[cursor];
            if (ch is '\n' or '\r' or '\t')
            {
                return false;
            }

            if (ch == '`' &&
                cursor + 1 < source.Length &&
                source[cursor + 1] == '`')
            {
                // Two backticks with nothing between them and the opening pair name nothing
                if (cursor == index + 2)
                {
                    return false;
                }

                index = cursor + 2;
                return true;
            }

            cursor++;
        }

        return false;
    }

    static bool StartsBlockComment(string source, int index) =>
        index + 1 < source.Length &&
        source[index] == '(' &&
        source[index + 1] == '*' &&
        // (*) is the multiplication operator as a function, not an empty comment
        !(index + 2 < source.Length && source[index + 2] == ')');

    /// <summary>
    /// Strict, because the alternative reading of a tick is a type parameter and swallowing to the
    /// next one would take a span of code out of the map. Only a literal that closes where a
    /// literal has to close is one.
    /// </summary>
    static bool TrySkipCharLiteral(string source, ref int index)
    {
        var cursor = index + 1;
        if (cursor >= source.Length)
        {
            return false;
        }

        var ch = source[cursor];
        if (ch == '\\')
        {
            cursor++;
            if (cursor >= source.Length)
            {
                return false;
            }

            var escape = source[cursor];
            cursor++;
            switch (escape)
            {
                case 'u':
                    if (!TrySkipHex(source, ref cursor, 4))
                    {
                        return false;
                    }

                    break;
                case 'U':
                    if (!TrySkipHex(source, ref cursor, 8))
                    {
                        return false;
                    }

                    break;
                case 'x':
                    if (!TrySkipHex(source, ref cursor, 2))
                    {
                        return false;
                    }

                    break;
                default:
                    if (char.IsDigit(escape) &&
                        !TrySkipDigits(source, ref cursor, 2))
                    {
                        return false;
                    }

                    break;
            }
        }
        else if (ch is '\'' or '\n' or '\r')
        {
            return false;
        }
        else
        {
            cursor++;
        }

        if (cursor >= source.Length ||
            source[cursor] != '\'')
        {
            return false;
        }

        index = cursor + 1;
        return true;
    }

    static bool TrySkipHex(string source, ref int index, int count)
    {
        for (var read = 0; read < count; read++)
        {
            if (index >= source.Length ||
                !Uri.IsHexDigit(source[index]))
            {
                return false;
            }

            index++;
        }

        return true;
    }

    static bool TrySkipDigits(string source, ref int index, int count)
    {
        for (var read = 0; read < count; read++)
        {
            if (index >= source.Length ||
                !char.IsDigit(source[index]))
            {
                return false;
            }

            index++;
        }

        return true;
    }

    // index at '$', '@' or '"'. Returns false when the characters do not start a string literal
    // (eg the list append operator '@'); the caller then advances by one.
    static bool TrySkipStringLike(string source, ref int index)
    {
        var cursor = index;
        var interpolated = false;
        var verbatim = false;
        while (cursor < source.Length)
        {
            var ch = source[cursor];
            if (ch == '$')
            {
                interpolated = true;
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

        var quotes = StringLiteral.QuoteRunLength(source, cursor);
        // There is no verbatim triple-quoted form, so a run of quotes after @" is an escaped quote
        // and the rest of the string, not a delimiter
        if (quotes >= 3 && !verbatim)
        {
            // Triple quoted: verbatim, so there are no escapes to consider and no delimiter to
            // widen. Its text ends at the next run of three quotes. A hole is not text: it may
            // hold a string or a comment with three quotes of its own, which end nothing
            var search = cursor + 3;
            // As many braces open a hole as there are dollars in front, and fewer are text
            var dollars = interpolated ? cursor - index : 0;
            while (search < source.Length)
            {
                if (source[search] == '"' &&
                    StringLiteral.QuoteRunLength(source, search) >= 3)
                {
                    index = search + 3;
                    return true;
                }

                if (dollars > 0 &&
                    source[search] == '{')
                {
                    var braces = 1;
                    while (search + braces < source.Length &&
                           source[search + braces] == '{')
                    {
                        braces++;
                    }

                    // With one dollar a doubled brace is a brace, so only an odd one out opens.
                    // With more, the last of a run that is long enough do, and the ones in front
                    // of them are text
                    var opens = dollars == 1 ? braces % 2 == 1 : braces >= dollars;
                    search += braces;
                    if (!opens)
                    {
                        continue;
                    }

                    search--;
                    if (!TrySkipHole(source, ref search))
                    {
                        index = source.Length;
                        return true;
                    }

                    // The rest of the braces that close it
                    for (var closing = 1; closing < dollars && search < source.Length && source[search] == '}'; closing++)
                    {
                        search++;
                    }

                    continue;
                }

                search++;
            }

            index = source.Length;
            return true;
        }

        if (quotes == 2 && !verbatim)
        {
            // Empty string "" or interpolated empty string $""
            index = cursor + 2;
            return true;
        }

        cursor++;
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
                // Escape or line continuation: either way the next character is content. Except a
                // brace where braces open holes, which no backslash escapes
                cursor += interpolated && cursor + 1 < source.Length && source[cursor + 1] == '{' ? 1 : 2;
                continue;
            }

            if (interpolated && ch == '{')
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

            if (interpolated && ch == '}' &&
                cursor + 1 < source.Length && source[cursor + 1] == '}')
            {
                cursor += 2;
                continue;
            }

            // An ordinary F# string may span lines, so a newline is content rather than the end
            cursor++;
        }

        index = source.Length;
        return true;
    }

    /// <summary>
    /// Steps past an interpolation hole, from its <c>{</c> to past the <c>}</c> that closes it.
    /// <para>
    /// A hole is code, and is lexed as code is: a brace in a char literal, a comment, a string or
    /// a backticked name is not one of the hole's own, and a quote in a char literal or a comment
    /// opens no string. Counting every brace and taking every quote for a string, as this did,
    /// <c>$"{'{'}"</c> never closed and <c>$"{'"'}"</c> closed on a string that ran to the end of
    /// the file, and the calls under either were inside a literal. <c>FsCompilerRoundTripTests</c>
    /// holds each shape to the compiler.
    /// </para>
    /// </summary>
    static bool TrySkipHole(string source, ref int cursor)
    {
        var depth = 1;
        cursor++;
        while (cursor < source.Length)
        {
            switch (source[cursor])
            {
                case '"':
                case '@':
                case '$':
                    if (!TrySkipStringLike(source, ref cursor))
                    {
                        cursor++;
                    }

                    continue;
                case '\'':
                    // The rule code has: a tick after a name is part of the name
                    if (IsNameChar(source[cursor - 1]) ||
                        !TrySkipCharLiteral(source, ref cursor))
                    {
                        cursor++;
                    }

                    continue;
                case '(':
                    if (!TrySkipBlockComment(source, ref cursor))
                    {
                        cursor++;
                    }

                    continue;
                case '/':
                    if (!TrySkipLineComment(source, ref cursor))
                    {
                        cursor++;
                    }

                    continue;
                case '`':
                    if (!TrySkipQuotedIdentifier(source, ref cursor))
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

}
