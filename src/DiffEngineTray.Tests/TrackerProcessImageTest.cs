/// <summary>
/// A move's process id is a claim by another process about which one is the diff tool, and a
/// library from before <c>ProcessCleanup.StillRunning</c> can send the id of a tool closed since
/// its test run began, which Windows has handed to something else. The tray holds whatever has
/// the id and, on accept, ends it. So the id is believed only when the process holding it runs
/// the executable the move names.
/// <para>
/// Every process here is started by the test, and stands in both for a diff tool and for the
/// stranger that got a diff tool's id.
/// </para>
/// </summary>
[NotInParallel]
public class TrackerProcessImageTest :
    IDisposable
{
    [Test]
    public async Task AProcessRunningSomethingElseIsNotTrackedAndNotEnded()
    {
        await using var tracker = new RecordingTracker();

        var tracked = tracker.AddMove(temp, target, @"C:\Tools\TheDiffTool\TheDiffTool.exe", "theArguments", true, stranger.Id);

        await Assert.That(tracked.Process).IsNull();
        await Assert.That(tracked.IsOpen).IsFalse();

        tracker.Accept(tracked);

        await Assert.That(tracker.Moves).IsEmpty();
        await Assert.That(stranger.WaitForExit(1000)).IsFalse();
    }

    [Test]
    public async Task AProcessRunningTheToolIsTrackedAndEnded()
    {
        await using var tracker = new RecordingTracker();

        var tracked = tracker.AddMove(temp, target, Image, "theArguments", true, stranger.Id);

        await Assert.That(tracked.Process!.Id).IsEqualTo(stranger.Id);

        tracker.Accept(tracked);

        await Assert.That(stranger.WaitForExit(5000)).IsTrue();
    }

    /// <summary>
    /// By file name. One executable has several paths - a junction, a substituted drive, a short
    /// name, another case - and which of them the sender resolved is not which the system reports,
    /// so a comparison of whole paths would stop closing tools it should close.
    /// </summary>
    [Test]
    public async Task TheToolUnderAnotherSpellingOfItsPathIsTracked()
    {
        await using var tracker = new RecordingTracker();
        var spelled = Path.Combine(@"X:\elsewhere", Path.GetFileName(Image).ToUpperInvariant());

        var tracked = tracker.AddMove(temp, target, spelled, "theArguments", true, stranger.Id);

        await Assert.That(tracked.Process!.Id).IsEqualTo(stranger.Id);
    }

    /// <summary>
    /// A tool started through a script runs under the command interpreter, so the id is that of
    /// cmd.exe and not of anything named by the move. It is left alone: ending the interpreter
    /// closes no diff window, and believing any cmd.exe to be the tool is how somebody's shell
    /// would be ended.
    /// </summary>
    [Test]
    public async Task AToolStartedThroughAScriptIsNotTracked()
    {
        await using var tracker = new RecordingTracker();
        var script = Path.Combine(Path.GetDirectoryName(Image)!, Path.ChangeExtension(Path.GetFileName(Image), ".cmd"));

        var tracked = tracker.AddMove(temp, target, script, "theArguments", true, stranger.Id);

        await Assert.That(tracked.Process).IsNull();
    }

    [Test]
    public async Task AReRunNamingAProcessRunningSomethingElseLeavesTheMoveWithNone()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddMove(temp, target, Image, "theArguments", true, stranger.Id);

        var tracked = tracker.AddMove(temp, target, @"C:\Tools\TheDiffTool\TheDiffTool.exe", "theArguments", true, stranger.Id);

        await Assert.That(tracked.Process).IsNull();
        tracker.Accept(tracked);
        await Assert.That(stranger.WaitForExit(1000)).IsFalse();
    }

    [Test]
    public async Task AMoveNamingNoToolIsNotGivenAProcess()
    {
        await using var tracker = new RecordingTracker();

        var tracked = tracker.AddMove(temp, target, null, null, true, stranger.Id);

        await Assert.That(tracked.Process).IsNull();
    }

    string Image => stranger.MainModule!.FileName;

    public TrackerProcessImageTest()
    {
        directory = Path.Combine(Path.GetTempPath(), "DiffEngineTray.Tests", Guid.NewGuid().ToString("N"));
        var received = Path.Combine(directory, "received");
        Directory.CreateDirectory(received);
        // An extension no diff tool is registered for, so a move naming no tool resolves none
        temp = Path.Combine(received, "file.trackerprocessimage");
        target = Path.Combine(directory, "file.trackerprocessimage");
        File.WriteAllText(temp, "received");
        File.WriteAllText(target, "verified");
        var locked = Path.Combine(directory, "locked.txt");
        File.WriteAllText(locked, "");
        stranger = FileLockUtils.StartFileLockProcess(locked);
    }

    public void Dispose()
    {
        FileLockUtils.Cleanup(stranger);
        try
        {
            Directory.Delete(directory, true);
        }
        catch (IOException)
        {
            // The process that held a file in it is still on its way out
        }
    }

    readonly string directory;
    readonly string temp;
    readonly string target;
    readonly Process stranger;
}
