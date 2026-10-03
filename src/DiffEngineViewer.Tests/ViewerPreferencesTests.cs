/// <summary>
/// What the viewer remembers between runs, and that forgetting it is never worse than a window
/// that opens the way a first one does.
/// </summary>
public class ViewerPreferencesTests
{
    [Test]
    public async Task WhatOneViewerRemembersTheNextOneReads()
    {
        using var directory = new TempDirectory();
        var path = directory.File("viewer.settings");

        new ViewerPreferences(path).Window = new(40, 60, 1200, 800, true);

        await Assert.That(new ViewerPreferences(path).Window).IsEqualTo(new WindowPlacement(40, 60, 1200, 800, true));
    }

    /// <summary>
    /// A second monitor to the left of the first puts a window at a negative x, which is a
    /// placement like any other.
    /// </summary>
    [Test]
    public async Task APlacementLeftOfTheFirstDisplayIsKept()
    {
        using var directory = new TempDirectory();
        var path = directory.File("viewer.settings");

        new ViewerPreferences(path).Window = new(-1900, -20, 900, 600, false);

        await Assert.That(new ViewerPreferences(path).Window).IsEqualTo(new WindowPlacement(-1900, -20, 900, 600, false));
    }

    [Test]
    public async Task NoFileIsNoPreferences()
    {
        using var directory = new TempDirectory();

        var preferences = new ViewerPreferences(directory.File("missing", "viewer.settings"));

        await Assert.That(preferences.Window).IsNull();
    }

    /// <summary>
    /// The folder is made on the first write rather than expected: a machine with no tray has
    /// never had one.
    /// </summary>
    [Test]
    public async Task TheFolderIsMadeOnTheFirstWrite()
    {
        using var directory = new TempDirectory();
        var path = directory.File("DiffEngine", "viewer.settings");

        new ViewerPreferences(path).Window = new(1, 2, 300, 400, false);

        await Assert.That(File.Exists(path)).IsTrue();
    }

    /// <summary>
    /// The file is one a person can open and edit, so what is in it may be anything. A line that
    /// is not a placement is no placement, rather than a viewer that will not start.
    /// </summary>
    [Test]
    [Arguments("window=")]
    [Arguments("window=wide")]
    [Arguments("window=1,2,3")]
    [Arguments("window=1,2,0,400,0")]
    [Arguments("window=1,2,300,-4,0")]
    [Arguments("window=1,2,300,400,0,9")]
    [Arguments("not a setting at all")]
    public async Task ALineThatMakesNoSenseIsIgnored(string line)
    {
        using var directory = new TempDirectory();
        var path = directory.File("viewer.settings");
        await File.WriteAllTextAsync(path, line);

        await Assert.That(new ViewerPreferences(path).Window).IsNull();
    }

    /// <summary>
    /// Two viewers can be open at once, and each knows only what was in the file when it started.
    /// Writing all of that back would undo whatever the other had remembered since.
    /// </summary>
    [Test]
    public async Task AWriteKeepsWhatAnotherViewerWroteSince()
    {
        using var directory = new TempDirectory();
        var path = directory.File("viewer.settings");
        var first = new ViewerPreferences(path);
        var second = new ViewerPreferences(path);

        first.Set("projection", "Goode");
        second.Window = new(5, 6, 700, 500, false);

        var read = new ViewerPreferences(path);
        await Assert.That(read.Get("projection")).IsEqualTo("Goode");
        await Assert.That(read.Window).IsEqualTo(new WindowPlacement(5, 6, 700, 500, false));
    }

    /// <summary>
    /// A setting at its default is kept as no line at all, so putting one back takes a line out of
    /// the file. The other viewer read that line when it started, and used to write it back with
    /// the next thing it remembered, since the file no longer had a value of its own to keep.
    /// </summary>
    [Test]
    public async Task ASettingAnotherViewerPutBackToItsDefaultStaysThere()
    {
        using var directory = new TempDirectory();
        var path = directory.File("viewer.settings");
        var before = new ViewerPreferences(path)
        {
            Projection = MapProjection.Goode,
            Drawings = new Dictionary<DocumentFormat, DrawingView>
            {
                [DocumentFormat.Pdf] = DrawingView.Picture
            }
        };
        await Assert.That(before.Projection).IsEqualTo(MapProjection.Goode);
        var first = new ViewerPreferences(path);

        // A second viewer, started after the first, puts both back
        var second = new ViewerPreferences(path)
        {
            Projection = MapProjection.Auto,
            Drawings = new Dictionary<DocumentFormat, DrawingView>()
        };
        await Assert.That(second.Get("projection")).IsNull();
        first.Window = new(5, 6, 700, 500, false);

        var read = new ViewerPreferences(path);
        await Assert.That(read.Projection).IsEqualTo(MapProjection.Auto);
        await Assert.That(read.Drawings).IsEmpty();
        await Assert.That(read.Window).IsEqualTo(new WindowPlacement(5, 6, 700, 500, false));
    }

    /// <summary>
    /// The viewer still showing the old setting is asked what it shows after every frame that did
    /// something. That is not a change of its own, so it writes nothing: what it holds is its own
    /// view, never brought up to date with a file it then looks different from.
    /// </summary>
    [Test]
    public async Task SayingWhatIsOnScreenDoesNotUndoAnotherViewersChange()
    {
        using var directory = new TempDirectory();
        var path = directory.File("viewer.settings");
        new ViewerPreferences(path).Projection = MapProjection.Goode;
        var first = new ViewerPreferences(path);
        var state = first.Apply(SessionState.Start(ViewerMode.Inline));
        await Assert.That(state.Projection).IsEqualTo(MapProjection.Goode);

        new ViewerPreferences(path).Projection = MapProjection.Auto;
        first.Window = new(5, 6, 700, 500, false);
        first.Remember(state);

        await Assert.That(new ViewerPreferences(path).Projection).IsEqualTo(MapProjection.Auto);
        await Assert.That(first.Projection).IsEqualTo(MapProjection.Goode);
    }

    /// <summary>
    /// A setting whose write failed is still this viewer's to write, and goes with the next one
    /// that can be: here a directory was in the way of the file, then was not.
    /// </summary>
    [Test]
    public async Task ASettingThatCouldNotBeWrittenGoesWithTheNextThatCan()
    {
        using var directory = new TempDirectory();
        var path = directory.File("viewer.settings");
        Directory.CreateDirectory(path);
        var preferences = new ViewerPreferences(path)
        {
            Projection = MapProjection.Lambert
        };
        Directory.Delete(path);

        preferences.Window = new(1, 2, 300, 400, false);

        var read = new ViewerPreferences(path);
        await Assert.That(read.Projection).IsEqualTo(MapProjection.Lambert);
        await Assert.That(read.Window).IsEqualTo(new WindowPlacement(1, 2, 300, 400, false));
    }

    [Test]
    public async Task NullForgetsAKey()
    {
        using var directory = new TempDirectory();
        var path = directory.File("viewer.settings");
        var preferences = new ViewerPreferences(path);
        preferences.Set("projection", "Goode");

        preferences.Set("projection", null);

        await Assert.That(new ViewerPreferences(path).Get("projection")).IsNull();
    }

    /// <summary>
    /// With nowhere to keep them they are still held for the process, which is what a test gets
    /// and what a user with no profile directory does.
    /// </summary>
    [Test]
    public async Task WithNoFileTheyAreHeldForTheProcess()
    {
        var preferences = new ViewerPreferences
        {
            Window = new(1, 2, 300, 400, true)
        };

        await Assert.That(preferences.Window).IsEqualTo(new WindowPlacement(1, 2, 300, 400, true));
    }

    /// <summary>
    /// A path that cannot be written - here because a directory is in the way - loses the setting
    /// and nothing else. It is asked for as the window closes, where a throw would take the
    /// queue's staging with it.
    /// </summary>
    [Test]
    public async Task AFileThatCannotBeWrittenIsNotAnError()
    {
        using var directory = new TempDirectory();
        var path = directory.File("viewer.settings");
        Directory.CreateDirectory(path);
        var preferences = new ViewerPreferences(path)
        {
            Window = new(1, 2, 300, 400, false)
        };

        await Assert.That(preferences.Window).IsEqualTo(new WindowPlacement(1, 2, 300, 400, false));
    }

    sealed class TempDirectory : IDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), $"viewer-preferences-{Guid.NewGuid():N}");

        public TempDirectory() =>
            Directory.CreateDirectory(root);

        public string File(params string[] parts) =>
            Path.Combine([root, .. parts]);

        public void Dispose()
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Best effort cleanup of the temp directory.
            }
        }
    }
}
