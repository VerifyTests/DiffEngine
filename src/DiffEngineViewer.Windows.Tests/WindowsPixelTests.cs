/// <summary>
/// Renders real frames through the WinForms head and verifies the pixels, over the same fixtures
/// the ASCII snapshots use, so the two describe the same screens.
/// <para>
/// Goes through <see cref="IViewerWindow.Capture" /> rather than rendering a control directly,
/// because that is the method the other heads' baselines come from and it is worth exercising.
/// </para>
/// <para>
/// One window for the class, reused, and serial. Creating and destroying a top level window per
/// test is slow and leaves activation racing between them.
/// </para>
/// </summary>
[NotInParallel]
public class WindowsPixelTests
{
    const int width = 1100;
    const int height = 700;

    /// <summary>
    /// The grid this head reports at that window size. Fixed here rather than read back from the
    /// canvas, for the same reason the native suite fixes its own: the baselines stay pinned to one
    /// layout instead of moving with a measurement.
    /// <para>
    /// Same columns as the native suite, one row fewer. The shim assumes a flat 18px line, while
    /// this head measures its own cell and reserves padding above the body, so the same window
    /// holds one less. Feeding it 38 would not widen the window, it would clip the bottom row: the
    /// canvas draws <c>Math.Min(capacity, rows)</c>.
    /// </para>
    /// </summary>
    const int columns = 122;

    const int rows = 37;

    static IViewerWindow? window;

    [Before(Class)]
    public static void Open()
    {
        window = FormsViewerWindow.Open("DiffEngineViewer", width, height, hidden: true, out var error);
        if (window is null)
        {
            throw new(error!);
        }
    }

    [After(Class)]
    public static void Close()
    {
        window?.Dispose();
        window = null;
    }

    [Test]
    public Task FileDiff() =>
        Capture(Fixtures.File());

    [Test]
    public Task InlineSingle() =>
        Capture(Fixtures.Inline(Fixtures.Patch()));

    [Test]
    public Task InlineQueue() =>
        Capture(
            Fixtures.Inline(
                Fixtures.Patch(),
                Fixtures.Patch("SampleTests.cs", 88, "\"one\"", "two"),
                Fixtures.Patch("OtherTests.cs", 12, null, "brand new")));

    /// <summary>
    /// A file name wider than the queue column. It has to stop at the rule rather than paint over
    /// the pane beside it, which is what GenericTypographic's NoClip used to let it do.
    /// </summary>
    [Test]
    public Task LongQueueLabel() =>
        Capture(
            Fixtures.Inline(
                Fixtures.Patch("HeaderPropagationExtensionsTests.cs", 130),
                Fixtures.Patch()));

    /// <summary>
    /// The new queue primitives in one frame: solution headers, a test sub-group, the conflict
    /// marker and the variant button. Mirrored in the native suite, as ever.
    /// </summary>
    [Test]
    public Task GroupedConflictedQueue() =>
        Capture(Fixtures.GroupedConflicted());

    [Test]
    public Task InlineAccepted()
    {
        var state = Fixtures.Inline(
            Fixtures.Patch(),
            Fixtures.Patch("OtherTests.cs", 12, null, "brand new"));
        return Capture(ViewerSession.Apply(state, CommandKind.Accept, Fixtures.Applied));
    }

    static async Task Capture(SessionState state)
    {
        var screen = ScreenBuilder.Build(ViewerSession.Resize(state, columns, rows));
        var path = Path.Combine(Path.GetTempPath(), $"deview-{Guid.NewGuid():N}.png");
        try
        {
            await Assert.That(window!.Capture(screen, width, height, path)).IsTrue();
            await VerifyFile(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
