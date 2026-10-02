/// <summary>
/// What a document side is, decided by its extension the way an image's is, so a side whose file
/// cannot be read still says what it would have been.
/// </summary>
enum DocumentFormat
{
    Pdf,
    Word,
    Excel,
    PowerPoint,
    Svg
}

/// <summary>
/// One side of a comparison that is a document: a PDF, an Office file, or an SVG. The
/// <see cref="ImageFile"/> of documents, and like it deliberately not the bytes.
/// <para>
/// Only read this way by a viewer with its documents folder (<see cref="DocumentPlugin"/>). The
/// copy bundled in DiffEngine has none, and reads these files exactly as it always has.
/// </para>
/// </summary>
/// <param name="Hash">
/// The content's SHA256, or null when the bytes could not be read. Also what the side's text and
/// pages are kept under, so a re-run that rewrites the file is a different document rather than a
/// stale view of the same one, and two sides holding the same bytes are read and drawn once.
/// </param>
/// <param name="Reading">
/// The text is still being read out of the document, which happens on
/// <see cref="DocumentWatch"/>'s thread rather than here. Never for an SVG, whose text is the file.
/// </param>
/// <param name="Unreadable">Why no text could be read out of the document, when none could.</param>
readonly record struct DocumentFile(
    string Path,
    long Length,
    DocumentFormat Format,
    string? Hash,
    bool Reading = false,
    string? Unreadable = null)
{
    /// <summary>
    /// Text that also draws, rather than a document whose text is read out of it.
    /// </summary>
    public bool IsDrawn => Format == DocumentFormat.Svg;

    /// <summary>
    /// Whether this side's text is the document's, rather than nothing yet or nothing at all.
    /// </summary>
    public bool HasText =>
        Hash is not null &&
        !Reading &&
        Unreadable is null;

    /// <summary>
    /// A file that is a document by its name and nothing more: what is left when the bytes could
    /// not be read at all.
    /// </summary>
    public static DocumentFile Unread(string path) =>
        new(path, 0, FormatOf(path), null);

    public static DocumentFormat FormatOf(string path) =>
        System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf" => DocumentFormat.Pdf,
            ".docx" => DocumentFormat.Word,
            ".xlsx" => DocumentFormat.Excel,
            ".pptx" => DocumentFormat.PowerPoint,
            _ => DocumentFormat.Svg
        };

    public static string Name(DocumentFormat format) =>
        format switch
        {
            DocumentFormat.Pdf => "PDF",
            DocumentFormat.Word => "Word",
            DocumentFormat.Excel => "Excel",
            DocumentFormat.PowerPoint => "PowerPoint",
            _ => "SVG"
        };
}
