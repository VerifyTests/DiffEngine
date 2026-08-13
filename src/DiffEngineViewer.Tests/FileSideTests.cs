/// <summary>
/// The read seam: the one place that decides whether a path is text or a picture, and the only
/// part of image support that touches a disk.
/// <para>
/// It runs on the queue poller, five times a second, over files a test run is rewriting underneath
/// it, so the rule it must not break is that nothing here throws.
/// </para>
/// </summary>
public class FileSideTests
{
    [Test]
    public async Task ReadsAnImage()
    {
        var file = Write("sample.png", Png());
        var side = FileSide.Read(file);

        await Assert.That(side.Text).IsEmpty();
        await Assert.That(side.Warning).IsNull();
        await Assert.That(side.Image!.Value.Header).IsEqualTo(new ImageHeader(ImageFormat.Png, 800, 600));
        await Assert.That(side.Image!.Value.Length).IsEqualTo(24);
        await Assert.That(side.Image!.Value.Hash).IsNotNull();
    }

    /// <summary>
    /// The extension decides, so bytes that are not a picture still read as an image side. The
    /// alternative is a pane full of the mojibake that binary decoded as text produces.
    /// </summary>
    [Test]
    public async Task AnImageNameOverBytesThatAreNot()
    {
        var file = Write("notreally.png", "the quick brown fox"u8.ToArray());
        var side = FileSide.Read(file);

        await Assert.That(side.Image).IsNotNull();
        await Assert.That(side.Image!.Value.Header).IsNull();
        await Assert.That(side.Text).IsEmpty();
    }

    [Test]
    public async Task TextIsStillText()
    {
        var file = Write("sample.txt", "the quick brown fox"u8.ToArray());
        var side = FileSide.Read(file);

        await Assert.That(side.Image).IsNull();
        await Assert.That(side.Text).IsEqualTo("the quick brown fox");
    }

    /// <summary>
    /// Normal for the expected side of a brand new snapshot. Absent is not unreadable: there is no
    /// image and no warning, and the pane renders as the empty side it is.
    /// </summary>
    [Test]
    public async Task MissingImage()
    {
        var side = FileSide.Read(Path.Combine(Directory(), "gone.png"));

        await Assert.That(side.Image).IsNull();
        await Assert.That(side.Warning).IsNull();
        await Assert.That(side.Stamp).IsNull();
    }

    /// <summary>
    /// The rule the poller depends on: a read that cannot succeed comes back as a warning rather
    /// than as an exception that closes the window.
    /// <para>
    /// Provoked with an unrepresentable path rather than a locked file, because an exclusive lock
    /// only refuses a reader on Windows and this has to fail the same way on all three platforms.
    /// </para>
    /// </summary>
    [Test]
    public async Task UnreadableImageDegrades()
    {
        var side = FileSide.Read(Path.Combine(Directory(), "no\0such.png"));

        await Assert.That(side.Warning).IsNotNull();
        // Still an image side, so the pane names the picture it could not read.
        await Assert.That(side.Image).IsNotNull();
        await Assert.That(side.Image!.Value.Hash).IsNull();
    }

    static string Write(string name, byte[] content)
    {
        var path = Path.Combine(Directory(), name);
        File.WriteAllBytes(path, content);
        return path;
    }

    static string Directory()
    {
        var path = Path.Combine(Path.GetTempPath(), "deview-image-sides");
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    static byte[] Png()
    {
        var bytes = new byte[24];
        ReadOnlySpan<byte> signature = [0x89, (byte) 'P', (byte) 'N', (byte) 'G', 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        bytes[8] = 0;
        bytes[9] = 0;
        bytes[10] = 0;
        bytes[11] = 13;
        bytes[18] = 800 >> 8;
        bytes[19] = 800 & 0xFF;
        bytes[22] = 600 >> 8;
        bytes[23] = 600 & 0xFF;
        return bytes;
    }
}
