using System.Collections.Concurrent;

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
        await Assert.That(resized!.Size).IsEqualTo(new Size(6, 4));
        await Assert.That(cache.Composed).IsEqualTo(2);
    }

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
        File.WriteAllBytes(path, SamplePng.Build(8, 6, 200, 40, 40));
        var stamp = File.GetLastWriteTimeUtc(path);
        using var cache = new ImageCache();
        var before = ((Bitmap) cache.Get(path, FileSide.Read(path).Image!.Value.Hash)!).GetPixel(0, 0);
        var hashBefore = FileSide.Read(path).Image!.Value.Hash;

        File.WriteAllBytes(path, SamplePng.Build(8, 6, 40, 200, 40));
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
