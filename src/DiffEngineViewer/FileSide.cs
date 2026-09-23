/// <summary>
/// Freshness token for a file a move or delete entry renders: enough to tell "unchanged since the
/// last pump" without reading the content again.
/// </summary>
readonly record struct FileStamp(long WriteTicksUtc, long Length);

/// <summary>
/// One guarded read of a file a side of the diff points at, as whichever of text or picture its
/// extension says it is. Never throws: the poller that materializes these must survive a file
/// vanishing mid-pump — a throw there closes the window as "owner gone" — so a missing or locked
/// file degrades to an empty side plus a warning.
/// </summary>
readonly record struct FileSide(string Text, FileStamp? Stamp, string? Warning, ImageFile? Image)
{
    /// <summary>
    /// Text that never came off a disk, which is what every test and every in-memory caller has.
    /// </summary>
    public static FileSide OfText(string text) =>
        new(text, null, null, null);

    public static FileSide Read(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                // Normal for a move: a brand new snapshot has no verified file yet. An absent
                // image is absent rather than unreadable, so it renders as the empty side it is.
                return new("", null, null, null);
            }

            var stamp = new FileStamp(info.LastWriteTimeUtc.Ticks, info.Length);
            if (!ImageExtensions.Is(path))
            {
                return new(ReadText(path), stamp, null, null);
            }

            return new("", stamp, null, ImageFile.Read(path, ReadBytes(path)));
        }
        catch (Exception exception)
        {
            return new("", null, $"Could not read {path}. {exception.Message}", Unread(path));
        }
    }

    /// <summary>
    /// A locked or unreadable image is still an image, so the pane names the picture it could not
    /// read rather than presenting it as text that happens to be empty.
    /// </summary>
    static ImageFile? Unread(string path) =>
        ImageExtensions.Is(path) ? ImageFile.Unread(path) : null;

    /// <summary>
    /// Every read of a pending file shares it with writers and deleters. A test process rewrites
    /// or deletes its received file on every run, and an accept moves it: opened with read sharing
    /// alone, a read that happened to overlap either failed it with "being used by another
    /// process", and the accept-all worker does not retry.
    /// </summary>
    static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    public static byte[] ReadBytes(string path)
    {
        using var stream = OpenShared(path);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>
    /// As File.ReadAllText reads, byte order mark included in the detection, over a shared stream.
    /// </summary>
    static string ReadText(string path)
    {
        using var reader = new StreamReader(OpenShared(path), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public static FileStamp? StampOf(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return null;
            }

            return new FileStamp(info.LastWriteTimeUtc.Ticks, info.Length);
        }
        catch
        {
            return null;
        }
    }
}
