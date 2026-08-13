/// <summary>
/// One side of a comparison that is a picture rather than text.
/// <para>
/// Deliberately not the bytes. A head that draws pixels reads the file itself and caches what it
/// decoded, so holding a queue of images in the session would be megabytes kept alive to answer a
/// question a hash already answers.
/// </para>
/// </summary>
/// <param name="Header">Null when the bytes are not a format the viewer recognizes.</param>
/// <param name="Hash">
/// Null when the content could not be read, which is the difference between "these differ" and
/// "these could not be compared".
/// </param>
readonly record struct ImageFile(string Path, long Length, ImageHeader? Header, string? Hash)
{
    public static ImageFile Read(string path, byte[] bytes) =>
        new(
            path,
            bytes.Length,
            ImageHeader.TryRead(bytes, out var header) ? header : null,
            Convert.ToHexString(SHA256.HashData(bytes)));

    /// <summary>
    /// A file that is an image by its name and nothing more: what is left when the bytes could not
    /// be read at all. Still an image side, so the pane says so rather than showing empty text.
    /// </summary>
    public static ImageFile Unread(string path) =>
        new(path, 0, null, null);
}
