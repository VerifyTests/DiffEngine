enum PatchStatus
{
    Applied,
    AlreadyApplied,
    NotFound
}

/// <summary>
/// Pure string in / string out engine that locates an inline snapshot call site in source and
/// splices in a new string literal. No file IO.
/// <para>
/// The structure it walks - a name, an argument list, a chain of calls hung off it - is the same
/// in every language it patches, so only what a literal looks like, what a comment looks like, and
/// what tells a declaration from a call is per language. All of that lives on
/// <see cref="SourceLanguage"/>, reached through the scan.
/// </para>
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
    /// Every entry point the adapters expose, by name.
    /// <para>
    /// The throwing ones are entry points like any other - they return a SettingsTask, so a
    /// Snapshot call chains onto them exactly the same way - and a test whose whole subject is the
    /// exception is the kind that most wants its snapshot inline. Searching for Verify alone left
    /// every one of them unpatchable: the snapshot was declared inline, the append silently found
    /// nothing, and the verified file was deleted out from under it.
    /// </para>
    /// <para>
    /// Names, rather than the Verify and Throws prefixes this used to match on. A prefix that
    /// ordinary reaches things that are no such thing: a mock's VerifyAll, or a test project's own
    /// <c>Task VerifyDocx(...)</c> wrapper around a verify call. Appending to one of those writes
    /// source that does not compile, there being no SettingsTask to chain onto, and nothing
    /// readable from the source says which it is. So the producer says instead: a wrapper that
    /// returns a SettingsTask declares itself an entry point and arrives on the patch, through
    /// <see cref="InlinePatch.EntryPoints"/>.
    /// </para>
    /// </summary>
    static string[] builtInEntryPoints =
    [
        "Verify",
        "VerifyJson",
        "VerifyXml",
        "VerifyFile",
        "VerifyFiles",
        "VerifyDirectory",
        "VerifyZip",
        "VerifyTuple",
        // Not a verify call itself: it opens the chain that ends in one, and the caller info is
        // captured here, so this is the line the hint names and the call the append hangs off
        "Combination",
        "Throws",
        "ThrowsTask",
        "ThrowsValueTask"
    ];

    /// <summary>
    /// How the entry points are named in a message, since there is no longer one of them.
    /// </summary>
    const string entryPointDescription = "verify entry point";

    /// <summary>
    /// The built-in entry points, plus whatever the patch declared. Deduplicated, since a name
    /// searched for twice finds the same call twice and the second one is pure work.
    /// </summary>
    static string[] EntryPoints(string[]? declared)
    {
        if (declared is null ||
            declared.Length == 0)
        {
            return builtInEntryPoints;
        }

        var names = new List<string>(builtInEntryPoints);
        foreach (var name in declared)
        {
            // An empty name matches everywhere and advances nothing, so the search for it never
            // ended - while holding the file's mutex. "VerifyDocx," arrives as one from a payload
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!names.Contains(name, StringComparer.Ordinal))
            {
                names.Add(name);
            }
        }

        return names.ToArray();
    }

    /// <summary>
    /// The only receiver a verify entry point is reached through. Every adapter exposes the entry
    /// points unqualified (a static using, or inherited from VerifyBase) or on this one class.
    /// </summary>
    const string verifierType = "Verifier";

    public static PatchStatus TryApply(
        SourceLanguage language,
        string source,
        int lineHint,
        InlinePatchMode mode,
        string? originalExpression,
        string? originalValue,
        string? memberName,
        string[]? entryPoints,
        bool anchorOnly,
        string newContent,
        out string newSource,
        out string failReason)
    {
        newSource = "";
        failReason = "";
        var eol = DetectEol(source);
        var lineStarts = BuildLineStarts(source);
        // Everything that reads the scan does so before this returns, the searches that are
        // enumerated lazily included, so its map goes back to the pool on the way out
        using var scan = language.Scan(source);
        var memberLine = MemberLine(source, scan, lineStarts, lineHint, memberName);

        if (mode == InlinePatchMode.Remove)
        {
            return TryRemove(language, source, scan, lineStarts, lineHint, memberLine, EntryPoints(entryPoints), originalExpression, originalValue, eol, ref newSource, ref failReason);
        }

        var fileUnit = DetectIndentUnit(source, scan, lineStarts);

        if (mode == InlinePatchMode.Append)
        {
            return TryAppend(source, scan, lineStarts, lineHint, memberLine, EntryPoints(entryPoints), anchorOnly, newContent, eol, fileUnit, ref newSource, ref failReason);
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
            var appliedAtHint = false;
            foreach (var (nameStart, openParen) in FindCalls(source, scan, lineStarts, lineHint, memberLine, snapshotName, false))
            {
                var onHint = IsOnHint(lineStarts, nameStart, lineHint);
                if (appliedAtHint &&
                    !onHint)
                {
                    return PatchStatus.AlreadyApplied;
                }

                if (!TryReadArguments(source, scan, openParen, out var expected) ||
                    !expected.Matches(source, needle))
                {
                    appliedAtHint |= onHint && HoldsContent(source, scan, openParen, newContent);
                    continue;
                }

                if (language.TryParse(needle, out var oldValue) &&
                    oldValue == newContent)
                {
                    return PatchStatus.AlreadyApplied;
                }

                var rendered = RenderArgument(source, scan, lineStarts, nameStart, expected.Start, newContent, eol, fileUnit);
                newSource = Splice(source, expected.Start, expected.End, rendered);
                return PatchStatus.Applied;
            }

            // Expression gone: another process may have applied the same patch already
            return InsertOrCheck(source, scan, lineStarts, lineHint, memberLine, newContent, eol, fileUnit, alreadyOnly: true, ref newSource, ref failReason);
        }

        if (originalValue != null)
        {
            // Located by content again, but by what the argument means rather than by what it
            // says. The same anchor for a producer whose language withholds the expression, and
            // the same outcome when nothing matches: report, rather than rewrite whichever call
            // the hint happens to land on.
            var previous = SourceLanguage.NormalizeNewlines(originalValue);
            var appliedAtHint = false;
            foreach (var (nameStart, openParen) in FindCalls(source, scan, lineStarts, lineHint, memberLine, snapshotName, false))
            {
                var onHint = IsOnHint(lineStarts, nameStart, lineHint);
                if (appliedAtHint &&
                    !onHint)
                {
                    return PatchStatus.AlreadyApplied;
                }

                if (!TryReadArguments(source, scan, openParen, out var expected) ||
                    expected.IsAbsent ||
                    expected.BlockedByName)
                {
                    continue;
                }

                var argument = source.Substring(expected.Start, expected.End - expected.Start);
                if (!language.TryParse(argument, out var value) ||
                    value != previous)
                {
                    appliedAtHint |= onHint && value == newContent;
                    continue;
                }

                if (previous == newContent)
                {
                    return PatchStatus.AlreadyApplied;
                }

                var rendered = RenderArgument(source, scan, lineStarts, nameStart, expected.Start, newContent, eol, fileUnit);
                newSource = Splice(source, expected.Start, expected.End, rendered);
                return PatchStatus.Applied;
            }

            return InsertOrCheck(source, scan, lineStarts, lineHint, memberLine, newContent, eol, fileUnit, alreadyOnly: true, ref newSource, ref failReason);
        }

        return InsertOrCheck(source, scan, lineStarts, lineHint, memberLine, newContent, eol, fileUnit, alreadyOnly: false, ref newSource, ref failReason);
    }

    /// <summary>
    /// Whether a call is on the recorded line, which <see cref="FindCalls"/> yields before anything
    /// else and never again.
    /// <para>
    /// It matters to the content search above because a patch can arrive a second time after it
    /// has been applied: a second target framework's identical patch reaching the queue after the
    /// first was accepted, or each framework's test process applying the same Remove. The anchor
    /// has gone from the call it named by then, so the search went looking for it elsewhere - and a
    /// sibling holding the same literal, which is ordinary for a member verifying two values that
    /// serialise alike, is exactly where it found it, and rewrote that one. So once the call at
    /// the recorded line turns out to already hold what the patch would write, the search stops
    /// there and reports it done, rather than carrying on to the next call that matches.
    /// </para>
    /// </summary>
    static bool IsOnHint(List<int> lineStarts, int nameStart, int lineHint) =>
        LineOf(lineStarts, nameStart) == Clamp(lineHint, lineStarts.Count);

    static PatchStatus InsertOrCheck(
        string source,
        SourceScan scan,
        List<int> lineStarts,
        int lineHint,
        int? memberLine,
        string newContent,
        string eol,
        string fileUnit,
        bool alreadyOnly,
        ref string newSource,
        ref string failReason)
    {
        if (!TryFindCall(source, scan, lineStarts, lineHint, memberLine, snapshotName, false, out var nameStart, out var openParen))
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

            var emptyRendered = RenderArgument(source, scan, lineStarts, nameStart, expected.Start, newContent, eol, fileUnit);
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

            var namedIndent = IndentForSpan(source, scan, lineStarts, nameStart, expected.ListStart, fileUnit);
            var namedRendered = scan.Language.Render(newContent, namedIndent, eol);
            newSource = Splice(source, expected.ListStart, expected.ListStart, $"{scan.Language.NamePrefix(parameterName)}{namedRendered}, ");
            return PatchStatus.Applied;
        }

        var argText = source.Substring(expected.Start, expected.End - expected.Start);
        // Both ways of writing "no snapshot here yet". They are placeholders rather than content,
        // and a producer sends no expression for either: a bare token is far too common in a file
        // for a content search to land anywhere meaningful
        if (argText is "null" or "default")
        {
            if (alreadyOnly)
            {
                failReason = StaleReason(lineHint);
                return PatchStatus.NotFound;
            }

            var rendered = RenderArgument(source, scan, lineStarts, nameStart, expected.Start, newContent, eol, fileUnit);
            newSource = Splice(source, expected.Start, expected.End, rendered);
            return PatchStatus.Applied;
        }

        if (scan.Language.TryParse(argText, out var currentValue))
        {
            if (currentValue == newContent)
            {
                return PatchStatus.AlreadyApplied;
            }

            if (!alreadyOnly &&
                !scan.Language.SuppliesArgumentExpressions)
            {
                // A differing literal is a snapshot that changed, and this is the only shape a
                // changed one arrives in from a language with no expression to anchor on. Refusing
                // it there would mean an inline snapshot could be accepted once and never updated
                var rendered = RenderArgument(source, scan, lineStarts, nameStart, expected.Start, newContent, eol, fileUnit);
                newSource = Splice(source, expected.Start, expected.End, rendered);
                return PatchStatus.Applied;
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
        /// True when the argument is the given expression, character for character or once both
        /// have had their newlines normalised.
        /// <para>
        /// The needle arrives normalised to the file's dominant line ending, which is the right
        /// thing to write but the wrong thing to search for: a literal whose own lines use the
        /// other ending is the same expression and did not match, so the snapshot could not be
        /// patched at all. Mixed endings inside one file are ordinary - a merge, an editor that
        /// only fixes what it touches, a generator.
        /// </para>
        /// </summary>
        public bool Matches(string source, string expression)
        {
            if (IsAbsent ||
                BlockedByName)
            {
                return false;
            }

            if (End - Start == expression.Length &&
                string.CompareOrdinal(source, Start, expression, 0, expression.Length) == 0)
            {
                return true;
            }

            var argument = source.Substring(Start, End - Start);
            return SourceLanguage.NormalizeNewlines(argument) ==
                   SourceLanguage.NormalizeNewlines(expression);
        }
    }

    static bool TryReadArguments(string source, SourceScan scan, int openParen, out ExpectedArgument expected)
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
                            scan.Language.TryStripArgumentName(source, ref start, out var argumentName) &&
                            argumentName != parameterName;
        expected = new(start, end, listStart, blockedByName);
        return true;
    }

    /// <summary>
    /// Appends a Snapshot call to the verify invocation, for a snapshot that has never been
    /// accepted. Snapshot terminates the chain, so the insertion point is the end of any calls
    /// already chained onto the invocation rather than the invocation's own closing paren - except
    /// where the chain ends in something Snapshot has to precede, which <see cref="WalkChain"/>
    /// answers.
    /// <para>
    /// The call is the first entry point the search yields that has no Snapshot call chained onto
    /// it, not the first it yields. A hint goes stale the moment an accept higher in the file
    /// inserts a literal, and the walk then starts over from the member's declaration - so the
    /// first call it meets is the first in the test, which is the one most likely to have been
    /// accepted already. Stopping there answered "already has a Snapshot call" for a patch whose
    /// own call sat two lines further down, and a single accept dropped the entry.
    /// </para>
    /// <para>
    /// And it has to be the only one, where the recorded line names no call. An append carries no
    /// anchor, so among several calls with no Snapshot call nothing says which it was for, and the
    /// first was taken: a call verified through files that came earlier in the test was given the
    /// snapshot of the one after it. That is refused now, and a re-run brings the line the call
    /// is on. A call passed a name Snapshot is called on (<see cref="TakesASnapshotReceiver"/>)
    /// has its snapshot and is not one of them.
    /// </para>
    /// </summary>
    static PatchStatus TryAppend(
        string source,
        SourceScan scan,
        List<int> lineStarts,
        int lineHint,
        int? memberLine,
        string[] entryPoints,
        bool anchorOnly,
        string newContent,
        string eol,
        string fileUnit,
        ref string newSource,
        ref string failReason)
    {
        var found = false;
        // Whether a call passed over for having a Snapshot call was holding this very content
        var held = false;
        List<(int Open, int Close)>? passedOver = null;
        // The call to append to, once the recorded line has turned out not to name one, and the
        // names Snapshot is called on in the member, read when the first such call is met
        (int NameStart, int InsertAt)? taken = null;
        HashSet<string>? snapshotReceivers = null;
        foreach (var (nameStart, openParen) in FindCalls(source, scan, lineStarts, lineHint, memberLine, entryPoints, true))
        {
            // An entry point in the argument list of a call that was passed over is part of that
            // call. Throws(() => Verify(value)).Snapshot(...) has its Snapshot, and the Verify
            // inside it is not a second place to hang one
            if (passedOver is not null &&
                passedOver.Any(_ => nameStart > _.Open && nameStart < _.Close))
            {
                continue;
            }

            found = true;
            if (!TryScanArguments(source, scan, openParen, out var closeParen, out var commas))
            {
                // One past the call that was taken is not this patch's to report
                if (taken is not null)
                {
                    break;
                }

                failReason = $"Could not parse the argument list of the {entryPointDescription} call near line {lineHint}.";
                return PatchStatus.NotFound;
            }

            // Everything a call site needs to host a snapshot has now been established, which is
            // all an anchor probe asked
            if (anchorOnly)
            {
                return PatchStatus.Applied;
            }

            var insertAt = WalkChain(source, scan, closeParen + 1, methodName, out var chained);
            if (chained < 0)
            {
                // The recorded line names its call, and with no member there is nothing to say
                // how far the walk may go for another
                if (memberLine is null ||
                    IsOnHint(lineStarts, nameStart, lineHint))
                {
                    newSource = AppendCall(source, scan, lineStarts, nameStart, insertAt, newContent, eol, fileUnit);
                    return PatchStatus.Applied;
                }

                passedOver ??= [];
                passedOver.Add((openParen, closeParen));
                snapshotReceivers ??= SnapshotReceivers(source, scan, lineStarts, memberLine.Value);
                if (TakesASnapshotReceiver(source, scan, openParen, closeParen, commas, snapshotReceivers))
                {
                    continue;
                }

                if (taken is not null)
                {
                    failReason = $"The call has moved from line {lineHint}, and more than one {entryPointDescription} call in its test has no {methodName} call. Re-run the test.";
                    return PatchStatus.NotFound;
                }

                taken = (nameStart, insertAt);
                continue;
            }

            // Past the call that was taken, the walk is only looking for a second one like it
            if (taken is not null)
            {
                passedOver ??= [];
                passedOver.Add((openParen, closeParen));
                continue;
            }

            held |= HoldsContent(source, scan, chained, newContent);

            // Two things end the search at a call that has one. The recorded line: a hint that
            // lands on a call names it, and a Snapshot call already there holding other content is
            // another framework's accept of the same call site, so carrying on would hang this
            // snapshot on the test's next verify call instead. And having no member: nothing bounds
            // the walk then, and the next call without one is as likely to be in another test
            if (memberLine is null ||
                IsOnHint(lineStarts, nameStart, lineHint))
            {
                break;
            }

            passedOver ??= [];
            passedOver.Add((openParen, closeParen));
        }

        if (taken is { } call)
        {
            newSource = AppendCall(source, scan, lineStarts, call.NameStart, call.InsertAt, newContent, eol, fileUnit);
            return PatchStatus.Applied;
        }

        if (!found)
        {
            // Short, because every surface that shows it is one line: a status bar, a balloon, a
            // menu tooltip. Both clauses earn their place there because they are the two causes a
            // reader cannot deduce from looking at the line the message names
            failReason = $"No {entryPointDescription} call at line {lineHint}. One reached through a receiver of its own does not count, nor a wrapper that AddInlineEntryPoint has not registered.";
            return PatchStatus.NotFound;
        }

        // Every call that could have taken it has a Snapshot call. Another process may have
        // appended one between the run and the accept, and two frameworks failing the same call
        // site is the ordinary way that happens: each queues an append, and accepting the first
        // leaves the second with nowhere to put a literal that is already there. Only the content
        // tells the two apart. The same snapshot is done, and saying so matters - a refusal reads
        // as a failure, and the reader who sent two identical snapshots and got one applied and
        // one rejected has no way to see that their source is already right. A different one is a
        // call site that cannot say what it wants until it has been re-run against the literal it
        // now has.
        if (held)
        {
            return PatchStatus.AlreadyApplied;
        }

        failReason = $"The call near line {lineHint} already has a {methodName} call. Re-run the test.";
        return PatchStatus.NotFound;
    }

    /// <summary>
    /// The names a Snapshot call is made on in the member declared at
    /// <paramref name="memberLine"/>: <c>settings</c> for <c>settings.Snapshot("old");</c>. Plain
    /// names only, since that is all <see cref="TakesASnapshotReceiver"/> can match an argument to.
    /// </summary>
    static HashSet<string> SnapshotReceivers(string source, SourceScan scan, List<int> lineStarts, int memberLine)
    {
        var receivers = new HashSet<string>(StringComparer.Ordinal);
        var lineCount = lineStarts.Count;
        var floor = Clamp(memberLine, lineCount);
        var ceiling = Math.Min(NextMemberLine(source, scan, lineStarts, floor), lineCount + 1);
        for (var line = floor; line < ceiling; line++)
        {
            foreach (var (nameStart, _) in CallsOnLine(source, scan, lineStarts, line, snapshotName, false))
            {
                var dot = PreviousToken(source, scan, nameStart);
                if (dot < 0 ||
                    source[dot] != '.')
                {
                    continue;
                }

                var end = PreviousToken(source, scan, dot);
                if (end < 0 ||
                    !scan.IsCode(end) ||
                    !scan.IsIdentifierChar(source[end]))
                {
                    continue;
                }

                var start = scan.WordStart(end);
                var before = PreviousToken(source, scan, start);
                if (before >= 0 &&
                    source[before] == '.')
                {
                    continue;
                }

                receivers.Add(source.Substring(start, end + 1 - start));
            }
        }

        return receivers;
    }

    /// <summary>
    /// Whether a call is passed something a Snapshot call is made on in the same member, which is
    /// a verification that has its snapshot already: <c>settings.Snapshot("old");</c> and then
    /// <c>Verify(value, settings)</c>.
    /// <para>
    /// Asked of a call the recorded line did not name. A patch that appends is for a call with no
    /// snapshot anywhere, so this one is not it, and with nothing chained onto it, it read as the
    /// first call still wanting one.
    /// </para>
    /// </summary>
    static bool TakesASnapshotReceiver(string source, SourceScan scan, int openParen, int closeParen, List<int> commas, HashSet<string> receivers)
    {
        if (receivers.Count == 0)
        {
            return false;
        }

        var start = openParen + 1;
        for (var index = 0; index <= commas.Count; index++)
        {
            var end = index < commas.Count ? commas[index] : closeParen;
            var argumentStart = start;
            var argumentEnd = end;
            start = end + 1;
            TrimSpan(source, scan, ref argumentStart, ref argumentEnd);
            if (argumentStart < argumentEnd)
            {
                scan.Language.TryStripArgumentName(source, ref argumentStart, out _);
            }

            if (argumentStart < argumentEnd &&
                scan.IsCode(argumentStart) &&
                receivers.Contains(source.Substring(argumentStart, argumentEnd - argumentStart)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The source with a Snapshot call holding <paramref name="newContent"/> spliced in at
    /// <paramref name="insertAt"/>, on a line of its own under the call at
    /// <paramref name="nameStart"/>.
    /// </summary>
    static string AppendCall(string source, SourceScan scan, List<int> lineStarts, int nameStart, int insertAt, string newContent, string eol, string fileUnit)
    {
        var statementIndent = StatementIndent(source, scan, lineStarts, nameStart);
        var unit = UnitFor(fileUnit, statementIndent);
        // Line up with the existing chain when there is one, otherwise start it one level in
        var callIndent = LineOf(lineStarts, insertAt - 1) == LineOf(lineStarts, nameStart)
            ? statementIndent + unit
            : LeadingWhitespace(source, lineStarts, insertAt - 1);
        if (scan.Language.IndentationIsSyntax)
        {
            // The line a chain ends on is not always one of its calls. A closing paren on a line
            // of its own sits at the column the expression started at, which is where a formatter
            // puts it, and an argument's last line may too. Lined up with either, the call is
            // read as the start of the next statement rather than as more of this one
            var expressionIndent = IndentTo(source, lineStarts, ExpressionStart(source, scan, nameStart));
            if (IndentWidth(callIndent) <= IndentWidth(expressionIndent))
            {
                callIndent = expressionIndent + unit;
            }
        }

        var contentIndent = callIndent + unit;
        var rendered = scan.Language.Render(newContent, contentIndent, eol);
        var argument = OnOwnLine(rendered, contentIndent, eol);
        return Splice(source, insertAt, insertAt, $"{eol}{callIndent}.{methodName}({argument})");
    }

    /// <summary>
    /// Whether the call at <paramref name="openParen"/> already carries
    /// <paramref name="content"/> as its expected argument.
    /// <para>
    /// What the argument means rather than what it says, so a literal the append would have
    /// written in another shape - a different delimiter, a different indent - still counts as the
    /// same snapshot. Anything that is not a literal at all, or is hidden behind another named
    /// argument, is not this content: no answer can be read out of it, and the caller's other
    /// branch says to re-run, which is where a call site nobody can make sense of belongs.
    /// </para>
    /// </summary>
    static bool HoldsContent(string source, SourceScan scan, int openParen, string content)
    {
        if (!TryReadArguments(source, scan, openParen, out var expected) ||
            expected.IsAbsent ||
            expected.BlockedByName)
        {
            return false;
        }

        var argument = source.Substring(expected.Start, expected.End - expected.Start);
        return scan.Language.TryParse(argument, out var value) &&
               value == content;
    }

    /// <summary>
    /// Removes the Snapshot call, along with the whitespace and line break that preceded it so no
    /// blank line is left behind, except over a line holding another Snapshot call, where one has
    /// to be (<see cref="KeepingItsLine"/>).
    /// <para>
    /// What it was called on stays, and that has to still be something once the call has gone. A
    /// verify call is. A variable is not: <c>settings.Snapshot("old");</c> became
    /// <c>settings;</c>, which is no statement (CS0201), and Snapshot is as public on a
    /// VerifySettings as on what a verify call returns. So there it is the statement that goes,
    /// where it can be taken whole (<see cref="TryStatementLines"/>), and the call is reported
    /// where it cannot.
    /// </para>
    /// <para>
    /// Only where the variable would be left as the statement, though. Awaited, assigned, returned
    /// or passed, it is still a value with the call gone (<see cref="IsTakenAsAValue"/>), and the
    /// statement reads as it would have run without the snapshot.
    /// </para>
    /// </summary>
    static PatchStatus TryRemove(
        SourceLanguage language,
        string source,
        SourceScan scan,
        List<int> lineStarts,
        int lineHint,
        int? memberLine,
        string[] entryPoints,
        string? originalExpression,
        string? originalValue,
        string eol,
        ref string newSource,
        ref string failReason)
    {
        if (RemovedAtHint(source, scan, lineStarts, lineHint, memberLine, entryPoints))
        {
            return PatchStatus.AlreadyApplied;
        }

        var anchored = !string.IsNullOrEmpty(originalExpression) || originalValue != null;
        if (!TryFindAnchoredCall(language, source, scan, lineStarts, lineHint, memberLine, originalExpression, originalValue, eol, out var nameStart, out var openParen))
        {
            failReason = anchored
                ? $"Could not find a {methodName} call near line {lineHint} whose expected argument is still the one the test run saw. The source may have changed since the test run. Re-run the test."
                : $"Could not find a {methodName} call near line {lineHint}. The source may have changed since the test run. Re-run the test.";
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
        if (LeavesOnlyItsReceiver(source, scan, dotStart, closeParen) &&
            !IsTakenAsAValue(source, scan, lineStarts, nameStart))
        {
            if (!TryStatementLines(source, scan, lineStarts, nameStart, closeParen, out var from, out var to))
            {
                failReason = $"Removing the {methodName} call near line {lineHint} would leave what it is called on as a statement by itself. Remove the statement by hand.";
                return PatchStatus.NotFound;
            }

            newSource = Splice(source, from, to, KeptStatementLine(source, scan, lineStarts, nameStart, from, to));
            return PatchStatus.Applied;
        }

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
            if (!scan.IsCode(lineBreak))
            {
                start = dotStart;
            }
            else if (SnapshotCallFollows(source, scan, lineStarts, closeParen))
            {
                newSource = KeepingItsLine(source, scan, lineBreak, start - 1, dotStart, closeParen);
                return PatchStatus.Applied;
            }
            else
            {
                start = lineBreak;
            }
        }

        newSource = Splice(source, start, closeParen + 1, "");
        return PatchStatus.Applied;
    }

    /// <summary>
    /// Whether the line under the one a call ends on holds a Snapshot call: the line that comes
    /// up onto the call's own when the call is taken out with its line.
    /// </summary>
    static bool SnapshotCallFollows(string source, SourceScan scan, List<int> lineStarts, int closeParen)
    {
        var next = LineOf(lineStarts, closeParen) + 1;
        return next <= lineStarts.Count &&
               CallsOnLine(source, scan, lineStarts, next, snapshotName, false).Any();
    }

    /// <summary>
    /// Takes out a call that started its line, back to the end of the line above, so what
    /// followed the call carries on from there, and leaves the line the call was on empty.
    /// <para>
    /// For a call with a Snapshot call on the line under it, and the empty line is for whoever
    /// applies the same Remove next: every framework of a multi-targeted run does, and each case
    /// of a test that ignores its parameters. With the lines under it pulled up, the recorded
    /// line came to hold that call, and where it had the same literal there was nothing to tell
    /// it from the one the patch was made for: the same line, the same anchor. It was taken for
    /// a call still to be removed, and a sibling lost its snapshot. The file as it then stood is
    /// the file an honest Remove of that sibling would meet, so nothing reading it afterwards
    /// can do better, and the line has to be kept. One line, where the call started, however
    /// many it ran over: that is the line a patch names, and <see cref="RemovedAtHint"/> reads
    /// one with no Snapshot call, under a verify statement with none, as that call removed.
    /// Anything else on the line under it is read that way already, so nothing is kept for it.
    /// </para>
    /// </summary>
    /// <param name="source">The source the call is in.</param>
    /// <param name="scan">The map of that source.</param>
    /// <param name="lineBreak">Where the line break in front of the call's line starts.</param>
    /// <param name="lineBreakEnd">The last character of that line break.</param>
    /// <param name="dot">The dot the call hangs off.</param>
    /// <param name="closeParen">The call's closing paren.</param>
    static string KeepingItsLine(string source, SourceScan scan, int lineBreak, int lineBreakEnd, int dot, int closeParen)
    {
        var restEnd = source.IndexOf('\n', closeParen + 1);
        // The line ends inside a literal or a block comment that opened after the call, where a
        // line break more would be content. The call goes from where it stands instead, which
        // keeps its line by leaving on it what followed. A break that ends a line comment is the
        // comment's last character, and is the end of the line all the same
        if (restEnd < 0 ||
            !(scan.IsCode(restEnd) || scan.TryGetCommentEndingAt(restEnd + 1, out _)))
        {
            return Splice(source, dot, closeParen + 1, "");
        }

        if (restEnd > 0 &&
            source[restEnd - 1] == '\r')
        {
            restEnd--;
        }

        var builder = new StringBuilder(source.Length);
        builder.Append(source, 0, lineBreak);
        builder.Append(source, closeParen + 1, restEnd - closeParen - 1);
        // The break that was in front of the call, now behind what followed it
        builder.Append(source, lineBreak, lineBreakEnd + 1 - lineBreak);
        builder.Append(source, restEnd, source.Length - restEnd);
        return builder.ToString();
    }

    /// <summary>
    /// What a statement taken whole leaves where it was: nothing, or one empty line when the line
    /// under it holds a Snapshot call.
    /// <para>
    /// <see cref="KeepingItsLine"/> for a statement, and for its reason.
    /// <c>settings.Snapshot("dup");</c> over <c>other.Snapshot("dup");</c> brought the second up
    /// onto the line the patch names, and the next apply of the same Remove had the same line and
    /// the same anchor to go by and took the sibling's statement.
    /// </para>
    /// <para>
    /// Only where the Snapshot call is on the statement's first line, which is the line the patch
    /// names and so the one the empty line has to be on. With what it is called on a line above
    /// it, the line that comes up onto the call's is further down than the one under the
    /// statement, and one kept line puts neither right.
    /// </para>
    /// </summary>
    /// <param name="source">The source the statement is in.</param>
    /// <param name="scan">The map of that source.</param>
    /// <param name="lineStarts">Where each line of the source starts.</param>
    /// <param name="nameStart">The Snapshot call's name.</param>
    /// <param name="from">The start of the statement's first line.</param>
    /// <param name="to">The start of the line after its last.</param>
    static string KeptStatementLine(string source, SourceScan scan, List<int> lineStarts, int nameStart, int from, int to)
    {
        if (to >= source.Length ||
            LineOf(lineStarts, nameStart) != LineOf(lineStarts, from) ||
            !CallsOnLine(source, scan, lineStarts, LineOf(lineStarts, to), snapshotName, false).Any())
        {
            return "";
        }

        // The break the statement's last line ended in, which is the file's own
        return to >= 2 && source[to - 2] == '\r' ? "\r\n" : "\n";
    }

    /// <summary>
    /// Whether taking a call out would leave nothing of its expression but what it was called on:
    /// the call hangs off a name rather than off another call, and nothing is chained on after it.
    /// <para>
    /// Either of those is enough for what is left to read as it did. A call result is a statement
    /// with one call fewer on the end of it, and with more of the chain to follow, the rest hangs
    /// off the receiver exactly as it hung off this.
    /// </para>
    /// </summary>
    static bool LeavesOnlyItsReceiver(string source, SourceScan scan, int dot, int closeParen)
    {
        var receiverEnd = PreviousToken(source, scan, dot);
        if (receiverEnd >= 0 &&
            source[receiverEnd] == ')' &&
            scan.IsCode(receiverEnd))
        {
            return false;
        }

        var after = closeParen + 1;
        scan.SkipTrivia(ref after);
        return after >= source.Length ||
               source[after] != '.';
    }

    /// <summary>
    /// Whether something takes the value of the expression a call ends: it is awaited, returned,
    /// assigned or passed.
    /// <para>
    /// What the call was called on is then still something with the call gone.
    /// <c>await task.Snapshot("old");</c> reads <c>await task;</c> and
    /// <c>var kept = task.Snapshot("old");</c> reads <c>var kept = task;</c>, each as it would
    /// have run without the snapshot. It is a statement that was nothing but the call that leaves
    /// a name standing by itself.
    /// </para>
    /// <para>
    /// A lambda's body is left out on purpose. <c>_ => _.Snapshot("old")</c> would be left as
    /// <c>_ => _</c>, which is no body for a lambda that returns nothing. And where indentation is
    /// syntax, only what takes it sits on the same line: the <c>=</c> a line above is the one a
    /// whole body hangs off, and the line under it is that body's first statement.
    /// </para>
    /// </summary>
    static bool IsTakenAsAValue(string source, SourceScan scan, List<int> lineStarts, int nameStart)
    {
        var expressionStart = ExpressionStart(source, scan, nameStart);
        var before = PreviousToken(source, scan, expressionStart);
        if (before < 0 ||
            !scan.IsCode(before))
        {
            return false;
        }

        if (scan.Language.IndentationIsSyntax &&
            LineOf(lineStarts, before) != LineOf(lineStarts, expressionStart))
        {
            return false;
        }

        // Passed, or assigned, or compared: an equals sign that ends a comparison takes a value as
        // much as one that assigns. The arrow of a lambda ends in the other character
        if (source[before] is '(' or ',' or '=')
        {
            return true;
        }

        if (!scan.IsIdentifierChar(source[before]))
        {
            return false;
        }

        var wordStart = scan.WordStart(before);
        var word = source.Substring(wordStart, before + 1 - wordStart);
        return word is "await" or "return";
    }

    /// <summary>
    /// The lines of a statement that is one call and nothing else, from the start of its first
    /// line to the start of the line after its last, for taking out whole.
    /// <para>
    /// Only where taking them out cannot change what is around them, which is a narrower thing
    /// than being a statement. It has to have its lines to itself, since a line is what goes. In
    /// C# it has to sit in a block and end in its own semicolon: the body of an <c>if</c> with no
    /// braces is a statement too, and removing that hands the <c>if</c> whatever came next. F#
    /// has no semicolon to look for, so the call has to end its line, and something has to follow
    /// at the same indentation: the last line of a block is the block's value, and a binding left
    /// with nothing under it does not compile.
    /// </para>
    /// </summary>
    static bool TryStatementLines(string source, SourceScan scan, List<int> lineStarts, int nameStart, int closeParen, out int start, out int end)
    {
        start = -1;
        end = -1;
        var expressionStart = ExpressionStart(source, scan, nameStart);
        if (!StartsLine(source, lineStarts, expressionStart))
        {
            return false;
        }

        var cursor = closeParen + 1;
        var byLayout = scan.Language.IndentationIsSyntax;
        if (!byLayout)
        {
            var before = PreviousToken(source, scan, expressionStart);
            if (before >= 0 &&
                !(scan.IsCode(before) && source[before] is ';' or '{' or '}'))
            {
                return false;
            }

            scan.SkipTrivia(ref cursor);
            if (cursor >= source.Length ||
                source[cursor] != ';')
            {
                return false;
            }

            cursor++;
        }

        var lastLine = LineOf(lineStarts, cursor - 1);
        var lineEnd = lastLine < lineStarts.Count ? lineStarts[lastLine] : source.Length;
        while (cursor < lineEnd)
        {
            if (char.IsWhiteSpace(source[cursor]))
            {
                cursor++;
                continue;
            }

            // A comment that runs past the end of the line would be cut in two
            if (scan.TryGetCommentSkip(cursor, out var afterComment) &&
                afterComment <= lineEnd)
            {
                cursor = afterComment;
                continue;
            }

            return false;
        }

        if (byLayout)
        {
            // A name at the same indentation is the next statement of the same block. An
            // operator there carries this one on, and anything further left ends the block
            var next = lineEnd;
            scan.SkipTrivia(ref next);
            if (next >= source.Length ||
                !scan.IsIdentifierChar(source[next]) ||
                !StartsLine(source, lineStarts, next) ||
                LeadingWhitespace(source, lineStarts, next) != LeadingWhitespace(source, lineStarts, expressionStart))
            {
                return false;
            }
        }

        start = lineStarts[LineOf(lineStarts, expressionStart) - 1];
        end = lineEnd;
        return true;
    }

    /// <summary>
    /// Whether the Snapshot call the recorded line names has already been removed: the line holds
    /// no Snapshot call, and the verify statement it belongs to has none chained onto it.
    /// <para>
    /// A Remove is applied by the test process itself rather than queued, so a multi-targeted run
    /// applies the same one once per framework. Every one after the first found the anchor gone
    /// from the call it named and went looking for it elsewhere, and a sibling holding the same
    /// literal is exactly where it found it: that snapshot was stripped instead, the way
    /// <see cref="IsOnHint"/> describes for a Set.
    /// </para>
    /// <para>
    /// The statement is the nearest verify call at or above the line, provided its chain still
    /// reaches the line or the one above it. Removing a Snapshot call that had a line of its own
    /// pulls the rest of its statement up onto the line above, so the recorded line then holds
    /// whatever followed, and the statement it named ends just before it.
    /// </para>
    /// <para>
    /// A call on a variable has no verify statement to be read off, and its whole statement goes.
    /// So an empty line over a line holding a Snapshot call is read as removed too, which is what
    /// <see cref="KeptStatementLine"/> leaves, and <see cref="KeepingItsLine"/>.
    /// </para>
    /// </summary>
    static bool RemovedAtHint(string source, SourceScan scan, List<int> lineStarts, int lineHint, int? memberLine, string[] entryPoints)
    {
        var lineCount = lineStarts.Count;
        if (lineHint < 1 ||
            lineHint > lineCount)
        {
            return false;
        }

        var floor = memberLine is null ? 1 : Clamp(memberLine.Value, lineCount);
        var ceiling = memberLine is null ? lineCount + 1 : NextMemberLine(source, scan, lineStarts, floor);
        // A hint outside the member has gone stale, and names nothing
        if (lineHint < floor ||
            lineHint >= ceiling)
        {
            return false;
        }

        // A Snapshot call still on the line is one to remove, whatever it hangs off
        if (CallsOnLine(source, scan, lineStarts, lineHint, snapshotName, false).Any())
        {
            return false;
        }

        // An empty line over a Snapshot call is the line a Remove kept, and the only sign there is
        // of a statement taken whole: nothing above it need be a verify call
        if (lineHint < lineCount &&
            IsEmptyLine(source, scan, lineStarts, lineHint) &&
            CallsOnLine(source, scan, lineStarts, lineHint + 1, snapshotName, false).Any())
        {
            return true;
        }

        for (var line = lineHint; line >= floor; line--)
        {
            var calls = CallsOnLine(source, scan, lineStarts, line, entryPoints, true).ToList();
            if (calls.Count == 0)
            {
                continue;
            }

            // The last on the line is the nearest one above the hint
            var (_, openParen) = calls[^1];
            if (!TryScanArguments(source, scan, openParen, out var closeParen, out _))
            {
                return false;
            }

            var end = WalkChain(source, scan, closeParen + 1, methodName, out var chained);
            return chained < 0 &&
                   LineOf(lineStarts, end - 1) >= lineHint - 1;
        }

        return false;
    }

    /// <summary>
    /// Whether a line holds nothing but whitespace, and is a line of code: an empty line of a
    /// literal's content is not one anything was taken from.
    /// </summary>
    static bool IsEmptyLine(string source, SourceScan scan, List<int> lineStarts, int line)
    {
        var start = lineStarts[line - 1];
        var end = line < lineStarts.Count ? lineStarts[line] : source.Length;
        for (var index = start; index < end; index++)
        {
            if (!char.IsWhiteSpace(source[index]))
            {
                return false;
            }
        }

        return scan.IsCode(start);
    }

    /// <summary>
    /// The calls a Snapshot call has to be appended in front of rather than after: each hands back
    /// something other than the SettingsTask a Snapshot call is made on, so the chain cannot be
    /// carried on past one.
    /// <para>
    /// The same three in both languages. This used to be ToTask alone, and F#'s alone, because an
    /// F# test ends its chain that way: F# does not apply the conversion that lets a SettingsTask
    /// be awaited. C# reaches for all three as readily - a library that configures every await, a
    /// synchronous test blocking on GetAwaiter - and with nothing to stop at there, an append onto
    /// <c>await Verify(value).ConfigureAwait(false)</c> went after the ConfigureAwait, where a
    /// ConfiguredTaskAwaitable has no Snapshot to call (CS1061). The anchor probe had already said
    /// the call site could host a snapshot, so the verification was inline with nowhere to put one.
    /// </para>
    /// </summary>
    static string[] chainTerminators = ["ToTask", "ConfigureAwait", "GetAwaiter"];

    /// <summary>
    /// Walks the calls chained onto an invocation and returns where a call should be appended:
    /// the end of the chain, or the point in front of the first of the
    /// <see cref="chainTerminators"/> when the chain holds one.
    /// <paramref name="found"/> is the open paren of the first call to <paramref name="name"/>
    /// among them, or -1 where there is none. The position rather than the fact of it, because a
    /// caller deciding what to do about one has to read its argument.
    /// </summary>
    static int WalkChain(string source, SourceScan scan, int index, string name, out int found)
    {
        found = -1;
        // Where the chain was before the terminating call, which is where an appended one goes:
        // in front of the terminator, and behind the whitespace and line break that introduced it
        var beforeTerminator = -1;
        while (true)
        {
            var cursor = index;
            scan.SkipTrivia(ref cursor);
            if (cursor >= source.Length ||
                source[cursor] != '.')
            {
                break;
            }

            cursor++;
            scan.SkipTrivia(ref cursor);

            var nameStart = cursor;
            while (cursor < source.Length &&
                   scan.IsIdentifierChar(source[cursor]))
            {
                cursor++;
            }

            if (cursor == nameStart ||
                !TrySkipToParen(source, scan, cursor, out var paren) ||
                !TryScanArguments(source, scan, paren, out var closeParen, out _))
            {
                break;
            }

            if (found < 0 &&
                IsCall(source, nameStart, cursor, name))
            {
                found = paren;
            }

            if (beforeTerminator < 0 &&
                IsCall(source, nameStart, cursor, chainTerminators))
            {
                beforeTerminator = index;
            }

            index = closeParen + 1;
        }

        return beforeTerminator < 0 ? index : beforeTerminator;
    }

    static bool IsCall(string source, int nameStart, int nameEnd, string name) =>
        nameEnd - nameStart == name.Length &&
        string.CompareOrdinal(source, nameStart, name, 0, name.Length) == 0;

    static bool IsCall(string source, int nameStart, int nameEnd, string[] names)
    {
        foreach (var name in names)
        {
            if (IsCall(source, nameStart, nameEnd, name))
            {
                return true;
            }
        }

        return false;
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

    /// <summary>
    /// The indentation a splice at a call measures one level in from: the leading whitespace of
    /// the line the call's name is on, unless the language reads indentation as syntax and the
    /// call's expression starts further along that line.
    /// <para>
    /// One level in from the line is right for C#, where it is only a convention. In F# a new
    /// line has to clear the column the expression starts at
    /// (<see cref="SourceLanguage.IndentationIsSyntax"/>): a chained call right of it, and a
    /// literal no further left than it. After <c>do!</c> that column is already a level past the
    /// line's indentation, and after <c>let! x =</c> it is further. So an appended call landed on
    /// the column or left of it, and after anything longer than <c>do!</c> a literal given a line
    /// of its own did too: FS0010 either way, in source that compiled until it was accepted into.
    /// </para>
    /// <para>
    /// A call whose expression starts its line, or started on a line above, is measured from the
    /// line as before. The line it is on was already somewhere the compiler accepts, and one level
    /// further in than that is too.
    /// </para>
    /// </summary>
    static string StatementIndent(string source, SourceScan scan, List<int> lineStarts, int nameStart)
    {
        if (scan.Language.IndentationIsSyntax)
        {
            var start = ExpressionStart(source, scan, nameStart);
            if (LineOf(lineStarts, start) == LineOf(lineStarts, nameStart))
            {
                return IndentTo(source, lineStarts, start);
            }
        }

        return LeadingWhitespace(source, lineStarts, nameStart);
    }

    /// <summary>
    /// Whitespace as wide as the column <paramref name="offset"/> is at: its line's own
    /// indentation, then spaces for whatever stands between that and the offset. The same string
    /// as the line's indentation for the first thing on a line.
    /// </summary>
    static string IndentTo(string source, List<int> lineStarts, int offset)
    {
        var lead = LeadingWhitespace(source, lineStarts, offset);
        var lineStart = lineStarts[LineOf(lineStarts, offset) - 1];
        return lead + new string(' ', offset - lineStart - lead.Length);
    }

    /// <summary>
    /// Where the expression a call belongs to starts: back from the call's name over everything it
    /// is reached through, one receiver at a time. For the Snapshot call in
    /// <c>Verifier.Verify(value).UseDirectory("x").Snapshot()</c> that is Verifier.
    /// <para>
    /// Whatever cannot be read as a name or a call ends the walk where it has got to, which is
    /// right of where the expression really starts. The answer is used as a column to stay clear
    /// of, so one too far right costs an indent deeper than it had to be, where one too far left
    /// would cost source that does not compile.
    /// </para>
    /// </summary>
    static int ExpressionStart(string source, SourceScan scan, int nameStart)
    {
        var start = nameStart;
        while (true)
        {
            var dot = PreviousToken(source, scan, start);
            if (dot < 0 ||
                source[dot] != '.')
            {
                return start;
            }

            var end = PreviousToken(source, scan, dot);
            // A literal is a receiver too, and not one this reads
            if (end < 0 ||
                !scan.IsCode(end))
            {
                return start;
            }

            if (source[end] == ')')
            {
                if (!TryFindOpenParen(source, scan, end, out var openParen))
                {
                    return start;
                }

                // The name an argument list belongs to sits against it. With anything between
                // them the parens are an expression of their own, and that is where this starts
                end = openParen - 1;
                if (end >= 0 &&
                    source[end] == '>' &&
                    !TrySkipTypeArgumentsBack(source, scan, ref end))
                {
                    return start;
                }

                if (end < 0 ||
                    !scan.IsIdentifierChar(source[end]))
                {
                    return openParen;
                }
            }
            else if (!scan.IsIdentifierChar(source[end]))
            {
                return start;
            }

            start = scan.WordStart(end);
        }
    }

    /// <summary>
    /// The offset of the last character before <paramref name="index"/> that is not whitespace and
    /// not in a comment, or -1 when there is none.
    /// <para>
    /// A literal counts, where <see cref="SourceScan.PreviousSignificant"/> steps over one. That
    /// suits a caller asking what kind of thing precedes a name, and not one asking what a call
    /// hangs off: looking past <c>"text"</c> in <c>"text".Verify(value)</c> finds whatever came
    /// before the literal and takes it for the receiver.
    /// </para>
    /// </summary>
    static int PreviousToken(string source, SourceScan scan, int index)
    {
        while (index > 0)
        {
            if (scan.TryGetCommentEndingAt(index, out var commentStart))
            {
                index = commentStart;
                continue;
            }

            if (!char.IsWhiteSpace(source[index - 1]))
            {
                return index - 1;
            }

            index--;
        }

        return -1;
    }

    /// <summary>
    /// The open paren that the close paren at <paramref name="closeParen"/> closes: the scan
    /// <see cref="TryScanArguments"/> does, run backwards, with comments and literals stepped over
    /// whole in the same way.
    /// </summary>
    static bool TryFindOpenParen(string source, SourceScan scan, int closeParen, out int openParen)
    {
        openParen = -1;
        var depth = 1;
        // Just past what is still to be read, so the character at closeParen itself is not
        var index = closeParen;
        while (index > 0)
        {
            if (scan.TryGetSkipEndingAt(index, out var skipStart))
            {
                index = skipStart;
                continue;
            }

            index--;
            switch (source[index])
            {
                case ')':
                case ']':
                case '}':
                    depth++;
                    continue;
                case '(':
                case '[':
                case '{':
                    depth--;
                    if (depth == 0)
                    {
                        openParen = index;
                        return source[index] == '(';
                    }

                    continue;
            }
        }

        return false;
    }

    /// <summary>
    /// Steps back over the type argument list that ends at <paramref name="end"/>, leaving it on
    /// the character in front of the list: <see cref="SourceLanguage.TrySkipTypeArguments"/> run
    /// backwards, accepting only what a type argument list can hold for the same reason.
    /// </summary>
    static bool TrySkipTypeArgumentsBack(string source, SourceScan scan, ref int end)
    {
        var depth = 0;
        for (var index = end; index >= 0; index--)
        {
            var ch = source[index];
            if (ch == '>')
            {
                depth++;
                continue;
            }

            if (ch == '<')
            {
                depth--;
                if (depth == 0)
                {
                    end = index - 1;
                    return true;
                }

                continue;
            }

            if (!scan.IsIdentifierChar(ch) &&
                !scan.Language.IsTypeArgumentChar(ch))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// The column indentation reaches, with a tab advancing to the next multiple of four. Four
    /// rather than eight because that is what the tab indented C# this has to measure is written
    /// with. F# rejects tabs outright (FS1161), so only C# ever gets here with one.
    /// </summary>
    static int IndentWidth(string whitespace)
    {
        var width = 0;
        foreach (var character in whitespace)
        {
            if (character == '\t')
            {
                width += 4 - width % 4;
            }
            else
            {
                width++;
            }
        }

        return width;
    }

    static string[] snapshotName = [methodName];

    /// <summary>
    /// The call the anchor names, rather than whichever one sits nearest the hint.
    /// <para>
    /// Set and Append locate by content for a reason - the same literal is just as likely to be in
    /// the test next door - and a Remove has exactly the same problem with none of the protection.
    /// It deleted the nearest call to a line number that stops being true as soon as anything
    /// above it is edited, so a stale hint retired somebody else's snapshot and reported Applied.
    /// </para>
    /// <para>
    /// With no anchor there is nothing to match on and nearest-to-the-hint is all there is, which
    /// is the case for a producer whose language withholds CallerArgumentExpression and sends no
    /// value either.
    /// </para>
    /// </summary>
    static bool TryFindAnchoredCall(
        SourceLanguage language,
        string source,
        SourceScan scan,
        List<int> lineStarts,
        int lineHint,
        int? memberLine,
        string? originalExpression,
        string? originalValue,
        string eol,
        out int nameStart,
        out int openParen)
    {
        if (string.IsNullOrEmpty(originalExpression) &&
            originalValue == null)
        {
            return TryFindCall(source, scan, lineStarts, lineHint, memberLine, snapshotName, false, out nameStart, out openParen);
        }

        // ReSharper disable once RedundantSuppressNullableWarningExpression
        var needle = string.IsNullOrEmpty(originalExpression) ? null : NormalizeTo(originalExpression!, eol);
        var previous = originalValue == null ? null : SourceLanguage.NormalizeNewlines(originalValue);

        foreach (var (candidateName, candidateParen) in FindCalls(source, scan, lineStarts, lineHint, memberLine, snapshotName, false))
        {
            if (!TryReadArguments(source, scan, candidateParen, out var expected))
            {
                continue;
            }

            if (needle != null)
            {
                if (!expected.Matches(source, needle))
                {
                    continue;
                }
            }
            else
            {
                if (expected.IsAbsent ||
                    expected.BlockedByName)
                {
                    continue;
                }

                var argument = source.Substring(expected.Start, expected.End - expected.Start);
                if (!language.TryParse(argument, out var value) ||
                    value != previous)
                {
                    continue;
                }
            }

            nameStart = candidateName;
            openParen = candidateParen;
            return true;
        }

        nameStart = -1;
        openParen = -1;
        return false;
    }

    static bool TryFindCall(
        string source,
        SourceScan scan,
        List<int> lineStarts,
        int lineHint,
        int? memberLine,
        string[] names,
        bool checkReceiver,
        out int nameStart,
        out int openParen)
    {
        foreach (var call in FindCalls(source, scan, lineStarts, lineHint, memberLine, names, checkReceiver))
        {
            (nameStart, openParen) = call;
            return true;
        }

        nameStart = -1;
        openParen = -1;
        return false;
    }

    /// <summary>
    /// Locates calls by name: the recorded line first, then outward - line, line+1, line-1, line+2
    /// and so on. A tie goes to the line at or after, because a file that moved under a pending
    /// patch usually grew above the call rather than below it.
    /// <para>
    /// <paramref name="memberLine"/>, where the patch named a member the file still declares, does
    /// two things. It bounds the search to the member's span: a call above the declaration, or at
    /// or past the next member's, cannot be inside the member the patch came from, whatever else
    /// recommends it, so an identical snapshot in a neighbouring test is not reachable at all. And
    /// it becomes the origin of the outward walk, so a hint gone stale fans out from the right
    /// test rather than from a line that now belongs to another one.
    /// </para>
    /// <para>
    /// The recorded line is still tried first, since a hint that lands on a call is the whole
    /// point of having one, and it is what keeps two snapshots in the same method apart.
    /// </para>
    /// <paramref name="checkReceiver"/> rejects a call reached through a receiver of the caller's
    /// own, which an entry point never is and a Snapshot call always is.
    /// </summary>
    static IEnumerable<(int nameStart, int openParen)> FindCalls(
        string source,
        SourceScan scan,
        List<int> lineStarts,
        int lineHint,
        int? memberLine,
        string[] names,
        bool checkReceiver)
    {
        var lineCount = lineStarts.Count;
        lineHint = Clamp(lineHint, lineCount);
        var floor = memberLine is null ? 1 : Clamp(memberLine.Value, lineCount);
        var origin = memberLine is null ? lineHint : floor;
        // The member's other end. The declaration floors the search; the next declaration at the
        // member's own indentation ceilings it, since a call at or past that line is inside the
        // test next door however close to the hint it sits. The ceiling used to be applied only
        // to the recorded line - the outward walk below ran to the end of the file - so a member
        // whose own call had changed or gone walked straight into the neighbour's and rewrote it
        var ceiling = memberLine is null ? lineCount + 1 : NextMemberLine(source, scan, lineStarts, floor);
        // The recorded line is tried first so that two snapshots in one member stay apart. A hint
        // outside the member's own span has gone stale - something above it moved - and points
        // into another test, which the bounds already exclude
        if (lineHint >= floor &&
            lineHint < ceiling)
        {
            foreach (var call in CallsOnLine(source, scan, lineStarts, lineHint, names, checkReceiver))
            {
                yield return call;
            }
        }

        for (var distance = 0; distance < lineCount; distance++)
        {
            // Below the origin then above it, without building a pair of candidates per step. At
            // distance zero the two sides are the same line, so only the first is taken
            for (var side = 0; side < 2; side++)
            {
                if (distance == 0 &&
                    side == 1)
                {
                    break;
                }

                var line = side == 0 ? origin + distance : origin - distance;
                if (line < floor ||
                    line >= ceiling ||
                    line > lineCount ||
                    // Already tried, and yielding it twice would have a caller that rejects the
                    // first reject it again rather than move on
                    line == lineHint)
                {
                    continue;
                }

                foreach (var call in CallsOnLine(source, scan, lineStarts, line, names, checkReceiver))
                {
                    yield return call;
                }
            }
        }
    }

    /// <summary>
    /// Where the next member after the one declared at <paramref name="memberLine"/> begins, or
    /// one past the last line when it is the file's last. Everything from the declaration up to
    /// this line is the member's own span, and it is the whole territory a search anchored to the
    /// member may roam.
    /// <para>
    /// Indentation is what tells a member from a local, because nothing in front of the name does:
    /// <c>var hash = Hash()</c> and F#'s <c>let hash = hash ()</c> are declarations to
    /// <see cref="SourceScan.IsDeclaration"/> exactly as a sibling test method is. A declaration
    /// indented past the member's own sits inside its body - a local, a local function, a nested
    /// type - and none of those end the member. Counting them did, which made the hint unreachable
    /// for the ordinary shape of a test: a local, then a verify call on it. A sibling shares the
    /// member's own indentation, so the comparison is inclusive.
    /// </para>
    /// <para>
    /// Compared as columns (<see cref="IndentWidth" />) rather than characters. Two tabs are two
    /// characters and four spaces are four, so a tab indented body under a space indented member
    /// read as a sibling and ended the member at its first local: the call the hint named fell
    /// outside the span, and a same literal call above it was patched instead.
    /// </para>
    /// </summary>
    static int NextMemberLine(string source, SourceScan scan, List<int> lineStarts, int memberLine)
    {
        var lineCount = lineStarts.Count;
        if (memberLine >= lineCount)
        {
            return lineCount + 1;
        }

        var memberIndent = IndentWidth(LeadingWhitespace(source, lineStarts, lineStarts[memberLine - 1]));
        var start = lineStarts[memberLine];
        var end = source.Length;
        for (var index = start; index < end; index++)
        {
            if (!scan.IsIdentifierChar(source[index]) ||
                !scan.IsCode(index) ||
                !StartsToken(source, scan, index))
            {
                continue;
            }

            if (scan.IsDeclaration(DeclarationStart(source, index)) &&
                IndentWidth(LeadingWhitespace(source, lineStarts, index)) <= memberIndent)
            {
                return LineOf(lineStarts, index);
            }

            while (index + 1 < end &&
                   scan.IsIdentifierChar(source[index + 1]))
            {
                index++;
            }
        }

        return lineCount + 1;
    }

    static int Clamp(int line, int lineCount) =>
        Math.Min(Math.Max(line, 1), lineCount);

    /// <summary>
    /// The calls on one line, in source order. Order matters across names as much as within one:
    /// the outward walk in <see cref="FindCalls"/> is what makes the nearest call win, and taking
    /// every Verify on a line ahead of a Throws that sits to the left of it would undo that for
    /// the one line where being nearest counts most.
    /// </summary>
    static IEnumerable<(int nameStart, int openParen)> CallsOnLine(
        string source,
        SourceScan scan,
        List<int> lineStarts,
        int line,
        string[] names,
        bool checkReceiver)
    {
        var lineCount = lineStarts.Count;
        var start = lineStarts[line - 1];
        var end = line < lineCount ? lineStarts[line] : source.Length;
        List<(int nameStart, int openParen)>? matches = null;
        foreach (var name in names)
        {
            var index = start;
            while (index < end)
            {
                // Bounded to the line. The unbounded overload searched to the end of the file and
                // then discarded whatever it found for being past this line - work repeated for
                // every line the outward walk probes, which on the miss path is all of them, so a
                // search that found nothing cost the file size squared. A name cannot span the
                // line break that ends the range, having no newline in it, so nothing that used to
                // match stops matching.
                var at = source.IndexOf(name, index, end - index, StringComparison.Ordinal);
                if (at < 0)
                {
                    break;
                }

                index = at;
                // A whole token: VerifyDocx is not a Verify call, whatever it is called
                var identifierEnd = index + name.Length;
                if (identifierEnd < source.Length &&
                    scan.IsIdentifierChar(source[identifierEnd]))
                {
                    index += name.Length;
                    continue;
                }

                // In code, the start of a token, an invocation rather than a declaration, and
                // followed by an argument list. A commented out example passes none of these
                if (scan.IsCode(index) &&
                    StartsToken(source, scan, index) &&
                    !scan.IsDeclaration(index) &&
                    !(checkReceiver && IsForeignReceiver(source, scan, index)) &&
                    TrySkipToParen(source, scan, identifierEnd, out var paren))
                {
                    matches ??= [];
                    matches.Add((index, paren));
                }

                index += name.Length;
            }
        }

        if (matches is null)
        {
            yield break;
        }

        matches.Sort(static (left, right) => left.nameStart.CompareTo(right.nameStart));
        foreach (var call in matches)
        {
            yield return call;
        }
    }

    /// <summary>
    /// Where the member a patch came from is declared, or null when it named none or the file no
    /// longer declares it - a test renamed since the run, which leaves the hint as all there is.
    /// <para>
    /// A member is not an identity, since it holds any number of snapshots, but it is a region: a
    /// call above the declaration is not in it, and that is what bounds the search in
    /// <see cref="FindCalls"/>.
    /// </para>
    /// <para>
    /// A declaration whose span holds the hint wins over one that is merely nearer: in the
    /// scenario per class layout two nested types each declare the same test, and the one below
    /// the hint can be the nearer, which put the floor past the hint and handed the patch to the
    /// other type's call. Where several spans hold it - an F# local named like the test, inside
    /// the test - the outermost is the member. Where none does, the nearest wins, so overloads and
    /// partials still pick the plausible one.
    /// </para>
    /// </summary>
    static int? MemberLine(string source, SourceScan scan, List<int> lineStarts, int lineHint, string? memberName)
    {
        if (string.IsNullOrEmpty(memberName))
        {
            return null;
        }

        var nearest = -1;
        var holding = -1;
        var holdingIndent = int.MaxValue;
        var index = 0;
        while (true)
        {
            // ReSharper disable once RedundantSuppressNullableWarningExpression
            index = source.IndexOf(memberName!, index, StringComparison.Ordinal);
            if (index < 0)
            {
                break;
            }

            // ReSharper disable once RedundantSuppressNullableWarningExpression
            var end = index + memberName!.Length;
            if (scan.IsCode(index) &&
                StartsToken(source, scan, index) &&
                (end >= source.Length || !scan.IsIdentifierChar(source[end])) &&
                scan.IsDeclaration(DeclarationStart(source, index)))
            {
                var line = LineOf(lineStarts, index);
                if (nearest < 0 ||
                    Math.Abs(line - lineHint) < Math.Abs(nearest - lineHint))
                {
                    nearest = line;
                }

                if (line <= lineHint &&
                    lineHint < NextMemberLine(source, scan, lineStarts, line))
                {
                    var indent = IndentWidth(LeadingWhitespace(source, lineStarts, index));
                    if (indent < holdingIndent ||
                        (indent == holdingIndent && line > holding))
                    {
                        holding = line;
                        holdingIndent = indent;
                    }
                }
            }

            index = end;
        }

        if (holding >= 0)
        {
            return holding;
        }

        if (nearest >= 0)
        {
            return nearest;
        }

        return null;
    }

    /// <summary>
    /// Where a declared name starts for the purpose of asking what declares it: before the opening
    /// backticks of an F# <c>``test name``</c>, the usual way an F# test is named. Judged from the
    /// name itself, the backtick in front of it is not a keyword, so no such member was ever found
    /// and the search it should have bounded ran across the whole file.
    /// </summary>
    static int DeclarationStart(string source, int nameStart)
    {
        if (nameStart >= 2 &&
            source[nameStart - 1] == '`' &&
            source[nameStart - 2] == '`')
        {
            return nameStart - 2;
        }

        return nameStart;
    }

    static bool StartsToken(string source, SourceScan scan, int index) =>
        index == 0 ||
        !scan.IsIdentifierChar(source[index - 1]);

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
    static bool IsForeignReceiver(string source, SourceScan scan, int nameStart)
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
            !scan.IsIdentifierChar(source[end]))
        {
            // Not a plain receiver, so a literal, an indexer or a call result
            return true;
        }

        var start = end;
        while (start > 0 &&
               scan.IsIdentifierChar(source[start - 1]))
        {
            start--;
        }

        var receiver = source.Substring(start, end - start + 1);
        return receiver != verifierType &&
               receiver != "this";
    }

    static bool TrySkipToParen(string source, SourceScan scan, int index, out int paren)
    {
        paren = -1;
        scan.SkipTrivia(ref index);
        if (index < source.Length &&
            source[index] == '<' &&
            scan.Language.TrySkipTypeArguments(source, ref index))
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
    static bool TryScanArguments(string source, SourceScan scan, int openParen, out int closeParen, out List<int> topCommas)
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

    /// <summary>
    /// Narrows a span to the expression in it: whitespace and comments are not part of the
    /// argument, and leaving a comment in makes the argument read as something other than the
    /// literal it is.
    /// </summary>
    static void TrimSpan(string source, SourceScan scan, ref int start, ref int end)
    {
        while (start < end)
        {
            if (char.IsWhiteSpace(source[start]))
            {
                start++;
                continue;
            }

            if (scan.TryGetCommentSkip(start, out var afterComment) &&
                afterComment <= end)
            {
                start = afterComment;
                continue;
            }

            break;
        }

        while (end > start)
        {
            // The comment is asked about before the whitespace, because a line comment's span
            // includes the newline that ends it. Trimming first ate that newline, after which
            // nothing ended at `end` any more and the comment stayed inside the argument
            if (scan.TryGetCommentEndingAt(end, out var commentStart) &&
                commentStart >= start)
            {
                end = commentStart;
                continue;
            }

            if (char.IsWhiteSpace(source[end - 1]))
            {
                end--;
                continue;
            }

            break;
        }
    }

    /// <summary>
    /// Renders the literal for a splice at <paramref name="spanStart"/>, in the argument list of
    /// the call at <paramref name="nameStart"/>, indented to suit where it lands.
    /// </summary>
    static string RenderArgument(string source, SourceScan scan, List<int> lineStarts, int nameStart, int spanStart, string newContent, string eol, string fileUnit)
    {
        var indent = IndentForSpan(source, scan, lineStarts, nameStart, spanStart, fileUnit);
        var rendered = scan.Language.Render(newContent, indent, eol);
        if (StartsLine(source, lineStarts, spanStart))
        {
            return rendered;
        }

        return OnOwnLine(rendered, indent, eol);
    }

    /// <summary>
    /// Puts a multi-line literal on its own line rather than trailing the open paren, so its
    /// opening delimiter sits with its content and its closing one. A regular literal stays where
    /// it is, since it has nothing to line up with.
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

    // A copy of the whole source for every patch, so the only copy where the framework allows it:
    // a builder holds the text once itself before it makes the string
    static string Splice(string source, int start, int end, string replacement) =>
#if NET6_0_OR_GREATER
        string.Concat(source.AsSpan(0, start), replacement, source.AsSpan(end));
#else
        new StringBuilder(source.Length - (end - start) + replacement.Length)
            .Append(source, 0, start)
            .Append(replacement)
            .Append(source, end, source.Length - end)
            .ToString();
#endif

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
    static string DetectIndentUnit(string source, SourceScan scan, List<int> lineStarts)
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
        var tabs = lead.Length > 0 ? lead[^1] == '\t' : fileUsesTabs;
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

    /// <summary>
    /// The indentation a literal taking a line of its own would sit at: one level in from the
    /// span's line, or the span's own column when it already starts a line.
    /// <para>
    /// <paramref name="nameStart"/> is the call the span is an argument of. Where the span is on
    /// that call's own line, the level is counted from what <see cref="StatementIndent"/> says the
    /// call is measured from rather than from the line.
    /// </para>
    /// </summary>
    static string IndentForSpan(string source, SourceScan scan, List<int> lineStarts, int nameStart, int spanStart, string fileUnit)
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

        var leadText = LineOf(lineStarts, nameStart) == line
            ? StatementIndent(source, scan, lineStarts, nameStart)
            : lead.ToString();
        return leadText + UnitFor(fileUnit, leadText);
    }
}
