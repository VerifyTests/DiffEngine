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
        var screen = ScreenBuilder.Build(state);
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
