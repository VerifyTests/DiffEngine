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
/// things are held for that. The decoded picture, because decoding a pair of 2000 by 1500 pictures
/// held the window for over 100 ms. And the picture as painted, at the size it was painted
/// (<see cref="Composite(string, Size, Func{Image, Size, Bitmap})"/>): scaling it and drawing the
/// checkerboard under it on every paint cost 46 to 66 ms a paint for that pair, where copying the
/// result costs almost nothing. One such at a time for a picture: fitted, or, while it is enlarged
/// to no more than half its own size, the whole of it at that size for the part that shows to be
/// copied out of. Past that nothing more is held, since the whole of it would be up to the
/// decoded picture's size again.
/// </para>
/// <para>
/// Both are made off the UI thread when there is somewhere to post the result, which the window
/// gives and a capture does not. Composing once per size still cost a page of a document 30 ms or
/// more on the UI thread, and a resize asks for a new size on every frame of the drag.
/// </para>
/// <para>
/// Everything here is touched only from the UI thread, apart from an entry's decoded picture,
/// which a compose reads on the pool. A background job hands its result back through <c>post</c>,
/// which is the canvas's BeginInvoke.
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
    /// <para>
    /// A class rather than a record: two entries for the same file at the same stamp are still two,
    /// and a compose finishing has to know whether the entry it was started for is the one cached.
    /// </para>
    /// </summary>
    sealed class Entry(Stamp stamp, Image? image)
    {
        public Stamp Stamp { get; } = stamp;

        public Image? Image { get; } = image;

        /// <summary>
        /// The picture as last painted, at the size it was painted: see
        /// <see cref="ImageCache.Composite(string, Size, Func{Image, Size, Bitmap})"/>.
        /// </summary>
        public Bitmap? Composite { get; set; }

        /// <summary>
        /// What made <see cref="Composite"/>. A picture is composed two ways, fitted over its
        /// checkerboard and enlarged with nothing under it, and one of those at the size the
        /// other is asked for is still not the other.
        /// </summary>
        public Func<Image, Size, Bitmap>? Composer { get; set; }

        /// <summary>
        /// Whether <see cref="Composite"/> is what <paramref name="build"/> makes at
        /// <paramref name="size"/>.
        /// </summary>
        public bool Holds(Size size, Func<Image, Size, Bitmap> build) =>
            Composite is { } composite &&
            composite.Size == size &&
            Composer == build;

        /// <summary>
        /// The size a compose on the pool is making, or null when none is.
        /// </summary>
        public Size? Composing { get; set; }

        /// <summary>
        /// A compose on the pool failed, so none is started again: the pane would ask on every
        /// step of its spinner, which would turn for good. Drawn as nothing, as a picture that
        /// cannot be decoded is.
        /// </summary>
        public bool Uncomposable { get; set; }

        /// <summary>
        /// Composes reading <see cref="Image"/> on the pool, and whether the cache has let go of
        /// it. The picture is disposed once both say so, so leaving an entry whose compose is still
        /// running neither waits for it nor pulls the picture out from under it.
        /// </summary>
        int readers;

        bool released;

        readonly Lock gate = new();

        public bool TryRead()
        {
            lock (gate)
            {
                if (released)
                {
                    return false;
                }

                readers++;
                return true;
            }
        }

        public void EndRead()
        {
            lock (gate)
            {
                readers--;
                if (released &&
                    readers == 0)
                {
                    Image?.Dispose();
                }
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                released = true;
                if (readers == 0)
                {
                    Image?.Dispose();
                }
            }

            Composite?.Dispose();
            Composite = null;
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
        Run(() => Load(path), image => Loaded(path, stamp, image, loaded));
        return null;
    }

    /// <summary>
    /// The decoded picture at <paramref name="path"/> for the UI thread to draw from here and now,
    /// or null when it is not decoded, or a compose on the pool is reading it: a GDI+ image used
    /// from two threads at once throws. A compose only ever starts on the UI thread, so one cannot
    /// begin between this answering and the caller drawing.
    /// <para>
    /// For a picture enlarged past half its own size, which is drawn a part at a time straight
    /// from the decoded picture. A composite of the whole of it at sixteen times the size that fits
    /// would be hundreds of megabytes to show the corner of it that is on screen.
    /// </para>
    /// </summary>
    public Image? Idle(string path) =>
        entries.TryGetValue(path, out var entry) &&
        entry.Composing is null
            ? entry.Image
            : null;

    /// <summary>
    /// The picture at <paramref name="path"/> as it was last composed, fitted or enlarged, at
    /// whatever size that was, or null when it never has been. Something to draw from while
    /// <see cref="Idle"/> has nothing to give.
    /// </summary>
    public Bitmap? Composited(string path) =>
        entries.TryGetValue(path, out var entry) ? entry.Composite : null;

    /// <summary>
    /// Whether the picture at <paramref name="path"/> is on its way: being decoded, or decoded and
    /// being composed with nothing older to show meanwhile. What a pane shows a spinner for, as
    /// against a picture that is not coming at all because this machine cannot decode it.
    /// </summary>
    public bool Loading(string path) =>
        pending.ContainsKey(path) ||
        entries.TryGetValue(path, out var entry) &&
        entry is { Composing: not null, Composite: null };

    void Loaded(string path, Stamp stamp, Image? image, Action loaded)
    {
        // Only the decode the path is still waiting on. An older one, for a stamp the file has
        // since moved past, would put back the picture the newer decode is replacing
        if (disposed ||
            !pending.TryGetValue(path, out var started) ||
            started != stamp)
        {
            image?.Dispose();
            return;
        }

        // The decode the path was waiting on has landed, so it is waiting no longer, whatever is
        // done with the result. Thrown away for a picture that had left the screen, it used to
        // leave the path marked as on its way: a reader who came back to it had nothing started
        // for it again, and a spinner in each pane until the file changed.
        pending.Remove(path);
        if (wanted is not null &&
            !wanted.Contains(path))
        {
            image?.Dispose();
            return;
        }

        Forget(path);
        entries.Add(path, new(stamp, image));
        loaded();
    }

    /// <summary>
    /// The picture at <paramref name="path"/> as it is painted at <paramref name="size"/>, built
    /// by <paramref name="build"/> from the decoded picture the first time that size is asked of
    /// it and kept until another size is, or another way of building it, or the picture goes. Null
    /// when the picture is not decoded.
    /// Built here and now: for a caller that has to have it this frame, such as a capture.
    /// </summary>
    public Bitmap? Composite(string path, Size size, Func<Image, Size, Bitmap> build)
    {
        if (!entries.TryGetValue(path, out var entry) ||
            entry.Image is null)
        {
            return null;
        }

        if (entry.Holds(size, build))
        {
            return entry.Composite;
        }

        entry.Composite?.Dispose();
        entry.Composite = build(entry.Image, size);
        entry.Composer = build;
        Composed++;
        return entry.Composite;
    }

    /// <summary>
    /// The picture at <paramref name="path"/> as it is painted at <paramref name="size"/>, composed
    /// on the pool when it has not been at that size and by <paramref name="build"/>, after which
    /// <paramref name="loaded"/> is called on the UI thread. Meanwhile the composite at whatever
    /// size it was last made, for the caller to stretch into place, or null when there has never
    /// been one. With nowhere to post the result this composes here and now.
    /// <para>
    /// One compose at a time per picture. A resize asks for a new size every frame, and the one
    /// asked for when the compose in hand finishes is the one composed next, so a drag ends with
    /// the size it ended on rather than a queue of every size it passed through.
    /// </para>
    /// </summary>
    public Bitmap? Composite(string path, Size size, Func<Image, Size, Bitmap> build, Action loaded)
    {
        if (post is null)
        {
            return Composite(path, size, build);
        }

        if (!entries.TryGetValue(path, out var entry) ||
            entry.Image is null)
        {
            return null;
        }

        if (entry.Holds(size, build))
        {
            return entry.Composite;
        }

        if (entry.Composing is null &&
            !entry.Uncomposable &&
            entry.TryRead())
        {
            entry.Composing = size;
            var image = entry.Image;
            Run(
                () =>
                {
                    try
                    {
                        return build(image, size);
                    }
                    finally
                    {
                        entry.EndRead();
                    }
                },
                built => Landed(path, entry, built, build, loaded));
        }

        return entry.Composite;
    }

    void Landed(string path, Entry entry, Bitmap? built, Func<Image, Size, Bitmap> build, Action loaded)
    {
        entry.Composing = null;
        // Only into the entry it was made from. One the cache has since let go of, for a picture
        // left or rewritten, would put back what replaced it
        if (disposed ||
            !entries.TryGetValue(path, out var current) ||
            !ReferenceEquals(current, entry))
        {
            built?.Dispose();
            return;
        }

        if (built is null)
        {
            entry.Uncomposable = true;
            // Repainted, so the spinner it was showing goes
            loaded();
            return;
        }

        entry.Composite?.Dispose();
        entry.Composite = built;
        entry.Composer = build;
        Composed++;
        loaded();
    }

    /// <summary>
    /// <paramref name="job"/> on the pool, its result handed to <paramref name="done"/> on the UI
    /// thread. A job that throws hands back null, as a decode that fails does.
    /// </summary>
    void Run<T>(Func<T?> job, Action<T?> done)
        where T : class, IDisposable =>
        Task.Run(
                () =>
                {
                    try
                    {
                        return job();
                    }
                    catch
                    {
                        return null;
                    }
                })
            .ContinueWith(
                task =>
                {
                    var result = task.Result;
                    try
                    {
                        post!(() => done(result));
                    }
                    catch (InvalidOperationException)
                    {
                        // The window went away before the job finished, and with it the thread
                        // this would have been handed to
                        result?.Dispose();
                    }
                },
                Cancel.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

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
