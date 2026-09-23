/// <summary>
/// Decoded pictures for the panes, keyed by the path the screen model handed over and invalidated
/// by the file's write time and length — the same freshness test the queue poller uses, so a re-run
/// that rewrites a received image refreshes the pane rather than leaving the old one up — and by
/// the content hash the model carries, since a same-length rewrite inside the file system's
/// timestamp granularity looks unchanged to a stat.
/// <para>
/// Only what the current screen shows is kept (<see cref="Keep"/>). Every picture ever drawn stayed
/// decoded otherwise, for the life of a process the tray can keep hidden for days: ten accepted
/// 400 by 300 pairs held 9 MB of unmanaged memory the collector does not see.
/// </para>
/// <para>
/// A cache and not a convenience: <c>OnPaint</c> runs on every wheel notch and every resize. Two
/// things are held for that. The decoded picture, which is decoded off the UI thread when there is
/// somewhere to post the result (<see cref="Get(string, string?, Action)"/>), because decoding a
/// pair of 2000 by 1500 pictures held the window for over 100 ms. And the picture as painted, at
/// the size it was painted (<see cref="Composite"/>): scaling it and drawing the checkerboard under
/// it on every paint cost 46 to 66 ms a paint for that pair, where copying the result costs
/// almost nothing.
/// </para>
/// <para>
/// Everything here is touched only from the UI thread. A background decode hands its result back
/// through <c>post</c>, which is the canvas's BeginInvoke.
/// </para>
/// </summary>
sealed class ImageCache(Action<Action>? post = null) : IDisposable
{
    readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Decodes started and not yet handed back, by path, with the stamp each was started for.
    /// </summary>
    readonly Dictionary<string, Stamp> pending = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The paths the last <see cref="Keep"/> named. A decode that finishes for a path no longer on
    /// screen is thrown away rather than cached. Null until the first Keep, which takes everything.
    /// </summary>
    HashSet<string>? wanted;

    bool disposed;

    record struct Stamp(long WriteTicksUtc, long Length, string? Hash);

    /// <summary>
    /// A null <paramref name="Image"/> is a remembered failure. Kept rather than dropped, so a file
    /// this machine cannot decode is attempted once instead of once per frame.
    /// </summary>
    sealed record Entry(Stamp Stamp, Image? Image)
    {
        /// <summary>
        /// The picture as last painted, at the size it was painted: see <see cref="ImageCache.Composite"/>.
        /// </summary>
        public Bitmap? Composite { get; set; }

        public void Dispose()
        {
            Image?.Dispose();
            Composite?.Dispose();
        }
    }

    /// <summary>
    /// Drops every picture not at one of <paramref name="paths"/>, which is what is on screen.
    /// </summary>
    public void Keep(IReadOnlyCollection<string> paths)
    {
        wanted = [with(StringComparer.OrdinalIgnoreCase), .. paths];
        foreach (var path in entries.Keys.Where(_ => !wanted.Contains(_)).ToList())
        {
            Forget(path);
        }
    }

    /// <summary>
    /// The picture at <paramref name="path"/>, decoded here and now if it is not cached. For a
    /// caller that has to have it this frame, such as a capture.
    /// </summary>
    public Image? Get(string path, string? hash)
    {
        if (!TryStamp(path, hash, out var stamp))
        {
            return null;
        }

        if (TryCached(path, stamp, out var cached))
        {
            return cached;
        }

        var image = Load(path);
        // Supersedes a background decode of the same file, whose result would only replace this
        pending.Remove(path);
        entries.Add(path, new(stamp, image));
        return image;
    }

    /// <summary>
    /// The picture at <paramref name="path"/> if it is decoded, and otherwise null while it is
    /// decoded on the pool, after which <paramref name="loaded"/> is called on the UI thread. With
    /// nowhere to post the result this decodes here and now, as <see cref="Get(string, string?)"/>
    /// does.
    /// </summary>
    public Image? Get(string path, string? hash, Action loaded)
    {
        if (post is null)
        {
            return Get(path, hash);
        }

        if (!TryStamp(path, hash, out var stamp))
        {
            return null;
        }

        if (TryCached(path, stamp, out var cached))
        {
            return cached;
        }

        if (pending.TryGetValue(path, out var started) &&
            started == stamp)
        {
            return null;
        }

        pending[path] = stamp;
        Task.Run(() => Load(path))
            .ContinueWith(
                task =>
                {
                    var image = task.Result;
                    try
                    {
                        post(() => Loaded(path, stamp, image, loaded));
                    }
                    catch (InvalidOperationException)
                    {
                        // The window went away before the decode finished, and with it the thread
                        // this would have been handed to
                        image?.Dispose();
                    }
                },
                Cancel.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        return null;
    }

    void Loaded(string path, Stamp stamp, Image? image, Action loaded)
    {
        // Only the decode the path is still waiting on. An older one, for a stamp the file has
        // since moved past, would put back the picture the newer decode is replacing
        if (disposed ||
            !pending.TryGetValue(path, out var started) ||
            started != stamp ||
            (wanted is not null && !wanted.Contains(path)))
        {
            image?.Dispose();
            return;
        }

        pending.Remove(path);
        Forget(path);
        entries.Add(path, new(stamp, image));
        loaded();
    }

    /// <summary>
    /// The picture at <paramref name="path"/> as it is painted at <paramref name="size"/>, built
    /// by <paramref name="build"/> from the decoded picture the first time that size is asked for
    /// and kept until another size is, or the picture goes. Null when the picture is not decoded.
    /// </summary>
    public Bitmap? Composite(string path, Size size, Func<Image, Size, Bitmap> build)
    {
        if (!entries.TryGetValue(path, out var entry) ||
            entry.Image is null)
        {
            return null;
        }

        if (entry.Composite is { } composite &&
            composite.Size == size)
        {
            return composite;
        }

        entry.Composite?.Dispose();
        entry.Composite = build(entry.Image, size);
        Composed++;
        return entry.Composite;
    }

    /// <summary>
    /// How many composites have been built, for the tests that pin how often that happens.
    /// </summary>
    internal int Composed { get; private set; }

    bool TryStamp(string path, string? hash, out Stamp stamp)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Exists)
            {
                stamp = new(info.LastWriteTimeUtc.Ticks, info.Length, hash);
                return true;
            }
        }
        catch
        {
            // A file that cannot be stat'd cannot be drawn, and the rows have already said what
            // the model made of it.
        }

        Forget(path);
        stamp = default;
        return false;
    }

    bool TryCached(string path, Stamp stamp, out Image? image)
    {
        if (entries.TryGetValue(path, out var entry))
        {
            if (entry.Stamp == stamp)
            {
                image = entry.Image;
                return true;
            }

            Forget(path);
        }

        image = null;
        return false;
    }

    static Image? Load(string path)
    {
        try
        {
            // Decoded from a copy of the bytes and then copied again. GDI+ holds on to the stream
            // it was handed for as long as the image lives, and a viewer keeping a handle on the
            // received file is one that blocks the accept it exists to perform.
            using var stream = new MemoryStream(FileSide.ReadBytes(path));
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
            entry.Dispose();
        }
    }

    public void Dispose()
    {
        disposed = true;
        foreach (var entry in entries.Values)
        {
            entry.Dispose();
        }

        entries.Clear();
        pending.Clear();
    }
}
