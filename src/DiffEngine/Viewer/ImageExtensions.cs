/// <summary>
/// The image extensions DiffEngineViewer renders as pictures, and therefore the ones DiffEngine
/// offers it as a binary diff tool for.
/// <para>
/// Lives in DiffEngine and is linked into the viewer rather than written on both sides. The viewer
/// cannot reference DiffEngine — DiffEngine publishes and embeds the viewer heads, so the reference
/// would be a cycle — and the two lists drifting apart means registering the tool for an extension
/// its renderer treats as text, which is a window full of mojibake.
/// </para>
/// <para>
/// Decided by extension rather than by sniffing the bytes, because the decision has to be made for
/// a file that does not exist yet: the expected side of a brand new image snapshot has nothing to
/// sniff, and it still has to render as a missing image rather than as empty text.
/// </para>
/// </summary>
static class ImageExtensions
{
    public static readonly string[] All =
    [
        ".bmp",
        ".gif",
        ".ico",
        ".jpeg",
        ".jpg",
        ".png",
        ".webp"
    ];

    static readonly HashSet<string> lookup = new(All, StringComparer.OrdinalIgnoreCase);

    public static bool Is(string path) =>
        lookup.Contains(Path.GetExtension(path));
}
