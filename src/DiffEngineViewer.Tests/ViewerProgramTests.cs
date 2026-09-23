/// <summary>
/// The exit-time half of the data-loss fix: an owning viewer writes what it still holds back to
/// the staging layout, an attached one leaves that to the owner it displays.
/// </summary>
public class ViewerProgramTests
{
    [Test]
    public async Task AnOwningViewerPersistsItsQueueOnExit()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");
        var state = Fixtures.Inline(Fixtures.Patch(source: source, framework: "net10.0"));

        var written = ViewerProgram.PersistOwned(state, link: null);

        await Assert.That(written).IsEqualTo(1);
        var staged = project.StagedFiles();
        await Assert.That(staged.Count).IsEqualTo(3);
        await Assert.That(staged.Count(_ => _.EndsWith(".inlinepatch"))).IsEqualTo(1);
    }

    [Test]
    public async Task AnAttachedViewerPersistsNothing()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");
        var state = Fixtures.Inline(Fixtures.Patch(source: source));

        // The link is what says this window displays someone else's queue. That owner is still
        // holding everything, so writing here would duplicate what is not lost.
        var link = new OwnerLink(new(state), port: 1);
        var written = ViewerProgram.PersistOwned(state, link);

        await Assert.That(written).IsEqualTo(0);
        await Assert.That(project.StagedFiles()).IsEmpty();
    }

    /// <summary>
    /// The port is bound before the window is asked for, so whoever launched the viewer was told
    /// what it sent had been taken. A window that could not open - no display, a native library
    /// that would not load - returned before anything was written, and the snapshot existed
    /// nowhere.
    /// </summary>
    [Test]
    public async Task AViewerWithNoWindowStillStagesWhatItHolds()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");
        var state = Fixtures.Inline(Fixtures.Patch(source: source, framework: "net10.0"));

        var code = ViewerProgram.Run(new(state), server: null, link: null, NoWindow);

        await Assert.That(code).IsEqualTo(4);
        await Assert.That(project.StagedFiles().Count(_ => _.EndsWith(".inlinepatch"))).IsEqualTo(1);
    }

    /// <summary>
    /// A loop that throws ends the way one that returns does. The throw used to unwind straight to
    /// Main's catch, past the persist, and the queue went with the process.
    /// </summary>
    [Test]
    public async Task AViewerWhoseLoopThrowsStillStagesWhatItHolds()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");
        var state = Fixtures.Inline(Fixtures.Patch(source: source, framework: "net10.0"));

        Assert.Throws<InvalidOperationException>(
            () =>
            {
                ViewerProgram.Run(new(state), server: null, link: null, ThrowingWindow.Open);
            });

        await Assert.That(project.StagedFiles().Count(_ => _.EndsWith(".inlinepatch"))).IsEqualTo(1);
    }

    static IViewerWindow? NoWindow(string title, int width, int height, bool hidden, out string? error)
    {
        error = "No display.";
        return null;
    }

    sealed class ThrowingWindow : IViewerWindow
    {
        public static IViewerWindow? Open(string title, int width, int height, bool hidden, out string? error)
        {
            error = null;
            return new ThrowingWindow();
        }

        public bool Present(Screen screen) =>
            throw new InvalidOperationException("The renderer failed.");

        public ViewerInput Poll() =>
            default;

        public void SetHidden(bool hidden)
        {
        }

        public void Focus()
        {
        }

        public void SetClipboard(string text)
        {
        }

        public bool Capture(Screen screen, int width, int height, string pngPath) =>
            false;

        public void Dispose()
        {
        }
    }

    sealed class TempProject : IDisposable
    {
        readonly string directory = Path.Combine(
            Path.GetTempPath(),
            $"viewer-persist-{Guid.NewGuid():N}");

        public TempProject()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "Sample.csproj"), "<Project />");
        }

        public string Source(string name)
        {
            var path = Path.Combine(directory, name);
            File.WriteAllText(path, "// sample");
            return path;
        }

        public IReadOnlyList<string> StagedFiles()
        {
            var staging = Path.Combine(directory, "obj", InlineStaging.DirectoryName);
            return Directory.Exists(staging)
                ? Directory.GetFiles(staging)
                : [];
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // Best effort cleanup of the temp directory.
            }
        }
    }
}
