/// <summary>
/// How an image comparison reads.
/// <para>
/// Snapshotted through <see cref="AsciiRenderer"/> like every other screen, because the rows are
/// what all three heads draw: one line per property, coloured against the other side. A head with a
/// decoder also paints the picture underneath, but nothing about the comparison is only expressible
/// that way, which is the property these hold.
/// </para>
/// <para>
/// Whether the two are the same picture is the one thing the rows cannot say — it belongs to the
/// pair, not to either side — so it lives in the status line, and half of these exist to pin it.
/// </para>
/// </summary>
public class ImageScreenTests
{
    [Test]
    public Task Identical() =>
        Verify(Render(Received(), Expected()));

    /// <summary>
    /// The ordinary failing snapshot: same format, different everything else.
    /// </summary>
    [Test]
    public Task Differs() =>
        Verify(Render(
            Received(),
            Expected(width: 640, height: 480, length: 9_120, hash: "0B")));

    /// <summary>
    /// Re-encoded rather than redrawn: the same pixel size in a different format. The dimensions
    /// row matches while the rest does not, which is the case a single verdict per side would lose.
    /// </summary>
    [Test]
    public Task FormatChanged() =>
        Verify(Render(
            Received(),
            Expected(format: ImageFormat.Jpeg, length: 4_002, hash: "0B")));

    /// <summary>
    /// A brand new image snapshot: nothing committed to compare against yet.
    /// </summary>
    [Test]
    public Task NewImage() =>
        Verify(Render(Received(), FileSide.OfText("")));

    /// <summary>
    /// Named as a picture, holding something that is not one. Still an image side — the extension
    /// is what decides that — so it says what it found rather than rendering the bytes as text.
    /// </summary>
    [Test]
    public Task NotRecognized() =>
        Verify(Render(
            new("", null, null, new ImageFile("temp/sample.received.png", 27, null, "0C")),
            Expected()));

    /// <summary>
    /// Locked or otherwise unreadable. No hash means the two were never compared, which must not
    /// be reported as them differing.
    /// </summary>
    [Test]
    public Task Unreadable() =>
        Verify(Render(
            new("", null, null, ImageFile.Unread("temp/sample.received.png")),
            Expected()));

    [Test]
    public Task MoveInQueue() =>
        Verify(Fixtures.Render(Fixtures.Attached(
            Fixtures.Pending(),
            QueueEntry.ForMove(
                "move:temp/sample.received.png",
                "Sample.Test (png)",
                null,
                "temp/sample.received.png",
                "code/sample.verified.png",
                Received(),
                Expected(width: 640, height: 480, length: 9_120, hash: "0B")))));

    /// <summary>
    /// The file being deleted sits on the right, so a picture being deleted is the right side's,
    /// and the left is the nothing that is left afterwards.
    /// </summary>
    [Test]
    public Task DeleteInQueue() =>
        Verify(Fixtures.Render(Fixtures.Attached(
            Fixtures.Pending(),
            QueueEntry.ForDelete(
                "delete:code/extra.verified.png",
                "extra.verified.png",
                null,
                "code/extra.verified.png",
                Expected()))));

    /// <summary>
    /// The enrichment a head with a decoder draws under the rows. Offered only once the bytes were
    /// read and recognized, so a renderer never has to decide whether a path is worth trying.
    /// </summary>
    [Test]
    public async Task PanesCarryThePicture()
    {
        var screen = ScreenBuilder.Build(State(Received(), Expected()));
        await Assert.That(screen.Left.Image).IsEqualTo(new("temp/sample.received.png", 800, 600));
        await Assert.That(screen.Right.Image).IsEqualTo(new("code/sample.verified.png", 800, 600));
    }

    [Test]
    public async Task NoPictureWithoutARecognizedHeader()
    {
        var unread = new FileSide("", null, null, ImageFile.Unread("temp/sample.received.png"));
        var screen = ScreenBuilder.Build(State(unread, FileSide.OfText("")));
        await Assert.That(screen.Left.Image).IsNull();
        await Assert.That(screen.Right.Image).IsNull();
    }

    /// <summary>
    /// A text comparison must be untouched by any of this.
    /// </summary>
    [Test]
    public async Task TextPanesCarryNoPicture()
    {
        var screen = ScreenBuilder.Build(Fixtures.File());
        await Assert.That(screen.Left.Image).IsNull();
        await Assert.That(screen.Right.Image).IsNull();
    }

    static FileSide Received(
        ImageFormat format = ImageFormat.Png,
        int width = 800,
        int height = 600,
        long length = 12_384,
        string hash = "0A") =>
        Image("temp/sample.received.png", format, width, height, length, hash);

    static FileSide Expected(
        ImageFormat format = ImageFormat.Png,
        int width = 800,
        int height = 600,
        long length = 12_384,
        string hash = "0A") =>
        Image("code/sample.verified.png", format, width, height, length, hash);

    // Forward slashes, and never a real file: an image side is a handful of numbers and a hash, so
    // the screens are reachable without writing pictures to a disk that renders paths differently
    // on each platform.
    static FileSide Image(string path, ImageFormat format, int width, int height, long length, string hash) =>
        new("", null, null, new ImageFile(path, length, new ImageHeader(format, width, height), hash));

    static string Render(FileSide left, FileSide right) =>
        Fixtures.Render(State(left, right));

    static SessionState State(FileSide left, FileSide right) =>
        ViewerSession.EnqueueFile(
            SessionState.Start(ViewerMode.File, Fixtures.Columns, Fixtures.Rows),
            QueueEntry.ForFiles("temp/sample.received.png", "code/sample.verified.png", left, right));
}
