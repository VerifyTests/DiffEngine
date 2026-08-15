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
/// </summary>
sealed class SourceScan(SourceLanguage language, string source)
{
    readonly bool[] code = new bool[source.Length];

    /// <summary>
    /// Start of a comment or literal to the offset just past it.
    /// </summary>
    readonly Dictionary<int, int> skips = new();

    /// <summary>
    /// The same spans keyed the other way round, for a scan working backwards. Ends are unique
    /// because the spans cannot overlap.
    /// </summary>
    readonly Dictionary<int, int> skipEnds = new();

    /// <summary>
    /// Which of the spans are comments. A literal is content, so the two cannot be treated alike
    /// where trivia is being stepped over or trimmed off.
    /// </summary>
    readonly HashSet<int> comments = [];

    public SourceLanguage Language { get; } = language;

    public string Source { get; } = source;

    /// <summary>
    /// Records a comment or literal spanning <paramref name="start"/> to <paramref name="end"/>.
    /// Called by the lexer on <see cref="SourceLanguage"/> as it fills the map.
    /// </summary>
    public void AddSkip(int start, int end, bool comment)
    {
        skips.Add(start, end);
        skipEnds[end] = start;
        if (comment)
        {
            comments.Add(start);
        }
    }

    public void MarkCode(int index) => code[index] = true;

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
    /// As <see cref="TryGetSkip"/>, but only for comments.
    /// </summary>
    public bool TryGetCommentSkip(int index, out int end) =>
        skips.TryGetValue(index, out end) &&
        comments.Contains(index);

    /// <summary>
    /// True when a comment ends at <paramref name="end"/>, with <paramref name="start"/> set to
    /// where it began. Only comments: a literal is content, and trimming one off a span would be
    /// trimming off the value.
    /// </summary>
    public bool TryGetCommentEndingAt(int end, out int start) =>
        skipEnds.TryGetValue(end, out start) &&
        comments.Contains(start);

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
    /// The offset of the last character before <paramref name="index"/> that is neither
    /// whitespace nor inside a comment, or -1 when there is none.
    /// </summary>
    public int PreviousSignificant(int index)
    {
        index--;
        while (index >= 0 &&
               (char.IsWhiteSpace(Source[index]) || !code[index]))
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
