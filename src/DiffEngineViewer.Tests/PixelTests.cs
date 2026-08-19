/// <summary>
/// Renders real frames through the native shim and verifies the pixels.
/// <para>
/// The shim owns one process wide window, so these run serially and share a single hidden window
/// rather than opening one per test.
/// </para>
/// <para>
/// Two sets of baselines, because the two platforms no longer share a renderer: Linux is raylib
/// and ImGui, macOS is AppKit and Core Text.
/// </para>
/// <para>
/// The Linux images come from the CI job under Xvfb with Mesa llvmpipe. Determinism there comes
/// from pinning the rasteriser rather than the platform: llvmpipe is pure software and therefore
/// more reproducible than any GPU driver, and ImGui rasterises glyphs with its own stb_truetype so
/// text is identical everywhere.
/// </para>
/// <para>
/// The macOS images come from the pinned macos-14 runner, and are the weaker guarantee of the two.
/// Core Text is the system text stack, so the capture pins everything it can reach — scale, colour
/// space, and the six font smoothing and subpixel switches — but Apple can still change glyph
/// rasterisation within a runner image. That shows up as one legible diff to re-accept, not as
/// flakiness.
/// </para>
/// <para>
/// Opting in on a developer machine will render correctly but may not match either set pixel for
/// pixel.
/// </para>
/// <para>
/// Every capture is one frame drawn in the shared window's one ImGui context, so state that
/// context carries between frames makes a capture depend on what rendered before it — the picture
/// placement in <see cref="Images" /> moved by half a pixel with the test order, which itself
/// moved with the test framework's own ordering. The order is pinned so each capture inherits the
/// same state on every run, which is what keeps the baselines reproducible.
/// </para>
/// </summary>
public class PixelTests
{
    const int width = 1100;
    const int height = 700;

    /// <summary>
    /// The grid JetBrains Mono at 15px gives at this window size. Fixed here rather than taken
    /// from the shim's own measurement, so the baselines stay pinned to one layout: these are the
    /// numbers they were captured at.
    /// </summary>
    const int columns = width / 9;

    const int rows = height / 18;

    static IViewerWindow? window;

    [Before(Class)]
    public static void Open()
    {
        if (Environment.GetEnvironmentVariable(PixelTestAttribute.Variable) != "true")
        {
            return;
        }

        window = NativeViewerWindow.Open("DiffEngineViewer", width, height, true, out var error);
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
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 1)]
    public Task FileDiff() =>
        Capture(Fixtures.File());

    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 2)]
    public Task InlineSingle() =>
        Capture(Fixtures.Inline(Fixtures.Patch()));

    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 3)]
    public Task InlineQueue() =>
        Capture(
            Fixtures.Inline(
                Fixtures.Patch(),
                Fixtures.Patch("SampleTests.cs", 88, "\"one\"", "two"),
                Fixtures.Patch("OtherTests.cs", 12, null, "brand new")));

    /// <summary>
    /// A file name wider than the queue column, which has to stop at the divider rather than paint
    /// over the pane beside it. The WinForms head has the same case, so all three are described.
    /// </summary>
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 4)]
    public Task LongQueueLabel() =>
        Capture(
            Fixtures.Inline(
                Fixtures.Patch("HeaderPropagationExtensionsTests.cs", 130),
                Fixtures.Patch()));

    /// <summary>
    /// The new queue primitives in one frame: solution headers, a test sub-group, the conflict
    /// marker and the variant button. Mirrored in WindowsPixelTests, as ever.
    /// </summary>
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 5)]
    public Task GroupedConflictedQueue() =>
        Capture(Fixtures.GroupedConflicted());

    /// <summary>
    /// An image comparison: the pictures drawn under the rows every head draws. Mirrored in
    /// WindowsPixelTests over the same fixture, which is what holds the three heads to one
    /// placement rule — one blank line under the pane's own rows, fitted, never enlarged.
    /// <para>
    /// PNG, which every head decodes. Where they differ is which other formats they can read at
    /// all, and that difference is not visible in a picture: a format a head has no decoder for
    /// draws nothing and the rows carry the comparison, which the ASCII screens already describe.
    /// </para>
    /// </summary>
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 6)]
    public Task Images() =>
        Capture(Fixtures.Images());

    /// <summary>
    /// The context menu floated over the grouped queue, opened on the conflicted entry.
    /// <para>
    /// Linux only. That head draws the menu itself, so a capture has it; the macOS head pops a
    /// real <c>NSMenu</c>, which buys the keyboard, Escape and VoiceOver and costs this baseline —
    /// a capture makes no window, and a menu cannot be shown without one. The WinForms head made
    /// the same trade, and covers its <c>ContextMenuStrip</c> in ContextMenuTests instead. There
    /// is no equivalent here: the menu is behind the C ABI, so nothing managed can reach it.
    /// </para>
    /// </summary>
    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 7)]
    [SkipOnMac("The macOS head pops a real NSMenu, which a capture has no window to show.")]
    public Task ContextMenu() =>
        Capture(ViewerSession.OpenMenu(Fixtures.GroupedConflicted(), 5));

    [Test]
    [PixelTest]
    [NotInParallel(nameof(PixelTests), Order = 8)]
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
            // Linux and macOS run the same tests against different renderers, so the baselines
            // have to be told apart.
            await VerifyFile(path)
                .UniqueForOSPlatform();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
