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

    /// <summary>
    /// The window is opened from what the last one left, and what this one leaves is kept for the
    /// next: maximise, close and run again used to open at the default size every time.
    /// </summary>
    [Test]
    public async Task TheWindowOpensAsTheLastWasLeftAndLeavesItsOwnForTheNext()
    {
        var preferences = new ViewerPreferences
        {
            Window = new(10, 20, 900, 600, true)
        };
        WindowPlacement? opened = null;

        IViewerWindow Open(string title, int width, int height, bool hidden, WindowPlacement? placement, out string? error)
        {
            opened = placement;
            error = null;
            return new PlacedWindow(new(30, 40, 1000, 700, false));
        }

        var code = ViewerProgram.Run(new(Fixtures.File()), server: null, link: null, Open, preferences: preferences);

        await Assert.That(code).IsEqualTo(0);
        await Assert.That(opened).IsEqualTo(new WindowPlacement(10, 20, 900, 600, true));
        await Assert.That(preferences.Window).IsEqualTo(new WindowPlacement(30, 40, 1000, 700, false));
    }

    /// <summary>
    /// A head that cannot say where its window is leaves what was remembered alone, rather than
    /// forgetting it.
    /// </summary>
    [Test]
    public async Task AWindowThatCannotSayWhereItIsLeavesWhatWasRemembered()
    {
        var preferences = new ViewerPreferences
        {
            Window = new(10, 20, 900, 600, true)
        };

        static IViewerWindow Open(string title, int width, int height, bool hidden, WindowPlacement? placement, out string? error)
        {
            error = null;
            return new PlacedWindow(null);
        }

        ViewerProgram.Run(new(Fixtures.File()), server: null, link: null, Open, preferences: preferences);

        await Assert.That(preferences.Window).IsEqualTo(new WindowPlacement(10, 20, 900, 600, true));
    }

    /// <summary>
    /// Closed on its first frame, which is all a test of what happens around the loop needs.
    /// </summary>
    sealed class PlacedWindow(WindowPlacement? placement) : IViewerWindow
    {
        public WindowPlacement? Placement =>
            placement;

        public bool Present(Screen screen) =>
            false;

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

    static IViewerWindow? NoWindow(string title, int width, int height, bool hidden, WindowPlacement? placement, out string? error)
    {
        error = "No display.";
        return null;
    }

    sealed class ThrowingWindow : IViewerWindow
    {
        public static IViewerWindow Open(string title, int width, int height, bool hidden, WindowPlacement? placement, out string? error)
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
            if (Directory.Exists(staging))
            {
                return Directory.GetFiles(staging);
            }

            return (string[])[];
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
