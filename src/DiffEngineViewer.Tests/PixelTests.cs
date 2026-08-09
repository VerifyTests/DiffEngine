/// <summary>
/// Renders real frames through the native shim and verifies the pixels.
/// <para>
/// The shim owns one process wide window, so these run serially and share a single hidden window
/// rather than opening one per test.
/// </para>
/// <para>
/// The verified images are the ones produced by the Linux CI job under Xvfb with Mesa llvmpipe.
/// Determinism comes from pinning the rasteriser rather than the platform: llvmpipe is pure
/// software and therefore more reproducible than any GPU driver, and ImGui rasterises glyphs with
/// its own stb_truetype so text is identical everywhere. Opting in on another machine will render
/// correctly but may not match those baselines pixel for pixel.
/// </para>
/// </summary>
[NotInParallel]
public class PixelTests
{
    const int width = 1100;
    const int height = 700;

    /// <summary>
    /// Matches NativeViewerWindow's cell metrics, so the captured grid is the one ScreenBuilder
    /// sized.
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

    [Test]
    [PixelTest]
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
