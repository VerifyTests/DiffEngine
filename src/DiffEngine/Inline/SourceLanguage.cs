namespace DiffEngine;

/// <summary>
/// The language of the source file a patch is applied to: how a snapshot is written as a string
/// literal, how one is read back, and what a scan has to step over to find a call.
/// <para>
/// Chosen by file extension rather than carried on the patch, because the file already says: a
/// patch names the source file it edits, and a producer that had to state the language as well
/// could state one the file is not.
/// </para>
/// </summary>
public abstract class SourceLanguage
{
    public static SourceLanguage CSharp { get; } = new CsLanguage();

    public static SourceLanguage FSharp { get; } = new FsLanguage();

    /// <summary>
    /// The language of <paramref name="path"/>, by extension. Anything that is not F# is treated
    /// as C#: C# is the only other language a producer targets today, and an unknown extension on
    /// a file full of C# is far more likely than a language with no support here at all.
    /// </summary>
    public static SourceLanguage ForFile(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.Equals(extension, ".fs", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".fsx", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".fsi", StringComparison.OrdinalIgnoreCase))
        {
            return FSharp;
        }

        return CSharp;
    }

    /// <summary>
    /// Renders <paramref name="content"/> (\n newlines) as a string literal expression in this
    /// language.
    /// </summary>
    /// <param name="content">Snapshot text with \n newlines.</param>
    /// <param name="indent">
    /// The indentation of the line the literal will start on. A prefix for the content lines where
    /// the multi-line form indents them (C#), and a floor the result has to clear where it cannot
    /// (F#, whose content is verbatim - see <see cref="FsStringLiteral.ClearsOffsideLine"/>).
    /// </param>
    /// <param name="eol">The target file's line ending ("\r\n" or "\n").</param>
    public abstract string Render(string content, string indent, string eol);

    /// <summary>
    /// Parses a string literal expression back to its runtime value. Returns false for
    /// interpolated strings, concatenations, or any other expression. Newlines in the returned
    /// value are normalized to \n.
    /// </summary>
    public abstract bool TryParse(string expression, [NotNullWhen(true)] out string? value);

    /// <summary>
    /// Lexes <paramref name="source"/> into the map every search then reads.
    /// </summary>
    internal abstract SourceScan Scan(string source);

    internal abstract bool IsIdentifierChar(char ch);

    /// <summary>
    /// True when the identifier at <paramref name="nameStart"/> is being declared rather than
    /// called. The two are otherwise identical - name, parens, body - so the tell is what comes
    /// in front of the name, and that is where the languages differ most.
    /// </summary>
    internal abstract bool IsDeclaration(SourceScan scan, int nameStart);

    /// <summary>
    /// How an argument is bound to a parameter by name, up to and including the separator:
    /// <c>expected: </c> in C#, <c>expected = </c> in F#.
    /// </summary>
    internal abstract string NamePrefix(string name);

    /// <summary>
    /// The character that follows an argument name.
    /// </summary>
    internal abstract char NameSeparator { get; }

    /// <summary>
    /// Whether a multi-line literal goes on the line below the open paren. True where the literal's
    /// opening delimiter has content to line up with, and false where the content is verbatim and
    /// so has to start hard against the delimiter.
    /// </summary>
    internal virtual bool LiteralOnOwnLine => true;

    /// <summary>
    /// Which indentation <see cref="Render"/> is given, out of the two the call site has: the line
    /// a literal of its own would sit on, or the statement the literal ends up inside.
    /// <para>
    /// A language that indents its literal is told where to indent it to. One that cannot is told
    /// what its result has to clear, which is the statement's indentation rather than the line's,
    /// because that is the column F#'s layout rule is measured from.
    /// </para>
    /// </summary>
    internal string IndentFor(string statementIndent, string ownLineIndent) =>
        LiteralOnOwnLine ? ownLineIndent : statementIndent;

    /// <summary>
    /// A chained call that a Snapshot call has to be appended in front of rather than after, or
    /// null when the end of the chain is always the insertion point.
    /// </summary>
    internal virtual string? ChainTerminator => null;

    /// <summary>
    /// Whether a patch from this language carries the source text of the expected argument, which
    /// is to say whether the compiler honours <see cref="CallerArgumentExpressionAttribute"/>.
    /// <para>
    /// That expression is what a patch is anchored to: the call whose argument is still the text
    /// the test run saw is the call to rewrite, whatever moved around it. Without one, all a patch
    /// has is a line hint and the search outward from it, and a literal that differs has to be
    /// taken as the snapshot that changed rather than as a conflict - the alternative being that
    /// an inline snapshot can be accepted once and never updated.
    /// </para>
    /// </summary>
    internal virtual bool SuppliesArgumentExpressions => true;

    /// <summary>
    /// Advances past a type argument list, so Foo&lt;Bar&gt;(...) is located as readily as
    /// Foo(...). Only the characters a type argument list can hold are accepted, and the caller
    /// still has to find a '(' after it, so a comparison cannot be mistaken for one.
    /// </summary>
    internal bool TrySkipTypeArguments(string source, ref int index)
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
                IsTypeArgumentChar(ch))
            {
                cursor++;
                continue;
            }

            return false;
        }

        return false;
    }

    internal virtual bool IsTypeArgumentChar(char ch) =>
        ch is ',' or '.' or '?' or '[' or ']' or ':' or ' ' or '\t';

    /// <summary>
    /// Reads an <c>name = </c> or <c>name: </c> prefix off the front of an argument, leaving
    /// <paramref name="start"/> on the expression itself.
    /// </summary>
    internal bool TryStripArgumentName(string source, ref int start, out string name)
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

        var separator = NameSeparator;
        if (index >= source.Length ||
            source[index] != separator ||
            // A doubled separator is an operator (:: in C#, == in F#), not a name
            index + 1 < source.Length && source[index + 1] == separator)
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

    internal static string NormalizeNewlines(string value) =>
        value
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');
}
