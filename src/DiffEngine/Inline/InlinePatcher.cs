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
    /// The parameter the snapshot literal binds to. Positional in a normal call, but it can be
    /// written by name.
    /// </summary>
    const string parameterName = "expected";

    /// <summary>
    /// Append mode has no Snapshot call to find, so it locates the verify invocation instead.
    /// Matched by prefix because every entry point is Verify, VerifyXml, VerifyJson and so on.
    /// </summary>
    const string verifyPrefix = "Verify";

    /// <summary>
    /// The only receiver a verify entry point is reached through. Every adapter exposes the entry
    /// points unqualified (a static using, or inherited from VerifyBase) or on this one class.
    /// </summary>
    const string verifierType = "Verifier";

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
        var scan = new CsScan(source);

        if (mode == InlinePatchMode.Remove)
        {
            return TryRemove(source, scan, lineStarts, lineHint, ref newSource, ref failReason);
        }

        var fileUnit = DetectIndentUnit(source, scan, lineStarts);

        if (mode == InlinePatchMode.Append)
        {
            return TryAppend(source, scan, lineStarts, lineHint, newContent, eol, fileUnit, ref newSource, ref failReason);
        }

        if (!string.IsNullOrEmpty(originalExpression))
        {
            // Located by content: the Snapshot call whose expected argument is still the text the
            // test run saw, nearest the hint first. Matched against expected arguments rather than
            // searched for as plain text, because the same literal is just as likely to sit in a
            // comment, in another test's snapshot content, or in the verify call on the same line,
            // and splicing into one of those leaves a file that no longer compiles and a snapshot
            // still unaccepted.
            // ReSharper disable once RedundantSuppressNullableWarningExpression
            var needle = NormalizeTo(originalExpression!, eol);
            foreach (var (_, openParen) in FindCalls(source, scan, lineStarts, lineHint, methodName, false))
            {
                if (!TryReadArguments(source, scan, openParen, out var expected) ||
                    !expected.Matches(source, needle))
                {
                    continue;
                }

                if (CsStringLiteral.TryParse(needle, out var oldValue) &&
                    oldValue == newContent)
                {
                    return PatchStatus.AlreadyApplied;
                }

                var rendered = RenderArgument(source, lineStarts, expected.Start, newContent, eol, fileUnit);
                newSource = Splice(source, expected.Start, expected.End, rendered);
                return PatchStatus.Applied;
            }

            // Expression gone: another process may have applied the same patch already
            return InsertOrCheck(source, scan, lineStarts, lineHint, newContent, eol, fileUnit, alreadyOnly: true, ref newSource, ref failReason);
        }

        return InsertOrCheck(source, scan, lineStarts, lineHint, newContent, eol, fileUnit, alreadyOnly: false, ref newSource, ref failReason);
    }

    static PatchStatus InsertOrCheck(
        string source,
        CsScan scan,
        List<int> lineStarts,
        int lineHint,
        string newContent,
        string eol,
        string fileUnit,
        bool alreadyOnly,
        ref string newSource,
        ref string failReason)
    {
        if (!TryFindCall(source, scan, lineStarts, lineHint, out var openParen))
        {
            failReason = $"Could not find a {methodName} call near line {lineHint}. The source may have changed since the test run. Re-run the test.";
            return PatchStatus.NotFound;
        }

        if (!TryReadArguments(source, scan, openParen, out var expected))
        {
            failReason = $"Could not parse the argument list of the {methodName} call near line {lineHint}.";
            return PatchStatus.NotFound;
        }

        if (expected.IsAbsent)
        {
            // The argument was left to its default
            if (alreadyOnly)
            {
                failReason = StaleReason(lineHint);
                return PatchStatus.NotFound;
            }

            var emptyRendered = RenderArgument(source, lineStarts, expected.Start, newContent, eol, fileUnit);
            newSource = Splice(source, expected.Start, expected.Start, emptyRendered);
            return PatchStatus.Applied;
        }

        if (expected.BlockedByName)
        {
            // Some other named argument came first (eg file:).
            // Insert a named expected argument before it.
            if (alreadyOnly)
            {
                failReason = StaleReason(lineHint);
                return PatchStatus.NotFound;
            }

            var namedIndent = IndentForSpan(source, lineStarts, expected.ListStart, fileUnit);
            var namedRendered = CsStringLiteral.Render(newContent, namedIndent, eol);
            newSource = Splice(source, expected.ListStart, expected.ListStart, $"{parameterName}: {namedRendered}, ");
            return PatchStatus.Applied;
        }

        var argText = source.Substring(expected.Start, expected.End - expected.Start);
        if (argText == "null")
        {
            if (alreadyOnly)
            {
                failReason = StaleReason(lineHint);
                return PatchStatus.NotFound;
            }

            var rendered = RenderArgument(source, lineStarts, expected.Start, newContent, eol, fileUnit);
            newSource = Splice(source, expected.Start, expected.End, rendered);
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
    /// Where the expected argument of a call is, and in what shape. Worked out once because the
    /// content search and the insert path need the same answer: one compares the span, the other
    /// decides from the shape what to splice.
    /// </summary>
    readonly struct ExpectedArgument(int start, int end, int listStart, bool blockedByName)
    {
        /// <summary>
        /// Start of the argument, past any <c>expected:</c> name.
        /// </summary>
        public int Start { get; } = start;

        public int End { get; } = end;

        /// <summary>
        /// Start of the first argument, before any argument name.
        /// </summary>
        public int ListStart { get; } = listStart;

        /// <summary>
        /// The first argument is named, and is not the expected one, so an expected argument has
        /// to be inserted in front of it.
        /// </summary>
        public bool BlockedByName { get; } = blockedByName;

        /// <summary>
        /// The argument was left to its default.
        /// </summary>
        public bool IsAbsent => Start == End;

        /// <summary>
        /// True when the argument is character for character the given expression.
        /// </summary>
        public bool Matches(string source, string expression) =>
            !IsAbsent &&
            !BlockedByName &&
            End - Start == expression.Length &&
            string.CompareOrdinal(source, Start, expression, 0, expression.Length) == 0;
    }

    static bool TryReadArguments(string source, CsScan scan, int openParen, out ExpectedArgument expected)
    {
        expected = default;
        if (!TryScanArguments(source, scan, openParen, out var closeParen, out var topCommas))
        {
            return false;
        }

        // expected is the first parameter of Snapshot, so the argument to read is the first one
        var start = openParen + 1;
        var end = topCommas.Count > 0 ? topCommas[0] : closeParen;
        TrimSpan(source, scan, ref start, ref end);
        var listStart = start;
        var blockedByName = start != end &&
                            TryStripArgumentName(source, ref start, out var argumentName) &&
                            argumentName != parameterName;
        expected = new(start, end, listStart, blockedByName);
        return true;
    }

    /// <summary>
    /// Appends a Snapshot call to the verify invocation, for a snapshot that has never been
    /// accepted. Snapshot terminates the chain, so the insertion point is the end of any calls
    /// already chained onto the invocation rather than the invocation's own closing paren.
    /// </summary>
    static PatchStatus TryAppend(
        string source,
        CsScan scan,
        List<int> lineStarts,
        int lineHint,
        string newContent,
        string eol,
        string fileUnit,
        ref string newSource,
        ref string failReason)
    {
        if (!TryFindCall(source, scan, lineStarts, lineHint, verifyPrefix, true, out var nameStart, out var openParen))
        {
            failReason = $"Could not find a {verifyPrefix} call near line {lineHint}. The source may have changed since the test run. Re-run the test.";
            return PatchStatus.NotFound;
        }

        if (!TryScanArguments(source, scan, openParen, out var closeParen, out _))
        {
            failReason = $"Could not parse the argument list of the {verifyPrefix} call near line {lineHint}.";
            return PatchStatus.NotFound;
        }

        var insertAt = WalkChain(source, scan, closeParen + 1, methodName, out var alreadyChained);
        // Another process may have appended one between the run and the accept
        if (alreadyChained)
        {
            failReason = $"The call near line {lineHint} already has a {methodName} call. Re-run the test.";
            return PatchStatus.NotFound;
        }

        var statementIndent = LeadingWhitespace(source, lineStarts, nameStart);
        var unit = UnitFor(fileUnit, statementIndent);
        // Line up with the existing chain when there is one, otherwise start it one level in
        var callIndent = LineOf(lineStarts, insertAt - 1) == LineOf(lineStarts, nameStart)
            ? statementIndent + unit
            : LeadingWhitespace(source, lineStarts, insertAt - 1);
        var contentIndent = callIndent + unit;
        var rendered = CsStringLiteral.Render(newContent, contentIndent, eol);
        var argument = OnOwnLine(rendered, contentIndent, eol);
        newSource = Splice(source, insertAt, insertAt, $"{eol}{callIndent}.{methodName}({argument})");
        return PatchStatus.Applied;
    }

    /// <summary>
    /// Removes the Snapshot call, along with the whitespace and line break that preceded it so no
    /// blank line is left behind.
    /// </summary>
    static PatchStatus TryRemove(
        string source,
        CsScan scan,
        List<int> lineStarts,
        int lineHint,
        ref string newSource,
        ref string failReason)
    {
        if (!TryFindCall(source, scan, lineStarts, lineHint, methodName, false, out var nameStart, out var openParen))
        {
            failReason = $"Could not find a {methodName} call near line {lineHint}. The source may have changed since the test run. Re-run the test.";
            return PatchStatus.NotFound;
        }

        if (!TryScanArguments(source, scan, openParen, out var closeParen, out _))
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
        var dotStart = start;
        // Then back over the indentation and line break it sat on
        while (start > 0 &&
               (source[start - 1] == ' ' || source[start - 1] == '\t'))
        {
            start--;
        }

        if (start > 0 &&
            source[start - 1] == '\n')
        {
            var lineBreak = start - 1;
            if (lineBreak > 0 &&
                source[lineBreak - 1] == '\r')
            {
                lineBreak--;
            }

            // Not when the line above ends in a line comment: pulling the call up would take the
            // semicolon that follows it into the comment
            start = scan.IsCode(lineBreak) ? lineBreak : dotStart;
        }

        newSource = Splice(source, start, closeParen + 1, "");
        return PatchStatus.Applied;
    }

    /// <summary>
    /// Walks the calls chained onto an invocation and returns the end of the chain.
    /// <paramref name="found"/> is set when one of them is a call to <paramref name="name"/>.
    /// </summary>
    static int WalkChain(string source, CsScan scan, int index, string name, out bool found)
    {
        found = false;
        while (true)
        {
            var cursor = index;
            scan.SkipTrivia(ref cursor);
            if (cursor >= source.Length ||
                source[cursor] != '.')
            {
                return index;
            }

            cursor++;
            scan.SkipTrivia(ref cursor);

            var nameStart = cursor;
            while (cursor < source.Length &&
                   CsScan.IsIdentifierChar(source[cursor]))
            {
                cursor++;
            }

            if (cursor == nameStart ||
                !TrySkipToParen(source, scan, cursor, out var paren) ||
                !TryScanArguments(source, scan, paren, out var closeParen, out _))
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

    static bool TryFindCall(string source, CsScan scan, List<int> lineStarts, int lineHint, out int openParen) =>
        TryFindCall(source, scan, lineStarts, lineHint, methodName, false, out _, out openParen);

    static bool TryFindCall(
        string source,
        CsScan scan,
        List<int> lineStarts,
        int lineHint,
        string name,
        bool byPrefix,
        out int nameStart,
        out int openParen)
    {
        foreach (var call in FindCalls(source, scan, lineStarts, lineHint, name, byPrefix))
        {
            (nameStart, openParen) = call;
            return true;
        }

        nameStart = -1;
        openParen = -1;
        return false;
    }

    /// <summary>
    /// Locates calls by name, searching outward from the hint: hint, hint+1, hint-1, hint+2 and so
    /// on. A tie goes to the line at or after the hint, because a file that moved under a pending
    /// patch usually grew above the call rather than below it.
    /// <paramref name="byPrefix"/> matches any identifier starting with <paramref name="name"/>,
    /// which is how the several Verify overloads are found with one search.
    /// </summary>
    static IEnumerable<(int nameStart, int openParen)> FindCalls(
        string source,
        CsScan scan,
        List<int> lineStarts,
        int lineHint,
        string name,
        bool byPrefix)
    {
        var lineCount = lineStarts.Count;
        lineHint = Math.Min(Math.Max(lineHint, 1), lineCount);
        for (var distance = 0; distance < lineCount; distance++)
        {
            var candidates = distance == 0
                ? new[] { lineHint }
                : new[] { lineHint + distance, lineHint - distance };
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
                               CsScan.IsIdentifierChar(source[identifierEnd]))
                        {
                            identifierEnd++;
                        }
                    }
                    else if (identifierEnd < source.Length &&
                             CsScan.IsIdentifierChar(source[identifierEnd]))
                    {
                        index += name.Length;
                        continue;
                    }

                    // In code, a whole token, an invocation rather than a declaration, and
                    // followed by an argument list. A commented out example passes none of these
                    if (scan.IsCode(index) &&
                        StartsToken(source, index) &&
                        !scan.IsDeclaration(index) &&
                        !(byPrefix && IsForeignReceiver(source, scan, index)) &&
                        TrySkipToParen(source, scan, identifierEnd, out var paren))
                    {
                        yield return (index, paren);
                    }

                    index += name.Length;
                }
            }
        }
    }

    static bool StartsToken(string source, int index) =>
        index == 0 ||
        !CsScan.IsIdentifierChar(source[index - 1]);

    /// <summary>
    /// True when the name is reached through a member access on anything other than the verify
    /// entry point class. Only used for the prefix search, where the name is a guess at a verify
    /// entry point rather than something already known to be one.
    /// <para>
    /// Verify is an ordinary enough name that a project has its own: ContentValidation.Verify,
    /// validator.Verify, mock.VerifyAll. Those read exactly like an entry point to a token scan,
    /// and appending a Snapshot call to one splices the snapshot into a call that never produced
    /// it, in a test that may not even be the one the patch came from.
    /// </para>
    /// </summary>
    static bool IsForeignReceiver(string source, CsScan scan, int nameStart)
    {
        var dot = scan.PreviousSignificant(nameStart);
        if (dot < 0 ||
            source[dot] != '.')
        {
            // Unqualified: a static using, or inherited from VerifyBase
            return false;
        }

        var end = scan.PreviousSignificant(dot);
        if (end >= 0 &&
            source[end] == '?')
        {
            end = scan.PreviousSignificant(end);
        }

        if (end < 0 ||
            !CsScan.IsIdentifierChar(source[end]))
        {
            // Not a plain receiver, so a literal, an indexer or a call result
            return true;
        }

        var start = end;
        while (start > 0 &&
               CsScan.IsIdentifierChar(source[start - 1]))
        {
            start--;
        }

        var receiver = source.Substring(start, end - start + 1);
        return receiver != verifierType &&
               receiver != "this";
    }

    static bool TrySkipToParen(string source, CsScan scan, int index, out int paren)
    {
        paren = -1;
        scan.SkipTrivia(ref index);
        if (index < source.Length &&
            source[index] == '<' &&
            CsScan.TrySkipTypeArguments(source, ref index))
        {
            scan.SkipTrivia(ref index);
        }

        if (index < source.Length &&
            source[index] == '(')
        {
            paren = index;
            return true;
        }

        return false;
    }

    // Scans a balanced argument list starting at the open paren.
    // Records top level comma positions. Comments and literals are stepped over whole.
    static bool TryScanArguments(string source, CsScan scan, int openParen, out int closeParen, out List<int> topCommas)
    {
        closeParen = -1;
        topCommas = [];
        var depth = 1;
        var index = openParen + 1;
        while (index < source.Length)
        {
            if (scan.TryGetSkip(index, out var skipTo))
            {
                index = skipTo;
                continue;
            }

            switch (source[index])
            {
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

    static bool TryStripArgumentName(string source, ref int start, out string name)
    {
        name = "";
        var index = start;
        if (index >= source.Length || !char.IsLetter(source[index]) && source[index] != '_')
        {
            return false;
        }

        while (index < source.Length && CsScan.IsIdentifierChar(source[index]))
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

    /// <summary>
    /// Narrows a span to the expression in it: whitespace and comments are not part of the
    /// argument, and leaving a comment in makes the argument read as something other than the
    /// literal it is.
    /// </summary>
    static void TrimSpan(string source, CsScan scan, ref int start, ref int end)
    {
        while (start < end)
        {
            if (char.IsWhiteSpace(source[start]))
            {
                start++;
                continue;
            }

            if (source[start] == '/' &&
                scan.TryGetSkip(start, out var afterComment) &&
                afterComment <= end)
            {
                start = afterComment;
                continue;
            }

            break;
        }

        while (end > start)
        {
            if (char.IsWhiteSpace(source[end - 1]))
            {
                end--;
                continue;
            }

            if (scan.TryGetCommentEndingAt(end, out var commentStart) &&
                commentStart >= start)
            {
                end = commentStart;
                continue;
            }

            break;
        }
    }

    /// <summary>
    /// Renders the literal for a splice at <paramref name="spanStart"/>, indented to suit where it
    /// lands.
    /// </summary>
    static string RenderArgument(string source, List<int> lineStarts, int spanStart, string newContent, string eol, string fileUnit)
    {
        var indent = IndentForSpan(source, lineStarts, spanStart, fileUnit);
        var rendered = CsStringLiteral.Render(newContent, indent, eol);
        if (StartsLine(source, lineStarts, spanStart))
        {
            return rendered;
        }

        return OnOwnLine(rendered, indent, eol);
    }

    /// <summary>
    /// Puts a raw literal on its own line rather than trailing the open paren, so its opening
    /// delimiter sits with its content and its closing one. A regular literal stays where it is,
    /// since it has nothing to line up with.
    /// </summary>
    static string OnOwnLine(string rendered, string indent, string eol) =>
        rendered.IndexOf('\n') == -1 ? rendered : $"{eol}{indent}{rendered}";

    /// <summary>
    /// True when only whitespace precedes the offset on its line.
    /// </summary>
    static bool StartsLine(string source, List<int> lineStarts, int offset)
    {
        for (var index = lineStarts[LineOf(lineStarts, offset) - 1]; index < offset; index++)
        {
            if (source[index] != ' ' &&
                source[index] != '\t')
            {
                return false;
            }
        }

        return true;
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

    /// <summary>
    /// What one level of indentation is made of in this file: the most common run of whitespace a
    /// line adds to the one above it.
    /// <para>
    /// Read off the source rather than taken from a convention, because a splice has to match the
    /// code it lands in, and files disagree with their repo's settings often enough - vendored,
    /// generated, or last edited by someone configured differently - that following the convention
    /// would make the patch look more out of place, not less. It answers the one question a single
    /// call site cannot: a line shows which characters it is indented with, but not how wide a
    /// level is, and hard coding four spaces is wrong in every two space repo.
    /// </para>
    /// Returns "" when the file is too small to show a step, which leaves the choice to
    /// <see cref="UnitFor"/>.
    /// </summary>
    static string DetectIndentUnit(string source, CsScan scan, List<int> lineStarts)
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        var previous = "";
        foreach (var lineStart in lineStarts)
        {
            // Inside a comment or a literal the leading whitespace is content, not indentation.
            // A snapshot literal in particular is arbitrary text, and counting its lines would
            // measure the snapshot rather than the file
            if (!scan.IsCode(lineStart))
            {
                continue;
            }

            var index = lineStart;
            while (index < source.Length &&
                   (source[index] == ' ' || source[index] == '\t'))
            {
                index++;
            }

            // A blank line has no indentation of its own, and must not break the run either
            if (index >= source.Length ||
                source[index] == '\r' ||
                source[index] == '\n')
            {
                continue;
            }

            var lead = source.Substring(lineStart, index - lineStart);
            // Only a line that indents further than the one above, by adding to what it already
            // had. Anything else is a dedent, or whitespace of a different kind, and neither
            // measures a step
            if (lead.Length > previous.Length &&
                lead.StartsWith(previous, StringComparison.Ordinal))
            {
                var step = lead.Substring(previous.Length);
                counts.TryGetValue(step, out var count);
                counts[step] = count + 1;
            }

            previous = lead;
        }

        var best = "";
        var bestCount = 0;
        foreach (var pair in counts)
        {
            if (bestCount == 0 ||
                pair.Value > bestCount ||
                pair.Value == bestCount && Closer(pair.Key, best))
            {
                best = pair.Key;
                bestCount = pair.Value;
            }
        }

        return best;

        // A tie goes to the shorter step, since a longer one is two levels taken at once, and
        // then to ordinal order so the answer cannot depend on enumeration order
        static bool Closer(string candidate, string current) =>
            candidate.Length == current.Length
                ? string.CompareOrdinal(candidate, current) < 0
                : candidate.Length < current.Length;
    }

    /// <summary>
    /// One level of indentation for a splice at a site indented with <paramref name="lead"/>.
    /// <para>
    /// The character comes from the site and the width from the file, so a file that indents
    /// inconsistently still gets a splice consistent with its own surroundings, while a file that
    /// indents by something other than four spaces gets that.
    /// </para>
    /// </summary>
    static string UnitFor(string fileUnit, string lead)
    {
        var fileUsesTabs = fileUnit.Length > 0 && fileUnit[0] == '\t';
        // The character the site's own indentation ends in decides, so tabs for depth followed by
        // spaces for alignment continues in spaces: a tab there would advance to the next tab stop
        // from wherever the alignment left off, which is a different width in every editor. With
        // no indentation to read, follow the file
        var tabs = lead.Length > 0 ? lead[lead.Length - 1] == '\t' : fileUsesTabs;
        if (tabs)
        {
            return "\t";
        }

        // The file's step is tabs, or there was none to find, so it says nothing about how wide a
        // space indent should be
        if (fileUsesTabs ||
            fileUnit.Length == 0)
        {
            return "    ";
        }

        return fileUnit;
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

    static string IndentForSpan(string source, List<int> lineStarts, int spanStart, string fileUnit)
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
        return leadText + UnitFor(fileUnit, leadText);
    }
}
