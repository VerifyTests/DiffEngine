/// <summary>
/// The extensions DiffEngineViewer reads as documents when it has its documents folder, which the
/// dotnet tool and the copy inside DiffEngineTray do and the copy bundled in DiffEngine does not.
/// <para>
/// Linked into the viewer rather than written on both sides, for the reason
/// <see cref="ImageExtensions"/> is: DiffEngine offers a viewer that has the folder as the diff tool
/// for the paged ones, and the two lists drifting apart means a window full of mojibake.
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
    /// Text that also draws as a picture. Already a text extension to DiffEngine, so it is routed
    /// nowhere new, and a viewer without the folder shows it as the text it is.
    /// </summary>
    public const string Drawn = ".svg";

    static HashSet<string> paged = [with(Paged, StringComparer.OrdinalIgnoreCase)];

    public static bool IsPaged(string path) =>
        paged.Contains(Path.GetExtension(path));

    public static bool IsDrawn(string path) =>
        Path.GetExtension(path).Equals(Drawn, StringComparison.OrdinalIgnoreCase);

    public static bool Is(string path) =>
        IsPaged(path) ||
        IsDrawn(path);
}
