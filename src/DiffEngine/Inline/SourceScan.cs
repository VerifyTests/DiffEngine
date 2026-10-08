using System.Buffers;

namespace DiffEngine;

/// <summary>
/// A one pass lexical map of a source file: where the comments, strings and char literals are, and
/// therefore which offsets are code.
/// <para>
/// The patcher finds its call sites by scanning text, and a text scan that cannot see a comment
/// or a string patches a commented out example, or the middle of another test's snapshot content,
/// as readily as the real call. Lexing once and asking the map is cheaper than lexing per search,
/// and it is one implementation: every search agrees on what a string is because there is only
/// one answer to ask.
/// </para>
/// <para>
/// The map is language neutral - an offset is code or it is not - so only the lexing that fills it
/// is per language, and that lives on <see cref="SourceLanguage"/>. The language is carried here
/// because nothing that reads the map can do without it: whatever is looking at an offset is about
/// to ask what an identifier character is, or how a literal is written.
/// </para>
/// <para>
/// Built again for every patch, over the whole file, so what it is made of shows on a large one. A
/// map as long as the source and three hash tables of its comments and literals came to two
/// megabytes a patch for a ten thousand line file, most of it in arrays large enough to be
/// collected only with the oldest generation. So the map is rented, which is why a scan is
/// disposed, and the spans are kept in the order the lexer found them rather than hashed.
/// </para>
/// </summary>
sealed class SourceScan(SourceLanguage language, string source) :
    IDisposable
{
    /// <summary>
    /// True at every offset inside a comment or a literal. Rented, and longer than the source.
    /// </summary>
    bool[] skipped = Rent(source.Length);

    /// <summary>
    /// Where each comment or literal starts and the offset just past it, in source order. The
    /// spans cannot overlap, so both lists are sorted and either end of a span is found by
    /// searching for it.
    /// </summary>
    readonly List<int> starts = [];

    readonly List<int> ends = [];

    /// <summary>
    /// Which of the spans are comments. A literal is content, so the two cannot be treated alike
    /// where trivia is being stepped over or trimmed off.
    /// </summary>
    readonly List<bool> comments = [];

    public SourceLanguage Language { get; } = language;

    public string Source { get; } = source;

    static bool[] Rent(int length)
    {
        var rented = ArrayPool<bool>.Shared.Rent(length);
        Array.Clear(rented, 0, length);
        return rented;
    }

    /// <summary>
    /// Hands the map back. A scan asked anything after this throws rather than answer from an
    /// array that some other scan may by then be filling.
    /// </summary>
    public void Dispose()
    {
        var rented = skipped;
        skipped = [];
        if (rented.Length > 0)
        {
            ArrayPool<bool>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Records a comment or literal spanning <paramref name="start"/> to <paramref name="end"/>.
    /// Called by the lexer on <see cref="SourceLanguage"/> as it fills the map, in the order it
    /// meets them, which is the order the searches below rely on. Everything it does not record
    /// is code.
    /// </summary>
    public void AddSkip(int start, int end, bool comment)
    {
        starts.Add(start);
        ends.Add(end);
        comments.Add(comment);
        skipped.AsSpan(start, end - start).Fill(true);
    }

    /// <summary>
    /// The scan this one is being made from, while it is, and how much longer this source is than
    /// that one's. See <see cref="Edited"/>.
    /// </summary>
    SourceScan? before;

    int growth;

    /// <summary>
    /// The first offset at which the lexer may be told the rest is known: just past the edit, for
    /// a scan being made from another, and never for one lexed whole.
    /// </summary>
    public int RejoinFrom { get; private set; } = int.MaxValue;

    /// <summary>
    /// The scan of this source after an edit, made from this one and not by lexing the whole of
    /// <paramref name="edited"/> again.
    /// <para>
    /// A batch applies each patch to what the one before it left, so the file was lexed again for
    /// every patch, start to end, to find the handful of spans one literal changed. What a lexer
    /// makes of a line depends on the text before it only through whether the line starts inside
    /// a comment or a literal, and on the text after it not at all once a span has closed. So the
    /// spans before the edit are kept, lexing starts again at the line the edit begins on, and it
    /// stops at the first line start past the edit that both this scan and the new one take as
    /// code (<see cref="TryRejoin"/>): from there on the two read the same text from the same
    /// state, and the rest of this scan's spans are the new one's, moved by the change in length.
    /// </para>
    /// <para>
    /// One thing does look across a line break from in front of it, which is a backslash: C#'s
    /// char and string scans step over the character after one, and F#'s char literal reads the
    /// one after that. So a line that follows a backslash is not started from, and the line
    /// before it is. <c>SourceScanTests</c> holds the result to a scan of the whole text, over
    /// edits made at random.
    /// </para>
    /// <para>
    /// This scan is still whole afterwards, and still the caller's to dispose.
    /// </para>
    /// </summary>
    public SourceScan Edited(string edited)
    {
        var limit = Math.Min(Source.Length, edited.Length);
        var prefix = CommonPrefix(Source, edited, limit);
        var suffix = CommonSuffix(Source, edited, limit - prefix);
        var restart = RestartFor(prefix);
        var next = new SourceScan(Language, edited);

        // About as many spans as there were, so the lists are sized once and not as they grow
        next.starts.Capacity = starts.Count + 16;
        next.ends.Capacity = starts.Count + 16;
        next.comments.Capacity = starts.Count + 16;
        var kept = LowerBound(starts, restart);
        for (var index = 0; index < kept; index++)
        {
            next.starts.Add(starts[index]);
            next.ends.Add(ends[index]);
            next.comments.Add(comments[index]);
        }

        Array.Copy(skipped, next.skipped, restart);
        next.before = this;
        next.growth = edited.Length - Source.Length;
        // Strictly past the edit, so the line break in front of the offset is one both texts have
        next.RejoinFrom = edited.Length - suffix + 1;
        Language.Lex(next, restart);
        next.before = null;
        next.RejoinFrom = int.MaxValue;

        if (lineStarts is not null)
        {
            next.lineStarts = EditedLineStarts(lineStarts, edited, prefix, edited.Length - suffix, next.growth);
        }

        return next;
    }

    /// <summary>
    /// Which lines an edit of this source moved: the first line, 1 based and in this source, that
    /// starts at or past the end of what the edit replaced, and how many lines further down it
    /// and those after it are in <paramref name="edited"/>.
    /// <para>
    /// Read off the two texts, as <see cref="Edited"/> reads where to lex again, rather than
    /// reported by whatever made the edit: every shape the patcher writes is then counted the
    /// same way, including one it learns later.
    /// </para>
    /// </summary>
    public (int From, int By) LinesMoved(string edited)
    {
        var limit = Math.Min(Source.Length, edited.Length);
        var prefix = CommonPrefix(Source, edited, limit);
        var suffix = CommonSuffix(Source, edited, limit - prefix);
        var end = Source.Length - suffix;
        var by = LineBreaks(edited, prefix, edited.Length - suffix) - LineBreaks(Source, prefix, end);
        return (LowerBound(LineStarts, end) + 1, by);
    }

    static int LineBreaks(string text, int start, int end)
    {
        var count = 0;
        foreach (var ch in text.AsSpan(start, end - start))
        {
            if (ch == '\n')
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Where lexing starts again for an edit that begins at <paramref name="offset"/>: the start
    /// of its line, or of an earlier one where that start is inside a comment or a literal, or
    /// follows a backslash.
    /// </summary>
    int RestartFor(int offset)
    {
        var restart = offset;
        while (restart > 0)
        {
            restart = Source.LastIndexOf('\n', restart - 1) + 1;
            if (restart == 0)
            {
                break;
            }

            // The last span to start before the line does. The line starts inside it when it ends
            // past there, and when it ran to the end of the source, which is where a comment or
            // a literal that was never closed ends: anything added there is inside it too
            var span = LowerBound(starts, restart) - 1;
            if (span >= 0 &&
                (ends[span] > restart || ends[span] == Source.Length))
            {
                // Back to where that span starts, and then to the start of its line
                restart = starts[span];
                continue;
            }

            if (restart < 2 ||
                Source[restart - 2] != '\\')
            {
                break;
            }

            restart--;
        }

        return restart;
    }

    /// <summary>
    /// Asked by a lexer at the top of its loop, once it is at or past <see cref="RejoinFrom"/>:
    /// whether everything from <paramref name="index"/> on is as the scan this one is made from
    /// has it. Where it is, the rest of that scan's spans are taken and the lexer is done.
    /// </summary>
    public bool TryRejoin(int index)
    {
        if (before is null ||
            Source[index - 1] != '\n')
        {
            return false;
        }

        var from = index - growth;
        if (before.skipped[from])
        {
            return false;
        }

        for (var span = LowerBound(before.starts, from); span < before.starts.Count; span++)
        {
            starts.Add(before.starts[span] + growth);
            ends.Add(before.ends[span] + growth);
            comments.Add(before.comments[span]);
        }

        Array.Copy(before.skipped, from, skipped, index, before.Source.Length - from);
        return true;
    }

    static List<int> EditedLineStarts(List<int> old, string edited, int prefix, int editEnd, int growth)
    {
        var found = new List<int>(old.Count + 8);
        // The lines that start before the edit, which the line breaks in front of it decide
        var index = 0;
        for (; index < old.Count && old[index] < Math.Max(prefix, 1); index++)
        {
            found.Add(old[index]);
        }

        // Those a line break in the edit, or the one in front of it, starts
        var search = Math.Max(prefix - 1, 0);
        while (search < editEnd)
        {
            search = edited.IndexOf('\n', search, editEnd - search);
            if (search < 0 ||
                search + 1 >= edited.Length)
            {
                break;
            }

            search++;
            found.Add(search);
        }

        // And those past it, moved
        index = LowerBound(old, editEnd - growth + 1);
        for (; index < old.Count; index++)
        {
            found.Add(old[index] + growth);
        }

        return found;
    }

    /// <summary>
    /// The index of the first value not less than <paramref name="value"/> in a sorted list.
    /// </summary>
    static int LowerBound(List<int> sorted, int value)
    {
        var low = 0;
        var high = sorted.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (sorted[middle] < value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    // Compared a block at a time, which is vectorised on every framework this targets, and then
    // a character at a time within the block that differs
    const int block = 4096;

    static int CommonPrefix(string left, string right, int limit)
    {
        var index = 0;
        while (index + block <= limit &&
               left.AsSpan(index, block).SequenceEqual(right.AsSpan(index, block)))
        {
            index += block;
        }

        while (index < limit &&
               left[index] == right[index])
        {
            index++;
        }

        return index;
    }

    static int CommonSuffix(string left, string right, int limit)
    {
        var count = 0;
        while (count + block <= limit &&
               left.AsSpan(left.Length - count - block, block).SequenceEqual(right.AsSpan(right.Length - count - block, block)))
        {
            count += block;
        }

        while (count < limit &&
               left[left.Length - count - 1] == right[right.Length - count - 1])
        {
            count++;
        }

        return count;
    }

    List<int>? lineStarts;
    string? eol;
    string? indentUnit;

    /// <summary>
    /// The offset each line starts at. A line break that ends the source starts no line.
    /// <para>
    /// Kept here, with <see cref="Eol"/> and <see cref="IndentUnit"/>, because they are facts
    /// about the same text the map is of, and a scan that is carried from one patch to the next
    /// carries them with it. Each is worked out when first asked, since a search that finds
    /// nothing to write needs only the first.
    /// </para>
    /// </summary>
    public List<int> LineStarts => lineStarts ??= BuildLineStarts();

    List<int> BuildLineStarts()
    {
        List<int> found = [0];
        var index = 0;
        while (true)
        {
            index = Source.IndexOf('\n', index);
            if (index < 0 ||
                index + 1 >= Source.Length)
            {
                return found;
            }

            index++;
            found.Add(index);
        }
    }

    /// <summary>
    /// The line break most of the file's lines end in, a tie going to <c>\r\n</c>, and the
    /// platform's own for a file with none.
    /// </summary>
    public string Eol => eol ??= DetectEol();

    string DetectEol()
    {
        var crlf = 0;
        var lf = 0;
        var starts = LineStarts;
        // Every line but the first starts after a line break, and the last line may end in one
        // that starts nothing
        for (var line = 1; line <= starts.Count; line++)
        {
            var end = line < starts.Count ? starts[line] : Source.Length;
            if (end == 0 ||
                Source[end - 1] != '\n')
            {
                continue;
            }

            if (end > 1 && Source[end - 2] == '\r')
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
    /// <para>
    /// "" when the file is too small to show a step, which leaves the choice to whoever asked.
    /// </para>
    /// </summary>
    public string IndentUnit => indentUnit ??= DetectIndentUnit();

    string DetectIndentUnit()
    {
        // Counted without a string a line: this is asked once a patch, of every line of the file,
        // and a file shows two or three different steps at most
        List<(string Step, int Count)> counts = [];
        var previousStart = 0;
        var previousLength = 0;
        foreach (var lineStart in LineStarts)
        {
            // Inside a comment or a literal the leading whitespace is content, not indentation.
            // A snapshot literal in particular is arbitrary text, and counting its lines would
            // measure the snapshot rather than the file
            if (!IsCode(lineStart))
            {
                continue;
            }

            var index = lineStart;
            while (index < Source.Length &&
                   (Source[index] == ' ' || Source[index] == '\t'))
            {
                index++;
            }

            // A blank line has no indentation of its own, and must not break the run either
            if (index >= Source.Length ||
                Source[index] == '\r' ||
                Source[index] == '\n')
            {
                continue;
            }

            var length = index - lineStart;
            // Only a line that indents further than the one above, by adding to what it already
            // had. Anything else is a dedent, or whitespace of a different kind, and neither
            // measures a step
            if (length > previousLength &&
                string.CompareOrdinal(Source, lineStart, Source, previousStart, previousLength) == 0)
            {
                Count(counts, lineStart + previousLength, length - previousLength);
            }

            previousStart = lineStart;
            previousLength = length;
        }

        var best = "";
        var bestCount = 0;
        foreach (var (step, count) in counts)
        {
            if (bestCount == 0 ||
                count > bestCount ||
                count == bestCount && Closer(step, best))
            {
                best = step;
                bestCount = count;
            }
        }

        return best;

        // A tie goes to the shorter step, since a longer one is two levels taken at once, and
        // then to ordinal order so the answer cannot depend on the order they were met in
        static bool Closer(string candidate, string current) =>
            candidate.Length == current.Length
                ? string.CompareOrdinal(candidate, current) < 0
                : candidate.Length < current.Length;
    }

    void Count(List<(string Step, int Count)> counts, int start, int length)
    {
        for (var index = 0; index < counts.Count; index++)
        {
            var (step, count) = counts[index];
            if (step.Length == length &&
                string.CompareOrdinal(Source, start, step, 0, length) == 0)
            {
                counts[index] = (step, count + 1);
                return;
            }
        }

        counts.Add((Source.Substring(start, length), 1));
    }

    /// <summary>
    /// True when the offset is outside every comment, string and char literal.
    /// </summary>
    public bool IsCode(int index) =>
        index >= 0 &&
        index < Source.Length &&
        !skipped[index];

    /// <summary>
    /// When a comment or literal starts at <paramref name="index"/>, <paramref name="end"/> is the
    /// offset just past it. Lets a structural scan step over trivia without lexing it again.
    /// </summary>
    public bool TryGetSkip(int index, out int end)
    {
        var span = SpanStartingAt(index);
        end = span < 0 ? 0 : ends[span];
        return span >= 0;
    }

    /// <summary>
    /// <see cref="TryGetSkip"/> for a scan working backwards: when a comment or literal ends at
    /// <paramref name="end"/>, <paramref name="start"/> is where it began.
    /// </summary>
    public bool TryGetSkipEndingAt(int end, out int start)
    {
        var span = SpanEndingAt(end);
        start = span < 0 ? 0 : starts[span];
        return span >= 0;
    }

    /// <summary>
    /// As <see cref="TryGetSkip"/>, but only for comments.
    /// </summary>
    public bool TryGetCommentSkip(int index, out int end)
    {
        var span = SpanStartingAt(index);
        if (span < 0 ||
            !comments[span])
        {
            end = 0;
            return false;
        }

        end = ends[span];
        return true;
    }

    /// <summary>
    /// True when a comment ends at <paramref name="end"/>, with <paramref name="start"/> set to
    /// where it began. Only comments: a literal is content, and trimming one off a span would be
    /// trimming off the value.
    /// </summary>
    public bool TryGetCommentEndingAt(int end, out int start)
    {
        var span = SpanEndingAt(end);
        if (span < 0 ||
            !comments[span])
        {
            start = 0;
            return false;
        }

        start = starts[span];
        return true;
    }

    /// <summary>
    /// Which span starts at <paramref name="index"/>, or -1. These are asked of nearly every
    /// offset a search steps over, and nearly all of those are code, which the map answers
    /// without a search: a span cannot start on an offset that is.
    /// </summary>
    int SpanStartingAt(int index)
    {
        if (index < 0 ||
            index >= Source.Length ||
            !skipped[index])
        {
            return -1;
        }

        return starts.BinarySearch(index);
    }

    /// <summary>
    /// Which span ends just before <paramref name="end"/>, or -1. The offset before it is the
    /// span's last, so it is not code either.
    /// </summary>
    int SpanEndingAt(int end)
    {
        if (end <= 0 ||
            end > Source.Length ||
            !skipped[end - 1])
        {
            return -1;
        }

        return ends.BinarySearch(end);
    }

    /// <summary>
    /// Advances past whitespace and comments.
    /// </summary>
    public void SkipTrivia(ref int index)
    {
        while (index < Source.Length)
        {
            if (char.IsWhiteSpace(Source[index]))
            {
                index++;
                continue;
            }

            if (TryGetCommentSkip(index, out var end))
            {
                index = end;
                continue;
            }

            return;
        }
    }

    /// <summary>
    /// The offset of the last character before <paramref name="index"/> that is code: not
    /// whitespace, and not inside a comment or a literal. -1 when there is none.
    /// <para>
    /// Literals are stepped over as well as comments, which matters to the caller that reads what
    /// it lands on. A literal receiver - <c>"text".Verify(...)</c> - is skipped whole rather than
    /// reported, so what comes back is whatever precedes it. That still answers what
    /// <see cref="SourceLanguage.IsDeclaration"/> and the foreign receiver check are asking, since
    /// a literal receiver is not the verify entry point either way.
    /// </para>
    /// </summary>
    public int PreviousSignificant(int index)
    {
        index--;
        while (index >= 0 &&
               (char.IsWhiteSpace(Source[index]) || skipped[index]))
        {
            index--;
        }

        return index;
    }

    /// <summary>
    /// True when the identifier at <paramref name="nameStart"/> is being declared rather than
    /// called.
    /// </summary>
    public bool IsDeclaration(int nameStart) =>
        Language.IsDeclaration(this, nameStart);

    public bool IsIdentifierChar(char ch) =>
        Language.IsIdentifierChar(ch);

    /// <summary>
    /// The start of the identifier ending at <paramref name="end"/>, which must be an identifier
    /// character.
    /// </summary>
    public int WordStart(int end)
    {
        var start = end;
        while (start > 0 &&
               IsIdentifierChar(Source[start - 1]))
        {
            start--;
        }

        return start;
    }

    /// <summary>
    /// The whole identifier ending at <paramref name="end"/>, which must be an identifier
    /// character.
    /// </summary>
    public string WordEndingAt(int end)
    {
        var start = WordStart(end);
        return Source.Substring(start, end - start + 1);
    }
}
