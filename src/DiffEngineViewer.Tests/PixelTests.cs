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
/// </summary>
[NotInParallel]
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
    public Task FileDiff() =>
        Capture(Fixtures.File());

    [Test]
    [PixelTest]
    public Task InlineSingle() =>
        Capture(Fixtures.Inline(Fixtures.Patch()));

    [Test]
    [PixelTest]
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
    public Task Images() =>
        Capture(Fixtures.Images(), imageSsim);

    /// <summary>
    /// Looser than the threshold <see cref="ModuleInitializer"/> sets, for the one screen that
    /// paints a picture rather than text.
    /// <para>
    /// The determinism the other baselines rely on is glyph rasterisation: ImGui runs its own
    /// stb_truetype, so text comes out identical on any rasteriser. A picture does not go through
    /// that — it is sampled by the renderer — and the ubuntu-latest llvmpipe build scores 0.9979
    /// on this frame on some runner images and matches on others. That has failed on main and on
    /// branches alike, which is a threshold saying nothing about the change under test.
    /// </para>
    /// <para>
    /// Still tight enough for what it has to catch. These fixtures are small against the window,
    /// but a picture that fails to draw, draws the wrong file, or lands in the wrong place takes
    /// its whole region to near zero local similarity, which scores well below this.
    /// </para>
    /// </summary>
    const double imageSsim = 0.995;

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
    [SkipOnMac("The macOS head pops a real NSMenu, which a capture has no window to show.")]
    public Task ContextMenu() =>
        Capture(ViewerSession.OpenMenu(Fixtures.GroupedConflicted(), 5));

    [Test]
    [PixelTest]
    public Task InlineAccepted()
    {
        var state = Fixtures.Inline(
            Fixtures.Patch(),
            Fixtures.Patch("OtherTests.cs", 12, null, "brand new"));
        return Capture(ViewerSession.Apply(state, CommandKind.Accept, Fixtures.Applied));
    }

    /// <param name="ssim">
    /// Overrides the suite threshold for a screen that needs its own. Null leaves
    /// <see cref="ModuleInitializer"/>'s in place, so there is one default rather than a copy of it
    /// per call.
    /// </param>
    static async Task Capture(SessionState state, double? ssim = null)
    {
        var screen = ScreenBuilder.Build(ViewerSession.Resize(state, columns, rows));
        var path = Path.Combine(Path.GetTempPath(), $"deview-{Guid.NewGuid():N}.png");
        try
        {
            await Assert.That(window!.Capture(screen, width, height, path)).IsTrue();
            // Linux and macOS run the same tests against different renderers, so the baselines
            // have to be told apart.
            var verify = VerifyFile(path)
                .UniqueForOSPlatform();
            if (ssim is not null)
            {
                verify = verify.UseSsimForPng(ssim.Value);
            }

            await verify;
        }
        finally
        {
            File.Delete(path);
        }
    }
}
