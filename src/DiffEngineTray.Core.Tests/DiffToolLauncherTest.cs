/// <summary>
/// The menu's "Open diff tool" over a tracked move.
/// </summary>
public class DiffToolLauncherTest :
    IDisposable
{
    string file1 = Path.GetTempFileName();
    string file2 = Path.GetTempFileName();

    public void Dispose()
    {
        File.Delete(file1);
        File.Delete(file2);
    }

    /// <summary>
    /// DiffRunner resends a move for the same received file on every re-run, with a process
    /// id each time. The update factory disposes the Process of the move it replaces and leaves it
    /// there, so a menu opened before that re-run holds a move whose process is disposed, and its
    /// "Open diff tool" item throws on the UI thread.
    /// </summary>
    [Test]
    public async Task OpenDiffToolFromAMenuBuiltBeforeTheMoveWasUpdated()
    {
        await using var tracker = new RecordingTracker();
        var temp = file1;
        var target = file2;
        // Stands in for an auto refresh diff tool that is still open, which is what DiffRunner
        // resends the id of
        var tool = StandInTool.Start(shows: temp);
        try
        {
            // Nothing at this path exists, so if the launcher gets past the process it starts
            // nothing. Named as the tool is, since a process running something else is not tracked
            var exe = Path.Combine(
                Path.GetTempPath(),
                $"ReviewReproNoSuchTool_{Guid.NewGuid()}",
                Path.GetFileName(StandInTool.Image));

            // What the menu captured when it opened
            var shown = tracker.AddMove(temp, target, exe, "theArguments", false, tool.Id);
            await Assert.That(shown.Process).IsNotNull();
            // The re-run's move, landing while that menu is open
            tracker.AddMove(temp, target, exe, "theArguments", false, tool.Id);

            Exception? thrown = null;
            try
            {
                // The "Open diff tool" item's click handler
                DiffToolLauncher.Launch(shown);
            }
            catch (Exception exception)
            {
                thrown = exception;
            }

            await Assert.That(thrown).IsNull();
        }
        finally
        {
            FileLockUtils.Cleanup(tool);
        }
    }

    /// <summary>
    /// Off Windows nothing brings another program's window forward, so a tool still showing the
    /// pair is ended and started again, which puts a window in front.
    /// </summary>
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Linux | TUnit.Core.Enums.OS.MacOs)]
    public async Task AToolStillShowingThePairIsEndedAndStartedAgain()
    {
        await using var tracker = new RecordingTracker();
        WriteDifferentFiles();
        var tool = StandInTool.Start(shows: file1);
        try
        {
            // Started again as bash told to exit, so nothing of it is left running
            var move = tracker.AddMove(file1, file2, StandInTool.Image, "-c exit", true, tool.Id);
            await Assert.That(move.Process).IsNotNull();

            DiffToolLauncher.Launch(move);

            await Assert.That(tool.WaitForExit(5000)).IsTrue();
            // Left on the move, which the tracker lets go of
            var started = move.Process;
            await Assert.That(started).IsNotNull();
            await Assert.That(started!.Id).IsNotEqualTo(tool.Id);
            started.WaitForExit(5000);
        }
        finally
        {
            FileLockUtils.Cleanup(tool);
        }
    }

    /// <summary>
    /// The id a move was tracked with, gone to another process by the time "Open diff tool" is
    /// chosen, which off Windows nothing held prevents. Stood in for by putting another copy of
    /// the tool on the move. That copy is left alone, and the tool is started.
    /// </summary>
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Linux | TUnit.Core.Enums.OS.MacOs)]
    public async Task AProcessNoLongerTheToolIsNotEnded()
    {
        await using var tracker = new RecordingTracker();
        WriteDifferentFiles();
        var tool = StandInTool.Start(shows: file1);
        var other = StandInTool.Start(shows: null);
        try
        {
            var move = tracker.AddMove(file1, file2, StandInTool.Image, "-c exit", true, tool.Id);
            move.Process!.Dispose();
            move.Process = Process.GetProcessById(other.Id);

            DiffToolLauncher.Launch(move);

            await Assert.That(other.WaitForExit(1000)).IsFalse();
            var started = move.Process;
            await Assert.That(started).IsNotNull();
            started!.WaitForExit(5000);
        }
        finally
        {
            FileLockUtils.Cleanup(tool);
            FileLockUtils.Cleanup(other);
        }
    }

    /// <summary>
    /// The tracker's scan drops a pair whose two files are the same, and ends its tool.
    /// </summary>
    void WriteDifferentFiles()
    {
        File.WriteAllText(file1, "received");
        File.WriteAllText(file2, "verified");
    }
}
