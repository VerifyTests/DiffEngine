/// <summary>
/// The extensions DiffEngineViewer reads as documents when it has its documents folder, which the
/// dotnet tool and the copy inside DiffEngineTray do and the copy bundled in DiffEngine does not.
/// <para>
/// Linked into the viewer rather than written on both sides, for the reason
/// <see cref="ImageExtensions"/> is: DiffEngine offers a viewer that has the folder as the diff tool
/// for the routed ones, and the two lists drifting apart means a window full of mojibake.
/// </para>
/// </summary>
static class DocumentExtensions
{
    /// <summary>
    /// Binary, so a viewer only reads them with the documents folder beside it, and DiffEngine only
    /// sends them to one that has it.
    /// </summary>
    public static readonly string[] Paged =
    [
        ".docx",
        ".pdf",
        ".pptx",
        ".xlsx"
    ];

    /// <summary>
    /// Maps whose text is the file, drawn by GeoConvert. Routed as the paged ones are, which is what
    /// offers a viewer for those that are no text extension to DiffEngine (.topojson and .wkt). The
    /// rest are text extensions, so like <see cref="Drawn"/> they go to any text tool, and a viewer
    /// without the folder shows them as the text they are.
    /// </summary>
    public static readonly string[] TextMaps =
    [
        ".geojson",
        ".gpx",
        ".kml",
        ".topojson",
        ".wkt"
    ];

    /// <summary>
    /// Maps whose text is what they read as in GeoJSON. Shapefile is left out: it is a set of files
    /// and a snapshot is one, and CSV and Parquet hold more than maps.
    /// </summary>
    public static readonly string[] BinaryMaps =
    [
        ".fgb",
        ".geoparquet",
        ".kmz",
        ".wkb"
    ];

    /// <summary>
    /// Text that also draws as a picture. Already a text extension to DiffEngine, so it is routed
    /// nowhere new, and a viewer without the folder shows it as the text it is.
    /// </summary>
    public const string Drawn = ".svg";

    /// <summary>
    /// What DiffEngine offers a viewer with the folder for.
    /// </summary>
    public static readonly string[] Routed = [.. Paged, .. TextMaps, .. BinaryMaps];

    static HashSet<string> routed = [with(Routed, StringComparer.OrdinalIgnoreCase)];

    public static bool Is(string path)
    {
        var extension = Path.GetExtension(path);
        return routed.Contains(extension) ||
               extension.Equals(Drawn, StringComparison.OrdinalIgnoreCase);
    }
}
