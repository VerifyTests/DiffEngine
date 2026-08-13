/// <summary>
/// Decoded pictures for the panes, keyed by the path the screen model handed over and invalidated
/// by the file's write time and length — the same freshness test the queue poller uses, so a re-run
/// that rewrites a received image refreshes the pane rather than leaving the old one up.
/// <para>
/// A cache and not a convenience: <c>OnPaint</c> runs on every wheel notch and every resize, and
/// decoding a picture per frame is what turns a window that is merely showing something into one
/// that is busy.
/// </para>
/// </summary>
sealed class ImageCache : IDisposable
{
    readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A null <paramref name="Image"/> is a remembered failure. Kept rather than dropped, so a file
    /// this machine cannot decode is attempted once instead of once per frame.
    /// </summary>
    record Entry(long WriteTicksUtc, long Length, Image? Image);

    public Image? Get(string path)
    {
        long ticks;
        long length;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                Forget(path);
                return null;
            }

            ticks = info.LastWriteTimeUtc.Ticks;
            length = info.Length;
        }
        catch
        {
            // A file that cannot be stat'd cannot be drawn, and the rows have already said what
            // the model made of it.
            Forget(path);
            return null;
        }

        if (entries.TryGetValue(path, out var entry))
        {
            if (entry.WriteTicksUtc == ticks &&
                entry.Length == length)
            {
                return entry.Image;
            }

            Forget(path);
        }

        var image = Load(path);
        entries.Add(path, new(ticks, length, image));
        return image;
    }

    static Image? Load(string path)
    {
        try
        {
            // Decoded from a copy of the bytes and then copied again. GDI+ holds on to the stream
            // it was handed for as long as the image lives, and a viewer keeping a handle on the
            // received file is one that blocks the accept it exists to perform.
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var decoded = new Bitmap(stream);
            return new Bitmap(decoded);
        }
        catch
        {
            return null;
        }
    }

    void Forget(string path)
    {
        if (entries.Remove(path, out var entry))
        {
            entry.Image?.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var entry in entries.Values)
        {
            entry.Image?.Dispose();
        }

        entries.Clear();
    }
}
