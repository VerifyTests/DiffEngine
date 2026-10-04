using System.Threading.Channels;

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
    /// Kept with its colours multiplied by their alpha, which is what a paint's buffer holds, so
    /// drawing from it converts nothing: the way a decoder hands it over, every paint of an
    /// enlarged picture multiplied every pixel it read. An opaque pixel is the one in the file,
    /// and a translucent one keeps its alpha.
    /// </summary>
    [Test]
    public async Task DecodesPremultiplied()
    {
        var path = Write("premultiplied.png", SamplePng.Build(8, 6, 200, 40, 40));
        using var cache = new ImageCache();
        using var file = new Bitmap(path);

        var picture = (Bitmap) cache.Get(path, null)!;

        await Assert.That(picture.PixelFormat).IsEqualTo(System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var translucent = 0;
        for (var x = 0; x < 8; x++)
        {
            var expected = file.GetPixel(x, 0);
            var kept = picture.GetPixel(x, 0);
            await Assert.That(kept.A).IsEqualTo(expected.A);
            if (expected.A == 255)
            {
                await Assert.That(kept).IsEqualTo(expected);
            }
            else
            {
                translucent++;
            }
        }

        // Or the sample has stopped fading out, and this compares only what cannot differ
        await Assert.That(translucent).IsGreaterThan(0);
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
        var posted = new Posts();
        using var cache = new ImageCache(posted.Add);
        var loaded = 0;

        await Assert.That(cache.Get(path, null, () => loaded++)).IsNull();
        var handBack = await posted.Take();
        handBack();

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
        var posted = new Posts();
        using var cache = new ImageCache(posted.Add);
        var loaded = 0;
        cache.Keep([path]);

        cache.Get(path, null, () => loaded++);
        var handBack = await posted.Take();
        cache.Keep([]);
        handBack();

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
        var posted = new Posts();
        using var cache = new ImageCache(posted.Add);
        var loaded = 0;
        cache.Keep([path]);
        cache.Get(path, null, () => loaded++);
        var dropped = await posted.Take();
        cache.Keep([]);
        dropped();
        await Assert.That(cache.Loading(path)).IsFalse();

        cache.Keep([path]);
        await Assert.That(cache.Get(path, null, () => loaded++)).IsNull();
        await Assert.That(cache.Loading(path)).IsTrue();
        var handBack = await posted.Take();
        handBack();

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
        var posted = new Posts();
        using var cache = new ImageCache(posted.Add);
        var loaded = 0;
        cache.Keep([path]);
        cache.Get(path, null, () => loaded++);
        var handBack = await posted.Take();
        cache.Keep([]);
        cache.Keep([path]);

        // Nothing new is started for it
        await Assert.That(cache.Get(path, null, () => loaded++)).IsNull();
        handBack();

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
    /// A picture is composed two ways: fitted over its checkerboard, and enlarged with nothing
    /// under it. Either of those at the size the other is asked for is still not the other, so it
    /// is composed again rather than handed over because the sizes happen to match.
    /// </summary>
    [Test]
    public async Task APictureComposedOneWayIsNotTheOtherAtThatSize()
    {
        var path = Write("two-ways.png", SamplePng.Build(8, 6, 200, 40, 40));
        using var cache = new ImageCache();
        await Assert.That(cache.Get(path, null)).IsNotNull();
        Func<Image, Size, Bitmap> fitted = (_, size) => new(size.Width, size.Height);
        Func<Image, Size, Bitmap> enlarged = (_, size) => new(size.Width, size.Height);

        var first = cache.Composite(path, new(4, 3), fitted);
        var other = cache.Composite(path, new(4, 3), enlarged);
        var again = cache.Composite(path, new(4, 3), enlarged);

        await Assert.That(ReferenceEquals(first, other)).IsFalse();
        await Assert.That(ReferenceEquals(other, again)).IsTrue();
        await Assert.That(cache.Composed).IsEqualTo(2);
    }

    /// <summary>
    /// And on the pool as for a new size: what is there stands in until the other way lands.
    /// </summary>
    [Test]
    public async Task APictureComposedOneWayStandsInUntilTheOtherLands()
    {
        var path = Write("two-ways-posted.png", SamplePng.Build(8, 6, 200, 40, 40));
        var posted = new Posts();
        using var cache = new ImageCache(posted.Add);
        cache.Get(path, null);
        Func<Image, Size, Bitmap> enlarged = (_, size) => new(size.Width, size.Height);
        cache.Composite(path, new(4, 3), Build, () => { });
        var first = await posted.Take();
        first();
        var fitted = cache.Composite(path, new(4, 3), Build, () => { });

        var meanwhile = cache.Composite(path, new(4, 3), enlarged, () => { });
        await Assert.That(ReferenceEquals(meanwhile, fitted)).IsTrue();

        var second = await posted.Take();
        second();
        var landed = cache.Composite(path, new(4, 3), enlarged, () => { });
        await Assert.That(ReferenceEquals(landed, fitted)).IsFalse();
        await Assert.That(posted.Count).IsEqualTo(0);
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
        var posted = new Posts();
        using var cache = new ImageCache(posted.Add);
        await Assert.That(cache.Get(path, null)).IsNotNull();
        var loaded = 0;

        await Assert.That(cache.Composite(path, new(4, 3), Build, () => loaded++)).IsNull();
        await Assert.That(cache.Loading(path)).IsTrue();
        var handBack = await posted.Take();
        handBack();

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
        var posted = new Posts();
        using var cache = new ImageCache(posted.Add);
        cache.Get(path, null);
        cache.Composite(path, new(4, 3), Build, () => { });
        var first = await posted.Take();
        first();
        var small = cache.Composite(path, new(4, 3), Build, () => { });

        var meanwhile = cache.Composite(path, new(6, 4), Build, () => { });
        await Assert.That(ReferenceEquals(meanwhile, small)).IsTrue();
        await Assert.That(cache.Loading(path)).IsFalse();

        var second = await posted.Take();
        second();
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
        var posted = new Posts();
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
        var handBack = await posted.Take();
        handBack();

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
        var posted = new Posts();
        using var cache = new ImageCache(posted.Add);
        cache.Get(path, null);
        var attempts = 0;
        Func<Image, Size, Bitmap> failing = (_, _) =>
        {
            attempts++;
            throw new InvalidOperationException("Out of memory.");
        };

        cache.Composite(path, new(4, 3), failing, () => { });
        var handBack = await posted.Take();
        handBack();

        await Assert.That(cache.Composite(path, new(4, 3), failing, () => { })).IsNull();
        await Assert.That(cache.Loading(path)).IsFalse();
        await Assert.That(posted.Count).IsEqualTo(0);
        await Assert.That(attempts).IsEqualTo(1);
    }

    /// <summary>
    /// What failed was that size made that way, and nothing else is held against the picture. An
    /// enlarged copy there was no memory for used to stop the fitted one being made again too, so
    /// after the next resize the pane had the old size stretched into place for good.
    /// </summary>
    [Test]
    public async Task AComposeThatFailsLeavesTheOtherSizesToBeMade()
    {
        var path = Write("uncomposable-at-one-size.png", SamplePng.Build(8, 6, 200, 40, 40));
        var posted = new Posts();
        using var cache = new ImageCache(posted.Add);
        cache.Get(path, null);
        var attempts = 0;
        Func<Image, Size, Bitmap> enlarged = (_, _) =>
        {
            attempts++;
            throw new InvalidOperationException("Out of memory.");
        };
        cache.Composite(path, new(4, 3), Build, () => { });
        (await posted.Take())();

        cache.Composite(path, new(400, 300), enlarged, () => { });
        (await posted.Take())();

        // The fitted copy at a new size, which is what a resize asks for
        cache.Composite(path, new(6, 4), Build, () => { });
        (await posted.Take())();
        await Assert.That(cache.Composite(path, new(6, 4), Build, () => { })!.Size).IsEqualTo(new(6, 4));

        // And the one that failed is still not tried again, while another size of it is
        cache.Composite(path, new(400, 300), enlarged, () => { });
        await Assert.That(posted.Count).IsEqualTo(0);
        await Assert.That(attempts).IsEqualTo(1);
        cache.Composite(path, new(200, 150), enlarged, () => { });
        (await posted.Take())();
        await Assert.That(attempts).IsEqualTo(2);
    }

    static Bitmap Build(Image picture, Size size) =>
        new(size.Width, size.Height);

    /// <summary>
    /// What a cache posts back, for a test to take and run: the window's BeginInvoke, as a queue.
    /// <para>
    /// Waited for without holding a thread. A decode runs on the pool, and so does every test, in
    /// parallel. These used to block where they stood until the decode posted back, so each held
    /// a pool thread while waiting for work that needed one, and once every thread the pool had
    /// was held that way the work had nowhere to run until the pool grew another: on a runner of
    /// four cores busy with the other test projects, ten seconds went by with nothing decoded and
    /// four of these failed together. A test that awaits gives its thread back.
    /// </para>
    /// </summary>
    sealed class Posts
    {
        readonly Channel<Action> posted = Channel.CreateUnbounded<Action>();

        public void Add(Action action) =>
            posted.Writer.TryWrite(action);

        public int Count =>
            posted.Reader.Count;

        public async Task<Action> Take()
        {
            // Long, since all it bounds is a test that would otherwise never end
            using var timeout = new CancelSource(TimeSpan.FromMinutes(1));
            try
            {
                return await posted.Reader.ReadAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("Nothing was posted back within a minute.");
            }
        }
    }

    [Test]
    public async Task MissingFile()
    {
        using var cache = new ImageCache();
        await Assert.That(cache.Get(Path.Combine(directory, "gone.png"), null)).IsNull();
    }

    static string Write(string name, byte[] content)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    // A folder for this run alone. Each test writes a file of its own name, so the tests of one
    // run never meet, but under one fixed name two runs on a machine at once wrote, rewrote and
    // deleted each other's pictures.
    static string directory = "";

    [Before(Class)]
    public static void CreateDirectory() =>
        directory = Directory.CreateTempSubdirectory("deview-image-cache-").FullName;

    [After(Class)]
    public static void DeleteDirectory() =>
        Directory.Delete(directory, true);

    /// <summary>
    /// A picture rewritten with different pixels at the same length and
    /// the same write time, which is what a rewrite inside the file system's timestamp granularity
    /// looks like to a stat. The model's hash sees it.
    /// </summary>
    [Test]
    public async Task ARewriteWithTheSameStampKeepsTheOldPicture()
    {
        var path = Path.Combine(directory, "Same.received.png");
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
