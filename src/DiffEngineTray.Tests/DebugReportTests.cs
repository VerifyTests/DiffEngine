/// <summary>
/// The text behind the "Debug view" menu item. Verified as text rather than as a rendered window,
/// since the window is a box to put this in.
/// </summary>
public class DebugReportTests :
    IDisposable
{
    /// <summary>
    /// Passed in rather than read from the clock, so the header needs no scrubbing.
    /// </summary>
    static DateTime now = new(2024, 10, 1, 13, 45, 30);

    string directory;
    string received;
    string verified;
    VerifySettings settings;

    public DebugReportTests()
    {
        VersionReader.VersionString = "TheVersion";
        directory = Path.Combine(Path.GetTempPath(), "DiffEngineDebugReport");
        Directory.CreateDirectory(directory);
        received = Path.Combine(directory, "Sample.Test.received.txt");
        verified = Path.Combine(directory, "Sample.Test.verified.txt");
        // Only the received file, so the report shows both sides of the existence check
        File.WriteAllText(received, "");

        settings = new();
        settings.AddScrubber(_ => _.Replace(directory, "{Directory}"));
        // Read when the scrubber runs, not when it is added: every test points at an ephemeral
        // port so that a viewer running on this machine is not talked to, and FakeViewer moves it
        // again for the tests that want one
        settings.AddScrubber(_ => _.Replace(ViewerClient.Port.ToString(), "{Port}"));
    }

    [Test]
    public async Task Empty()
    {
        await using var tracker = new RecordingTracker();

        await Verify(DebugReport.Build(tracker, now), settings);
    }

    [Test]
    public async Task Full()
    {
        using var viewer = new FakeViewer("Sample.cs:12", "Other.cs:40");
        // A snapshot that failed to apply stays queued with what it failed with, and the debug
        // view is where the whole message is readable rather than the menu's "!"
        viewer.Queue.Add(new(@"c:\repo\failed.cs|7", "Failed.cs:7", "the file is locked"));
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(Path.Combine(directory, "Extra.verified.txt"));
        tracker.AddMove(
            received,
            verified,
            @"C:\tools\diff.exe",
            $"\"{received}\" \"{verified}\"",
            canKill: true,
            processId: null);

        await Verify(DebugReport.Build(tracker, now), settings);
    }

    /// <summary>
    /// The usual arrangement: the tray started first, so it holds the queue and the patches are in
    /// this process rather than in a viewer.
    /// </summary>
    [Test]
    public async Task Owned()
    {
        await using var host = OwnedInlineHost.TryOwn(_ => { }, new NoWindow(), port: 0) ??
                               throw new("Could not bind an ephemeral port.");
        host.Start();
        // Ephemeral, so a viewer or tray running on this machine is not in the way
        settings.AddScrubber(_ => _.Replace(host.Port.ToString(), "{Port}"));
        // The key a patch is filed under is case folded, so the directory shows up twice
        settings.AddScrubber(_ => _.Replace(directory.ToLowerInvariant(), "{Directory}"));

        var source = Path.Combine(directory, "SampleTests.cs");
        File.WriteAllText(source, "");
        var patch = new InlinePatch(source, 42, "\"old\"", "line one\nline two");
        // Over the socket, as the test process that failed the assertion sends it
        if (!ViewerClient.TrySend(new(ViewerVerb.Inline, Body: InlinePatchFile.Build(patch)), out _, host.Port))
        {
            throw new("The patch was not queued.");
        }

        await using var tracker = new RecordingTracker(inline: host);

        await Verify(DebugReport.Build(tracker, now), settings);
    }

    /// <summary>
    /// Owning the queue means a window can be asked for, and this test wants none. Reporting one
    /// as already up means none is ever started.
    /// </summary>
    sealed class NoWindow :
        IViewerLauncher
    {
        public bool Running => true;

        public bool Launch() =>
            true;
    }

    public void Dispose() =>
        Directory.Delete(directory, true);
}
