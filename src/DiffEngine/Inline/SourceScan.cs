using System.Buffers;

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
