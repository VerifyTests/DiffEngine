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
    const string methodName = "VerifyInline";

    public static PatchStatus TryApply(
        string source,
        int lineHint,
        string? originalExpression,
        string newContent,
        out string newSource,
        out string failReason)
    {
        newSource = "";
        failReason = "";
        var eol = DetectEol(source);
        var lineStarts = BuildLineStarts(source);

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

        if (topCommas.Count == 0)
        {
            // Only the target argument exists
            if (source.Substring(openParen + 1, closeParen - openParen - 1).Trim().Length == 0)
            {
                failReason = $"The {methodName} call near line {lineHint} has no arguments.";
                return PatchStatus.NotFound;
            }

            if (alreadyOnly)
            {
                failReason = $"The previous expected expression was not found near line {lineHint}. The source may have changed since the test run. Re-run the test.";
                return PatchStatus.NotFound;
            }

            var insertAt = EndOfLastNonWhitespace(source, openParen + 1, closeParen);
            var indent = IndentForSpan(source, lineStarts, insertAt) ;
            var rendered = CsStringLiteral.RenderRaw(newContent, indent, eol);
            newSource = Splice(source, insertAt, insertAt, ", " + rendered);
            return PatchStatus.Applied;
        }

        // A second argument exists
        var argStart = topCommas[0] + 1;
        var argEnd = topCommas.Count > 1 ? topCommas[1] : closeParen;
        TrimSpan(source, ref argStart, ref argEnd);
        var argOriginalStart = argStart;
        var named = TryStripArgumentName(source, ref argStart, out var argumentName);
        var argText = source.Substring(argStart, argEnd - argStart);

        if (named && argumentName != "expected")
        {
            // Second argument is some other named argument (eg settings:).
            // Insert a named expected argument before it.
            if (alreadyOnly)
            {
                failReason = $"The previous expected expression was not found near line {lineHint}. The source may have changed since the test run. Re-run the test.";
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
                failReason = $"The previous expected expression was not found near line {lineHint}. The source may have changed since the test run. Re-run the test.";
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

    static bool TryFindCall(string source, List<int> lineStarts, int lineHint, out int openParen)
    {
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
                    index = source.IndexOf(methodName, index, StringComparison.Ordinal);
                    if (index < 0 || index >= end)
                    {
                        break;
                    }

                    if (IsToken(source, index, methodName.Length) &&
                        TrySkipToParen(source, index + methodName.Length, out var paren))
                    {
                        openParen = paren;
                        return true;
                    }

                    index += methodName.Length;
                }
            }
        }

        return false;
    }

    static bool IsToken(string source, int index, int length)
    {
        if (index > 0 && IsIdentifierChar(source[index - 1]))
        {
            return false;
        }

        var after = index + length;
        return after >= source.Length || !IsIdentifierChar(source[after]);
    }

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

    static int EndOfLastNonWhitespace(string source, int start, int end)
    {
        var index = end;
        while (index > start && char.IsWhiteSpace(source[index - 1]))
        {
            index--;
        }

        return index;
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
