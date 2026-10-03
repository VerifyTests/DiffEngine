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
    Svg,
    GeoJson,
    TopoJson,
    Kml,
    Kmz,
    Gpx,
    Wkt,
    Wkb,
    FlatGeobuf,
    GeoParquet
}

/// <summary>
/// One side of a comparison that is a document: a PDF, an Office file, an SVG or a map. The
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
/// <see cref="DocumentWatch"/>'s thread rather than here. Never for one whose text is the file.
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
    /// One picture rather than pages: an SVG or a map.
    /// </summary>
    public bool IsDrawn =>
        Format is not (
            DocumentFormat.Pdf or
            DocumentFormat.Word or
            DocumentFormat.Excel or
            DocumentFormat.PowerPoint);

    /// <summary>
    /// Its text is the file, read as any text file is, rather than read out of it.
    /// </summary>
    public bool IsSource =>
        Format is
            DocumentFormat.Svg or
            DocumentFormat.GeoJson or
            DocumentFormat.TopoJson or
            DocumentFormat.Kml or
            DocumentFormat.Gpx or
            DocumentFormat.Wkt;

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
            ".geojson" => DocumentFormat.GeoJson,
            ".topojson" => DocumentFormat.TopoJson,
            ".kml" => DocumentFormat.Kml,
            ".kmz" => DocumentFormat.Kmz,
            ".gpx" => DocumentFormat.Gpx,
            ".wkt" => DocumentFormat.Wkt,
            ".wkb" => DocumentFormat.Wkb,
            ".fgb" => DocumentFormat.FlatGeobuf,
            ".geoparquet" => DocumentFormat.GeoParquet,
            _ => DocumentFormat.Svg
        };

    public static string Name(DocumentFormat format) =>
        format switch
        {
            DocumentFormat.Pdf => "PDF",
            DocumentFormat.Word => "Word",
            DocumentFormat.Excel => "Excel",
            DocumentFormat.PowerPoint => "PowerPoint",
            DocumentFormat.GeoJson => "GeoJSON map",
            DocumentFormat.TopoJson => "TopoJSON map",
            DocumentFormat.Kml => "KML map",
            DocumentFormat.Kmz => "KMZ map",
            DocumentFormat.Gpx => "GPX map",
            DocumentFormat.Wkt => "WKT map",
            DocumentFormat.Wkb => "WKB map",
            DocumentFormat.FlatGeobuf => "FlatGeobuf map",
            DocumentFormat.GeoParquet => "GeoParquet map",
            _ => "SVG"
        };
}
