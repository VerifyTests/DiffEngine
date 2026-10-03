/// <summary>
/// The decode the WinForms head puts under an image pane's rows.
/// <para>
/// <c>OnPaint</c> runs on every wheel notch and every resize, so the two properties worth holding
/// are that a picture is decoded once and that decoding it does not leave a handle on the file —
/// the received file is the one accepting is about to copy over.
/// </para>
/// </summary>
public class ImageCacheTests
{
    [Test]
    public async Task DecodesOnceAndKeepsIt()
    {
        var path = Write("decoded.png", SamplePng.Build(8, 6, 200, 40, 40));
        using var cache = new ImageCache();

        var first = cache.Get(path, null);
        await Assert.That(first).IsNotNull();
        await Assert.That(first!.Width).IsEqualTo(8);
        await Assert.That(first.Height).IsEqualTo(6);
        await Assert.That(ReferenceEquals(cache.Get(path, null), first)).IsTrue();
    }

    /// <summary>
    /// The hazard this cache is written around. GDI+ holds the stream it was handed for as long as
    /// the image lives, so decoding straight from the file would make the viewer the reason its own
    /// accept fails.
    /// </summary>
    [Test]
    public async Task LeavesNoHandleOnTheFile()
    {
        var path = Write("copied-over.png", SamplePng.Build(8, 6, 200, 40, 40));
        using var cache = new ImageCache();
        await Assert.That(cache.Get(path, null)).IsNotNull();

        var replacement = Write("replacement.png", SamplePng.Build(4, 4, 40, 200, 40));
        File.Copy(replacement, path, true);
    }

    /// <summary>
    /// A re-run rewrites the received file underneath an open window, and the pane has to follow it
    /// rather than keep showing what was there when it was first drawn.
    /// </summary>
    [Test]
    public async Task RedecodesWhenTheFileChanges()
    {
        var path = Write("rewritten.png", SamplePng.Build(8, 6, 200, 40, 40));
        using var cache = new ImageCache();
        await Assert.That(cache.Get(path, null)!.Width).IsEqualTo(8);

        // A different size, so the change is visible whatever the file system's timestamp
        // resolution turns out to be.
        await File.WriteAllBytesAsync(path, SamplePng.Build(4, 4, 40, 200, 40));
        await Assert.That(cache.Get(path, null)!.Width).IsEqualTo(4);
    }

    /// <summary>
    /// Something named as a picture that this machine cannot decode draws as nothing, and is
    /// attempted once rather than once per frame. The rows have already said what it is.
    /// </summary>
    [Test]
    public async Task RemembersAFailure()
    {
        var path = Write("notreally.png", "the quick brown fox"u8.ToArray());
        using var cache = new ImageCache();

        await Assert.That(cache.Get(path, null)).IsNull();
        await Assert.That(cache.Get(path, null)).IsNull();
    }

    /// <summary>
    /// The window's decode runs on the pool and comes back through the post the window gives the
    /// cache, which is BeginInvoke there and a queue here. Until it does, the pane has no picture
    /// and the window is free to paint its rows.
    /// </summary>
    [Test]
    public async Task ADecodeWithSomewhereToPostItIsHandedBack()
    {
        var path = Write("posted.png", SamplePng.Build(8, 6, 200, 40, 40));
        using var posted = new BlockingCollection<Action>();
        using var cache = new ImageCache(posted.Add);
        var loaded = 0;

        await Assert.That(cache.Get(path, null, () => loaded++)).IsNull();
        await Assert.That(posted.TryTake(out var handBack, TimeSpan.FromSeconds(10))).IsTrue();
        handBack!();

        await Assert.That(loaded).IsEqualTo(1);
        await Assert.That(cache.Get(path, null, () => loaded++)!.Width).IsEqualTo(8);
    }

    /// <summary>
    /// A decode that finishes after its picture left the screen is thrown away rather than cached,
    /// or navigating quickly through a queue of pictures would hold every one of them.
    /// </summary>
    [Test]
    public async Task ADecodeForAPictureNoLongerOnScreenIsDropped()
    {
        var path = Write("left-behind.png", SamplePng.Build(8, 6, 200, 40, 40));
        using var posted = new BlockingCollection<Action>();
        using var cache = new ImageCache(posted.Add);
        var loaded = 0;
        cache.Keep([path]);

        cache.Get(path, null, () => loaded++);
        await Assert.That(posted.TryTake(out var handBack, TimeSpan.FromSeconds(10))).IsTrue();
        cache.Keep([]);
        handBack!();

        await Assert.That(loaded).IsEqualTo(0);
        await Assert.That(cache.Composite(path, new(8, 6), (_, size) => new(size.Width, size.Height))).IsNull();
    }

    /// <summary>
    /// And a picture that comes back after its decode was dropped is decoded again. The dropped
    /// decode used to leave the path marked as on its way, so nothing was started for it and both
    /// panes showed a spinner until the file changed: stepping past a picture before it had
    /// decoded, which holding Tab through a queue of them does to nearly every one.
    /// </summary>
    [Test]
    public async Task APictureThatComesBackAfterItsDecodeWasDroppedIsDecodedAgain()
    {
        var path = Write("came-back.png", SamplePng.Build(8, 6, 200, 40, 40));
        using var posted = new BlockingCollection<Action>();
        using var cache = new ImageCache(posted.Add);
        var loaded = 0;
        cache.Keep([path]);
        cache.Get(path, null, () => loaded++);
        await Assert.That(posted.TryTake(out var dropped, TimeSpan.FromSeconds(10))).IsTrue();
        cache.Keep([]);
        dropped!();
        await Assert.That(cache.Loading(path)).IsFalse();

        cache.Keep([path]);
        await Assert.That(cache.Get(path, null, () => loaded++)).IsNull();
        await Assert.That(cache.Loading(path)).IsTrue();
        await Assert.That(posted.TryTake(out var handBack, TimeSpan.FromSeconds(10))).IsTrue();
        handBack!();

        await Assert.That(loaded).IsEqualTo(1);
        await Assert.That(cache.Loading(path)).IsFalse();
        await Assert.That(cache.Get(path, null, () => loaded++)!.Width).IsEqualTo(8);
    }

    /// <summary>
    /// Back on screen before its decode has landed, the picture is still the one on its way: that
    /// decode is kept when it lands, not thrown away and started over.
    /// </summary>
    [Test]
    public async Task APictureBackOnScreenBeforeItsDecodeLandsKeepsThatDecode()
    {
        var path = Write("back-in-time.png", SamplePng.Build(8, 6, 200, 40, 40));
        using var posted = new BlockingCollection<Action>();
        using var cache = new ImageCache(posted.Add);
        var loaded = 0;
        cache.Keep([path]);
        cache.Get(path, null, () => loaded++);
        await Assert.That(posted.TryTake(out var handBack, TimeSpan.FromSeconds(10))).IsTrue();
        cache.Keep([]);
        cache.Keep([path]);

        // Nothing new is started for it
        await Assert.That(cache.Get(path, null, () => loaded++)).IsNull();
        handBack!();

        await Assert.That(loaded).IsEqualTo(1);
        await Assert.That(posted.Count).IsEqualTo(0);
        await Assert.That(cache.Get(path, null, () => loaded++)!.Width).IsEqualTo(8);
    }

    /// <summary>
    /// A pane paints the picture over its checkerboard, scaled, once per size, and copies that on
    /// every paint after. Scaling it on every paint cost 46 to 66 ms a paint for a pair of 2000 by
    /// 1500 pictures, on every wheel notch.
    /// </summary>
    [Test]
    public async Task APictureIsComposedOncePerSize()
    {
        var path = Write("composed.png", SamplePng.Build(8, 6, 200, 40, 40));
        using var cache = new ImageCache();
        await Assert.That(cache.Get(path, null)).IsNotNull();
        Func<Image, Size, Bitmap> build = (_, size) => new(size.Width, size.Height);

        var first = cache.Composite(path, new(4, 3), build);
        var again = cache.Composite(path, new(4, 3), build);
        var resized = cache.Composite(path, new(6, 4), build);

        await Assert.That(ReferenceEquals(first, again)).IsTrue();
        await Assert.That(resized!.Size).IsEqualTo(new(6, 4));
        await Assert.That(cache.Composed).IsEqualTo(2);
    }

    /// <summary>
    /// The window composes on the pool as well: scaling a page of a document on the UI thread held
    /// it for tens of milliseconds a size. Until the first lands there is nothing to draw, and the
    /// pane shows that it is coming.
    /// </summary>
    [Test]
    public async Task AComposeWithSomewhereToPostItIsHandedBack()
    {
        var path = Write("composed-posted.png", SamplePng.Build(8, 6, 200, 40, 40));
        using var posted = new BlockingCollection<Action>();
        using var cache = new ImageCache(posted.Add);
        await Assert.That(cache.Get(path, null)).IsNotNull();
        var loaded = 0;

        await Assert.That(cache.Composite(path, new(4, 3), Build, () => loaded++)).IsNull();
        await Assert.That(cache.Loading(path)).IsTrue();
        await Assert.That(posted.TryTake(out var handBack, TimeSpan.FromSeconds(10))).IsTrue();
        handBack!();

        await Assert.That(loaded).IsEqualTo(1);
        await Assert.That(cache.Loading(path)).IsFalse();
        await Assert.That(cache.Composite(path, new(4, 3), Build, () => loaded++)!.Size).IsEqualTo(new(4, 3));
    }

    /// <summary>
    /// Mid resize the size last composed stands in, stretched into place, while the new one is
    /// made. A spinner on every step of a drag would be worse than a frame or two of a rough picture.
    /// </summary>
    [Test]
    public async Task AResizeShowsTheLastSizeUntilTheNewOneLands()
    {
        var path = Write("resized.png", SamplePng.Build(8, 6, 200, 40, 40));
        using var posted = new BlockingCollection<Action>();
        using var cache = new ImageCache(posted.Add);
        cache.Get(path, null);
        cache.Composite(path, new(4, 3), Build, () => { });
        await Assert.That(posted.TryTake(out var first, TimeSpan.FromSeconds(10))).IsTrue();
        first!();
        var small = cache.Composite(path, new(4, 3), Build, () => { });

        var meanwhile = cache.Composite(path, new(6, 4), Build, () => { });
        await Assert.That(ReferenceEquals(meanwhile, small)).IsTrue();
        await Assert.That(cache.Loading(path)).IsFalse();

        await Assert.That(posted.TryTake(out var second, TimeSpan.FromSeconds(10))).IsTrue();
        second!();
        await Assert.That(cache.Composite(path, new(6, 4), Build, () => { })!.Size).IsEqualTo(new(6, 4));
    }

    /// <summary>
    /// A compose that finishes after its picture left the screen is thrown away rather than put
    /// back. And the picture it was reading is let go of once it has finished reading, rather than
    /// disposed under it, which would have failed the compose in the middle of a GDI+ call.
    /// </summary>
    [Test]
    public async Task AComposeForAPictureNoLongerOnScreenIsDropped()
    {
        var path = Write("composed-left-behind.png", SamplePng.Build(8, 6, 200, 40, 40));
        using var posted = new BlockingCollection<Action>();
        using var cache = new ImageCache(posted.Add);
        using var reading = new ManualResetEventSlim();
        var width = 0;
        var loaded = 0;
        cache.Keep([path]);
        cache.Get(path, null);

        cache.Composite(
            path,
            new(4, 3),
            (picture, size) =>
            {
                reading.Wait();
                width = picture.Width;
                return new(size.Width, size.Height);
            },
            () => loaded++);
        cache.Keep([]);
        reading.Set();
        await Assert.That(posted.TryTake(out var handBack, TimeSpan.FromSeconds(10))).IsTrue();
        handBack!();

        await Assert.That(width).IsEqualTo(8);
        await Assert.That(loaded).IsEqualTo(0);
    }

    /// <summary>
    /// A compose that fails is not started again. The pane asks on every step of its spinner, so
    /// one retried there would turn for good. The picture draws as nothing instead, as one that
    /// cannot be decoded does.
    /// </summary>
    [Test]
    public async Task AComposeThatFailsIsNotTriedAgain()
    {
        var path = Write("uncomposable.png", SamplePng.Build(8, 6, 200, 40, 40));
        using var posted = new BlockingCollection<Action>();
        using var cache = new ImageCache(posted.Add);
        cache.Get(path, null);
        var attempts = 0;
        Func<Image, Size, Bitmap> failing = (_, _) =>
        {
            attempts++;
            throw new InvalidOperationException("Out of memory.");
        };

        cache.Composite(path, new(4, 3), failing, () => { });
        await Assert.That(posted.TryTake(out var handBack, TimeSpan.FromSeconds(10))).IsTrue();
        handBack!();

        await Assert.That(cache.Composite(path, new(4, 3), failing, () => { })).IsNull();
        await Assert.That(cache.Loading(path)).IsFalse();
        await Assert.That(posted.Count).IsEqualTo(0);
        await Assert.That(attempts).IsEqualTo(1);
    }

    static Bitmap Build(Image picture, Size size) =>
        new(size.Width, size.Height);

    [Test]
    public async Task MissingFile()
    {
        using var cache = new ImageCache();
        await Assert.That(cache.Get(Path.Combine(Directory(), "gone.png"), null)).IsNull();
    }

    static string Write(string name, byte[] content)
    {
        var path = Path.Combine(Directory(), name);
        File.WriteAllBytes(path, content);
        return path;
    }

    static string Directory()
    {
        var path = Path.Combine(Path.GetTempPath(), "deview-image-cache");
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// A picture rewritten with different pixels at the same length and
    /// the same write time, which is what a rewrite inside the file system's timestamp granularity
    /// looks like to a stat. The model's hash sees it.
    /// </summary>
    [Test]
    public async Task ARewriteWithTheSameStampKeepsTheOldPicture()
    {
        var path = Path.Combine(Directory(), "Same.received.png");
        await File.WriteAllBytesAsync(path, SamplePng.Build(8, 6, 200, 40, 40));
        var stamp = File.GetLastWriteTimeUtc(path);
        using var cache = new ImageCache();
        var before = ((Bitmap) cache.Get(path, FileSide.Read(path).Image!.Value.Hash)!).GetPixel(0, 0);
        var hashBefore = FileSide.Read(path).Image!.Value.Hash;

        await File.WriteAllBytesAsync(path, SamplePng.Build(8, 6, 40, 200, 40));
        File.SetLastWriteTimeUtc(path, stamp);
        var hashAfter = FileSide.Read(path).Image!.Value.Hash;
        var after = ((Bitmap) cache.Get(path, hashAfter)!).GetPixel(0, 0);

        // How often two writes in a row land on one stamp here, for how reachable that is.
        var ticks = new List<long>();
        for (var index = 0; index < 200; index++)
        {
            File.WriteAllBytes(path, [(byte) index]);
            ticks.Add(File.GetLastWriteTimeUtc(path).Ticks);
        }

        var repeats = ticks.Zip(ticks.Skip(1)).Count(_ => _.First == _.Second);
        var smallest = ticks.Zip(ticks.Skip(1)).Select(_ => _.Second - _.First).Where(_ => _ > 0).DefaultIfEmpty(0).Min();
        File.Delete(path);
        Console.WriteLine(
            $"hash changed {hashBefore != hashAfter}; pixel before {before}, after {after}; " +
            $"back to back writes on this volume: {repeats} of 199 kept the stamp, smallest step {smallest / 10}us");
        await Assert.That(after.ToArgb()).IsNotEqualTo(before.ToArgb());
    }
}
