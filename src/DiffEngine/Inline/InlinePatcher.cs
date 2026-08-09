enum PatchStatus
{
    Applied,
    AlreadyApplied,
    NotFound
}

/// <summary>
/// Pure string in / string out engine that locates an inline snapshot call site in C# source
/// and splices in a new raw string literal. No file IO.
/// </summary>
static class InlinePatcher
{
    /// <summary>
    /// The fluent call that carries the snapshot literal.
    /// </summary>
    const string methodName = "Snapshot";

    /// <summary>
    /// Append mode has no Snapshot call to find, so it locates the verify invocation instead.
    /// Matched by prefix because every entry point is Verify, VerifyXml, VerifyJson and so on.
    /// </summary>
    const string verifyPrefix = "Verify";

    public static PatchStatus TryApply(
        string source,
        int lineHint,
        InlinePatchMode mode,
        string? originalExpression,
        string newContent,
        out string newSource,
        out string failReason)
    {
        newSource = "";
        failReason = "";
        var eol = DetectEol(source);
        var lineStarts = BuildLineStarts(source);

        if (mode == InlinePatchMode.Remove)
        {
            return TryRemove(source, lineStarts, lineHint, ref newSource, ref failReason);
        }

        if (mode == InlinePatchMode.Append)
        {
            return TryAppend(source, lineStarts, lineHint, newContent, eol, ref newSource, ref failReason);
        }

        if (!string.IsNullOrEmpty(originalExpression))
        {
            // Search for the previous expression verbatim, with newlines matched to the file's EOL
            var needle = NormalizeTo(originalExpression!, eol);
            var occurrences = FindAll(source, needle);
            if (occurrences.Count > 0)
            {
                var start = Nearest(occurrences, lineStarts, lineHint);
                if (CsStringLiteral.TryParse(needle, out var oldValue) &&
                    oldValue == newContent)
                {
                    return PatchStatus.AlreadyApplied;
                }

                var indent = IndentForSpan(source, lineStarts, start);
                var rendered = CsStringLiteral.RenderRaw(newContent, indent, eol);
                newSource = Splice(source, start, start + needle.Length, rendered);
                return PatchStatus.Applied;
            }

            // Expression gone: another process may have applied the same patch already
            return InsertOrCheck(source, lineStarts, lineHint, newContent, eol, alreadyOnly: true, ref newSource, ref failReason);
        }

        return InsertOrCheck(source, lineStarts, lineHint, newContent, eol, alreadyOnly: false, ref newSource, ref failReason);
    }

    static PatchStatus InsertOrCheck(
        string source,
        List<int> lineStarts,
        int lineHint,
        string newContent,
        string eol,
        bool alreadyOnly,
        ref string newSource,
        ref string failReason)
    {
        if (!TryFindCall(source, lineStarts, lineHint, out var openParen))
        {
            failReason = $"Could not find a {methodName} call near line {lineHint}. The source may have changed since the test run. Re-run the test.";
            return PatchStatus.NotFound;
        }

        if (!TryScanArguments(source, openParen, out var closeParen, out var topCommas))
        {
            failReason = $"Could not parse the argument list of the {methodName} call near line {lineHint}.";
            return PatchStatus.NotFound;
        }

        // expected is the first parameter of Snapshot, so the argument to set is the first one
        var argStart = openParen + 1;
        var argEnd = topCommas.Count > 0 ? topCommas[0] : closeParen;
        TrimSpan(source, ref argStart, ref argEnd);

        if (argStart == argEnd)
        {
            // The argument was left to its default
            if (alreadyOnly)
            {
                failReason = StaleReason(lineHint);
                return PatchStatus.NotFound;
            }

            var emptyIndent = IndentForSpan(source, lineStarts, argStart);
            var emptyRendered = CsStringLiteral.RenderRaw(newContent, emptyIndent, eol);
            newSource = Splice(source, argStart, argStart, emptyRendered);
            return PatchStatus.Applied;
        }

        var argOriginalStart = argStart;
        var named = TryStripArgumentName(source, ref argStart, out var argumentName);
        var argText = source.Substring(argStart, argEnd - argStart);

        if (named && argumentName != "expected")
        {
            // Some other named argument came first (eg file:).
            // Insert a named expected argument before it.
            if (alreadyOnly)
            {
                failReason = StaleReason(lineHint);
                return PatchStatus.NotFound;
            }

            var namedIndent = IndentForSpan(source, lineStarts, argOriginalStart);
            var namedRendered = CsStringLiteral.RenderRaw(newContent, namedIndent, eol);
            newSource = Splice(source, argOriginalStart, argOriginalStart, "expected: " + namedRendered + ", ");
            return PatchStatus.Applied;
        }

        if (argText == "null")
        {
            if (alreadyOnly)
            {
                failReason = StaleReason(lineHint);
                return PatchStatus.NotFound;
            }

            var indent = IndentForSpan(source, lineStarts, argStart);
            var rendered = CsStringLiteral.RenderRaw(newContent, indent, eol);
            newSource = Splice(source, argStart, argEnd, rendered);
            return PatchStatus.Applied;
        }

        if (CsStringLiteral.TryParse(argText, out var currentValue))
        {
            if (currentValue == newContent)
            {
                return PatchStatus.AlreadyApplied;
            }

            failReason = alreadyOnly
                ? $"The previous expected expression was not found near line {lineHint}, and the current expected argument has different content. The source may have changed since the test run. Re-run the test."
                : $"The {methodName} call near line {lineHint} already has a different expected argument.";
            return PatchStatus.NotFound;
        }

        failReason = $"The expected argument of the {methodName} call near line {lineHint} is not a string literal.";
        return PatchStatus.NotFound;
    }

    static string StaleReason(int lineHint) =>
        $"The previous expected expression was not found near line {lineHint}. The source may have changed since the test run. Re-run the test.";

    /// <summary>
    /// Appends a Snapshot call to the verify invocation, for a snapshot that has never been
    /// accepted. Snapshot terminates the chain, so the insertion point is the end of any calls
    /// already chained onto the invocation rather than the invocation's own closing paren.
    /// </summary>
    static PatchStatus TryAppend(
        string source,
        List<int> lineStarts,
        int lineHint,
        string newContent,
        string eol,
        ref string newSource,
        ref string failReason)
    {
        if (!TryFindCall(source, lineStarts, lineHint, verifyPrefix, true, out var nameStart, out var openParen))
        {
            failReason = $"Could not find a {verifyPrefix} call near line {lineHint}. The source may have changed since the test run. Re-run the test.";
            return PatchStatus.NotFound;
        }

        if (!TryScanArguments(source, openParen, out var closeParen, out _))
        {
            failReason = $"Could not parse the argument list of the {verifyPrefix} call near line {lineHint}.";
            return PatchStatus.NotFound;
        }

        var insertAt = WalkChain(source, closeParen + 1, methodName, out var alreadyChained);
        // Another process may have appended one between the run and the accept
        if (alreadyChained)
        {
            failReason = $"The call near line {lineHint} already has a {methodName} call. Re-run the test.";
            return PatchStatus.NotFound;
        }

        var statementIndent = LeadingWhitespace(source, lineStarts, nameStart);
        var unit = statementIndent.Contains('\t') ? "\t" : "    ";
        // Line up with the existing chain when there is one, otherwise start it one level in
        var callIndent = LineOf(lineStarts, insertAt - 1) == LineOf(lineStarts, nameStart)
            ? statementIndent + unit
            : LeadingWhitespace(source, lineStarts, insertAt - 1);
        var rendered = CsStringLiteral.RenderRaw(newContent, callIndent + unit, eol);
        newSource = Splice(source, insertAt, insertAt, $"{eol}{callIndent}.{methodName}({rendered})");
        return PatchStatus.Applied;
    }

    /// <summary>
    /// Removes the Snapshot call, along with the whitespace and line break that preceded it so no
    /// blank line is left behind.
    /// </summary>
    static PatchStatus TryRemove(
        string source,
        List<int> lineStarts,
        int lineHint,
        ref string newSource,
        ref string failReason)
    {
        if (!TryFindCall(source, lineStarts, lineHint, methodName, false, out var nameStart, out var openParen))
        {
            failReason = $"Could not find a {methodName} call near line {lineHint}. The source may have changed since the test run. Re-run the test.";
            return PatchStatus.NotFound;
        }

        if (!TryScanArguments(source, openParen, out var closeParen, out _))
        {
            failReason = $"Could not parse the argument list of the {methodName} call near line {lineHint}.";
            return PatchStatus.NotFound;
        }

        var start = nameStart;
        // Back over the dot that made it a chained call
        while (start > 0 &&
               char.IsWhiteSpace(source[start - 1]))
        {
            start--;
        }

        if (start == 0 ||
            source[start - 1] != '.')
        {
            failReason = $"The {methodName} call near line {lineHint} is not a chained call.";
            return PatchStatus.NotFound;
        }

        start--;
        // Then back over the indentation and line break it sat on
        while (start > 0 &&
               (source[start - 1] == ' ' || source[start - 1] == '\t'))
        {
            start--;
        }

        if (start > 0 &&
            source[start - 1] == '\n')
        {
            start--;
            if (start > 0 &&
                source[start - 1] == '\r')
            {
                start--;
            }
        }

        newSource = Splice(source, start, closeParen + 1, "");
        return PatchStatus.Applied;
    }

    /// <summary>
    /// Walks the calls chained onto an invocation and returns the end of the chain.
    /// <paramref name="found"/> is set when one of them is a call to <paramref name="name"/>.
    /// </summary>
    static int WalkChain(string source, int index, string name, out bool found)
    {
        found = false;
        while (true)
        {
            var cursor = index;
            if (!TrySkipTo(source, ref cursor, '.'))
            {
                return index;
            }

            cursor++;
            while (cursor < source.Length &&
                   char.IsWhiteSpace(source[cursor]))
            {
                cursor++;
            }

            var nameStart = cursor;
            while (cursor < source.Length &&
                   IsIdentifierChar(source[cursor]))
            {
                cursor++;
            }

            if (cursor == nameStart ||
                !TrySkipToParen(source, cursor, out var paren) ||
                !TryScanArguments(source, paren, out var closeParen, out _))
            {
                return index;
            }

            if (string.CompareOrdinal(source, nameStart, name, 0, name.Length) == 0 &&
                cursor - nameStart == name.Length)
            {
                found = true;
            }

            index = closeParen + 1;
        }
    }

    static bool TrySkipTo(string source, ref int index, char ch)
    {
        while (index < source.Length &&
               char.IsWhiteSpace(source[index]))
        {
            index++;
        }

        return index < source.Length &&
               source[index] == ch;
    }

    static string LeadingWhitespace(string source, List<int> lineStarts, int offset)
    {
        var lineStart = lineStarts[LineOf(lineStarts, offset) - 1];
        var index = lineStart;
        while (index < source.Length &&
               (source[index] == ' ' || source[index] == '\t'))
        {
            index++;
        }

        return source.Substring(lineStart, index - lineStart);
    }

    static bool TryFindCall(string source, List<int> lineStarts, int lineHint, out int openParen) =>
        TryFindCall(source, lineStarts, lineHint, methodName, false, out _, out openParen);

    /// <summary>
    /// Locates a call by name, searching outward from the hint. <paramref name="byPrefix"/> matches
    /// any identifier starting with <paramref name="name"/>, which is how the several Verify
    /// overloads are found with one search.
    /// </summary>
    static bool TryFindCall(
        string source,
        List<int> lineStarts,
        int lineHint,
        string name,
        bool byPrefix,
        out int nameStart,
        out int openParen)
    {
        nameStart = -1;
        openParen = -1;
        var lineCount = lineStarts.Count;
        lineHint = Math.Min(Math.Max(lineHint, 1), lineCount);
        // Outward from the hint: hint, hint-1, hint+1, hint-2, ...
        for (var distance = 0; distance < lineCount; distance++)
        {
            var candidates = distance == 0
                ? new[] { lineHint }
                : new[] { lineHint - distance, lineHint + distance };
            foreach (var line in candidates)
            {
                if (line < 1 || line > lineCount)
                {
                    continue;
                }

                var start = lineStarts[line - 1];
                var end = line < lineCount ? lineStarts[line] : source.Length;
                var index = start;
                while (true)
                {
                    index = source.IndexOf(name, index, StringComparison.Ordinal);
                    if (index < 0 || index >= end)
                    {
                        break;
                    }

                    var identifierEnd = index + name.Length;
                    if (byPrefix)
                    {
                        while (identifierEnd < source.Length &&
                               IsIdentifierChar(source[identifierEnd]))
                        {
                            identifierEnd++;
                        }
                    }
                    else if (identifierEnd < source.Length &&
                             IsIdentifierChar(source[identifierEnd]))
                    {
                        index += name.Length;
                        continue;
                    }

                    if (StartsToken(source, index) &&
                        TrySkipToParen(source, identifierEnd, out var paren))
                    {
                        nameStart = index;
                        openParen = paren;
                        return true;
                    }

                    index += name.Length;
                }
            }
        }

        return false;
    }

    static bool StartsToken(string source, int index) =>
        index == 0 ||
        !IsIdentifierChar(source[index - 1]);

    static bool IsIdentifierChar(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_';

    static bool TrySkipToParen(string source, int index, out int paren)
    {
        paren = -1;
        while (index < source.Length && char.IsWhiteSpace(source[index]))
        {
            index++;
        }

        if (index < source.Length && source[index] == '(')
        {
            paren = index;
            return true;
        }

        return false;
    }

    // Scans a balanced argument list starting at the open paren.
    // Records top level comma positions. Skips strings, chars and comments.
    static bool TryScanArguments(string source, int openParen, out int closeParen, out List<int> topCommas)
    {
        closeParen = -1;
        topCommas = [];
        var depth = 1;
        var index = openParen + 1;
        while (index < source.Length)
        {
            var ch = source[index];
            switch (ch)
            {
                case '/':
                    if (!TrySkipComment(source, ref index))
                    {
                        index++;
                    }

                    continue;
                case '\'':
                    if (!TrySkipCharLiteral(source, ref index))
                    {
                        return false;
                    }

                    continue;
                case '"':
                case '@':
                case '$':
                    if (!TrySkipStringLike(source, ref index))
                    {
                        index++;
                    }

                    continue;
                case '(':
                case '[':
                case '{':
                    depth++;
                    index++;
                    continue;
                case ')':
                    depth--;
                    if (depth == 0)
                    {
                        closeParen = index;
                        return true;
                    }

                    index++;
                    continue;
                case ']':
                case '}':
                    depth--;
                    if (depth <= 0)
                    {
                        return false;
                    }

                    index++;
                    continue;
                case ',':
                    if (depth == 1)
                    {
                        topCommas.Add(index);
                    }

                    index++;
                    continue;
                default:
                    index++;
                    continue;
            }
        }

        return false;
    }

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
        if (quotes == 2 && dollars == 0)
        {
            // Empty string ""
            index = cursor;
            return true;
        }

        if (quotes == 2)
        {
            // Interpolated empty string $""
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

    static bool TryStripArgumentName(string source, ref int start, out string name)
    {
        name = "";
        var index = start;
        if (index >= source.Length || !char.IsLetter(source[index]) && source[index] != '_')
        {
            return false;
        }

        while (index < source.Length && IsIdentifierChar(source[index]))
        {
            index++;
        }

        var nameEnd = index;
        while (index < source.Length && char.IsWhiteSpace(source[index]))
        {
            index++;
        }

        if (index >= source.Length ||
            source[index] != ':' ||
            index + 1 < source.Length && source[index + 1] == ':')
        {
            return false;
        }

        name = source.Substring(start, nameEnd - start);
        index++;
        while (index < source.Length && char.IsWhiteSpace(source[index]))
        {
            index++;
        }

        start = index;
        return true;
    }

    static void TrimSpan(string source, ref int start, ref int end)
    {
        while (start < end && char.IsWhiteSpace(source[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(source[end - 1]))
        {
            end--;
        }
    }

    static string Splice(string source, int start, int end, string replacement) =>
        new StringBuilder(source.Length - (end - start) + replacement.Length)
            .Append(source, 0, start)
            .Append(replacement)
            .Append(source, end, source.Length - end)
            .ToString();

    static string DetectEol(string source)
    {
        var crlf = 0;
        var lf = 0;
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] != '\n')
            {
                continue;
            }

            if (index > 0 && source[index - 1] == '\r')
            {
                crlf++;
            }
            else
            {
                lf++;
            }
        }

        if (crlf >= lf && crlf > 0)
        {
            return "\r\n";
        }

        if (lf > 0)
        {
            return "\n";
        }

        return Environment.NewLine;
    }

    static string NormalizeTo(string value, string eol) =>
        value
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace("\n", eol);

    static List<int> BuildLineStarts(string source)
    {
        List<int> starts = [0];
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] == '\n' && index + 1 < source.Length)
            {
                starts.Add(index + 1);
            }
        }

        return starts;
    }

    static int LineOf(List<int> lineStarts, int offset)
    {
        var low = 0;
        var high = lineStarts.Count - 1;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (lineStarts[mid] <= offset)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return low + 1;
    }

    static List<int> FindAll(string source, string needle)
    {
        List<int> result = [];
        var index = 0;
        while (true)
        {
            index = source.IndexOf(needle, index, StringComparison.Ordinal);
            if (index < 0)
            {
                break;
            }

            result.Add(index);
            index++;
        }

        return result;
    }

    static int Nearest(List<int> occurrences, List<int> lineStarts, int lineHint)
    {
        var best = occurrences[0];
        var bestDistance = int.MaxValue;
        var bestAfter = false;
        foreach (var occurrence in occurrences)
        {
            var line = LineOf(lineStarts, occurrence);
            var distance = Math.Abs(line - lineHint);
            var after = line >= lineHint;
            if (distance < bestDistance ||
                distance == bestDistance && after && !bestAfter)
            {
                best = occurrence;
                bestDistance = distance;
                bestAfter = after;
            }
        }

        return best;
    }

    static string IndentForSpan(string source, List<int> lineStarts, int spanStart)
    {
        var line = LineOf(lineStarts, spanStart);
        var lineStart = lineStarts[line - 1];
        var lead = new StringBuilder();
        var index = lineStart;
        while (index < source.Length &&
               (source[index] == ' ' || source[index] == '\t'))
        {
            lead.Append(source[index]);
            index++;
        }

        if (index >= spanStart)
        {
            // The span starts on its own line: align with it
            return source.Substring(lineStart, spanStart - lineStart);
        }

        var leadText = lead.ToString();
        var unit = leadText.Contains('\t') ? "\t" : "    ";
        return leadText + unit;
    }
}
