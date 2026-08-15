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
    /// F# does not apply the implicit conversion that lets a SettingsTask be awaited, so an F#
    /// test ends the chain with ToTask. Snapshot returns the SettingsTask and ToTask does not, so
    /// an appended call goes in front of it rather than after it.
    /// </summary>
    internal override string? ChainTerminator => "ToTask";

    /// <summary>
    /// The F# compiler does not implement <see cref="CallerArgumentExpressionAttribute"/> - it
    /// warns FS0202 and leaves the parameter at its default - so an F# patch never carries the
    /// expression its C# equivalent is anchored to, and is located by line hint alone.
    /// </summary>
    internal override bool SuppliesArgumentExpressions => false;

    internal override bool IsIdentifierChar(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_' || ch == '\'';

    internal override bool IsTypeArgumentChar(char ch) =>
        base.IsTypeArgumentChar(ch) ||
        // Tuple types (Foo<int * string>) and statically resolved type parameters (^T)
        ch is '*' or '^';

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

            scan.MarkCode(index);
            index++;
        }

        return scan;
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
            if (StartsBlockComment(source, cursor))
            {
                depth++;
                cursor += 2;
                continue;
            }

            if (source[cursor] == '*' &&
                cursor + 1 < source.Length &&
                source[cursor + 1] == ')')
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

            cursor++;
        }

        // Unterminated: the rest of the file is comment, which is what the compiler sees too
        index = source.Length;
        return true;
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

        var quotes = QuoteRun(source, cursor);
        // There is no verbatim triple-quoted form, so a run of quotes after @" is an escaped quote
        // and the rest of the string, not a delimiter
        if (quotes >= 3 && !verbatim)
        {
            // Triple quoted: verbatim, so there are no escapes to consider and no delimiter to
            // widen. It ends at the next run of three quotes, interpolation holes included
            var search = cursor + 3;
            while (search < source.Length)
            {
                if (source[search] == '"' &&
                    QuoteRun(source, search) >= 3)
                {
                    index = search + 3;
                    return true;
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
                // Escape or line continuation: either way the next character is content
                cursor += 2;
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

    // cursor at '{' of an interpolation hole; skips past the matching '}'
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
