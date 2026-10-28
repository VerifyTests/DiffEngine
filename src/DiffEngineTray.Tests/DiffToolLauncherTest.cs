/// <summary>
/// The menu's "Open diff tool", which is the head's, over a tracked move: the tracker's own tests
/// are the core's, and run on every platform.
/// </summary>
public class DiffToolLauncherTest :
    IDisposable
{
    string file1 = Path.GetTempFileName();
    string file2 = Path.GetTempFileName();
    string file3 = Path.GetTempFileName();

    public void Dispose()
    {
        File.Delete(file1);
        File.Delete(file2);
        File.Delete(file3);
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
        var toolLock = file3;
        // Stands in for an auto refresh diff tool that is still open, which is what DiffRunner
        // resends the id of
        var tool = FileLockUtils.StartFileLockProcess(toolLock, shows: temp);
        try
        {
            // Nothing at this path exists, so if the launcher gets past the process it starts
            // nothing. Named as the tool is, since a process running something else is not tracked
            var exe = Path.Combine(
                Path.GetTempPath(),
                $"ReviewReproNoSuchTool_{Guid.NewGuid()}",
                Path.GetFileName(tool.MainModule!.FileName));

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
}
