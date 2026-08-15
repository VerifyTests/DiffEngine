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

        var first = cache.Get(path);
        await Assert.That(first).IsNotNull();
        await Assert.That(first!.Width).IsEqualTo(8);
        await Assert.That(first.Height).IsEqualTo(6);
        await Assert.That(ReferenceEquals(cache.Get(path), first)).IsTrue();
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
        await Assert.That(cache.Get(path)).IsNotNull();

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
        await Assert.That(cache.Get(path)!.Width).IsEqualTo(8);

        // A different size, so the change is visible whatever the file system's timestamp
        // resolution turns out to be.
        await File.WriteAllBytesAsync(path, SamplePng.Build(4, 4, 40, 200, 40));
        await Assert.That(cache.Get(path)!.Width).IsEqualTo(4);
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

        await Assert.That(cache.Get(path)).IsNull();
        await Assert.That(cache.Get(path)).IsNull();
    }

    [Test]
    public async Task MissingFile()
    {
        using var cache = new ImageCache();
        await Assert.That(cache.Get(Path.Combine(Directory(), "gone.png"))).IsNull();
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
}
