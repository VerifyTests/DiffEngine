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

        // And says so, where it used to say only that it failed: whoever launched it stages what
        // it sent on a failure, and that was a second trio beside this one
        await Assert.That(code).IsEqualTo(ViewerExit.Staged);
        await Assert.That(project.StagedFiles().Count(_ => _.EndsWith(".inlinepatch"))).IsEqualTo(1);
    }

    /// <summary>
    /// Staged is said only where it is so. A snapshot whose source has no project above it has
    /// nowhere to be staged, and one of two left unwritten is still nowhere: the exit is the
    /// failure it always was, which is what has the launcher keep the patch it sent.
    /// </summary>
    [Test]
    public async Task AViewerWithNoWindowThatCouldNotStageEverythingSaysItFailed()
    {
        using var project = new TempProject();
        var source = project.Source("SampleTests.cs");
        var nowhere = Path.Combine(Path.GetTempPath(), $"viewer-persist-{Guid.NewGuid():N}.cs");
        var state = Fixtures.Inline(
            Fixtures.Patch(source: source, framework: "net10.0"),
            Fixtures.Patch(source: nowhere, framework: "net10.0"));

        var code = ViewerProgram.Run(new(state), server: null, link: null, NoWindow);

        await Assert.That(code).IsEqualTo(4);
        await Assert.That(project.StagedFiles().Count(_ => _.EndsWith(".inlinepatch"))).IsEqualTo(1);
    }

    /// <summary>
    /// A viewer with nothing of a snapshot in it has staged nothing, whatever it was started for.
    /// </summary>
    [Test]
    public async Task AViewerWithNoWindowAndNoSnapshotsSaysItFailed()
    {
        var code = ViewerProgram.Run(new(Fixtures.File()), server: null, link: null, NoWindow);

        await Assert.That(code).IsEqualTo(4);
    }

    /// <summary>
    /// A viewer started for a delete or a pair can be holding snapshots other processes sent it
    /// by the time its window fails, and staging those says nothing of the file it was started
    /// for. Its launcher is told the launch failed.
    /// </summary>
    [Test]
    public async Task AViewerStartedForAFileNeverSaysStaged()
    {
        await Assert.That(ViewerProgram.ForAFile(ViewerExit.Staged)).IsEqualTo(4);
        await Assert.That(ViewerProgram.ForAFile(0)).IsEqualTo(0);
        await Assert.That(ViewerProgram.ForAFile(1)).IsEqualTo(1);
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
    /// A window in the taskbar or the Dock, or wholly behind another, is one nobody is reading,
    /// and the loop only knew that of a window it had hidden itself: the owner went on being
    /// listed, the pending files watched and the documents drawn as for one on screen. A head
    /// says so with each poll now, and what is kept going beside the window is slowed for as long
    /// as it does.
    /// </summary>
    [Test]
    public async Task AWindowNobodyCanSeeSlowsWhatIsKeptGoingBesideIt()
    {
        var host = new SessionHost(Fixtures.File());
        var link = new OwnerLink(host, port: 1);
        var watch = new TrackedWatch(host);
        // None of the three is run, so the reader is never asked for a document: each is only
        // told whether to slow
        var reader = new DocumentWatch(host, null!);
        List<string> slowed = [];
        var window = new ScriptedWindow(
            [true, true, false],
            () => slowed.Add($"{link.Hidden} {watch.Hidden} {reader.Hidden}"));

        ViewerProgram.Loop(host, window, link, reader, watch, new(), null, new(), new());

        // As each frame was presented: before anything was said, after each of the two polls
        // that said nobody could see the window, and after the one that said somebody could
        await Assert.That(string.Join(", ", slowed))
            .IsEqualTo("False False False, True True True, True True True, False False False");
    }

    /// <summary>
    /// The two reasons come and go apart. A window hidden from here stays slowed whatever its head
    /// goes on to say, since a head need not count a window it was told to hide.
    /// </summary>
    [Test]
    public async Task AWindowHiddenFromHereStaysSlowedWhateverItsHeadSays()
    {
        var host = new SessionHost(Fixtures.File());
        var watch = new TrackedWatch(host);
        List<bool> slowed = [];
        var window = new ScriptedWindow([true, false], () => slowed.Add(watch.Hidden));
        ConcurrentQueue<WindowCommand> commands = new();
        commands.Enqueue(WindowCommand.Hide);

        ViewerProgram.Loop(host, window, null, null, watch, commands, null, new(), new());

        await Assert.That(string.Join(", ", slowed)).IsEqualTo("True, True, True");
    }

    /// <summary>
    /// A window that says, poll by poll, whether anybody can see it, and nothing else. It closes
    /// on the present after its last poll, and tells the test as each present begins.
    /// </summary>
    sealed class ScriptedWindow(bool[] unseen, Action presenting) : IViewerWindow
    {
        int polls;

        public bool Present(Screen screen)
        {
            presenting();
            return polls < unseen.Length;
        }

        public ViewerInput Poll() =>
            // Not default: that zeroes every index, and zero is the first button and the first row
            new(CommandKind.None, -1, -1, 0, false, Fixtures.Columns, Fixtures.Rows, Unseen: unseen[polls++]);

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
