public class TrackerLockedMoveTest :
    IDisposable
{
    [Test]
    public async Task Ignore_KeepsMovePending()
    {
        LockedFiles? observed = null;
        await using var tracker = new RecordingTracker(
            (_, locked) =>
            {
                observed = locked;
                return LockedFilesResponse.Ignore;
            });
        var lockProcess = FileLockUtils.StartFileLockProcess(target1);
        try
        {
            var tracked = tracker.AddMove(temp1, target1, "theExe", "theArguments", false, null);
            tracker.Accept(tracked);

            await Assert.That(tracker.Moves).HasSingleItem();
            await Assert.That(observed).IsNotNull();
            await Assert.That(observed!.Files).Contains(target1);
            await Assert.That(observed.Processes.Select(_ => _.ProcessId)).Contains(lockProcess.Id);
        }
        finally
        {
            FileLockUtils.Cleanup(lockProcess);
        }

        await Assert.That(await File.ReadAllTextAsync(target1)).IsEqualTo("old");
    }

    [Test]
    public async Task Kill_AcceptsMove()
    {
        await using var tracker = new RecordingTracker(
            (_, _) => LockedFilesResponse.Kill);
        var lockProcess = FileLockUtils.StartFileLockProcess(target1);
        try
        {
            var tracked = tracker.AddMove(temp1, target1, "theExe", "theArguments", false, null);
            tracker.Accept(tracked);

            await tracker.AssertEmpty();
            await Assert.That(await File.ReadAllTextAsync(target1)).IsEqualTo("new");
            await Assert.That(lockProcess.WaitForExit(5000)).IsTrue();
        }
        finally
        {
            FileLockUtils.Cleanup(lockProcess);
        }
    }

    [Test]
    public async Task KillAndAcceptAllPending_AcceptsOtherMoves()
    {
        var resolveCount = 0;
        await using var tracker = new RecordingTracker(
            (_, _) =>
            {
                resolveCount++;
                return LockedFilesResponse.KillAndAcceptAllPending;
            });
        var lockProcess1 = FileLockUtils.StartFileLockProcess(target1);
        var lockProcess2 = FileLockUtils.StartFileLockProcess(target2);
        try
        {
            var tracked = tracker.AddMove(temp1, target1, "theExe", "theArguments", false, null);
            tracker.AddMove(temp2, target2, "theExe", "theArguments", false, null);

            tracker.Accept(tracked);

            await tracker.AssertEmpty();
            await Assert.That(resolveCount).IsEqualTo(1);
            await Assert.That(await File.ReadAllTextAsync(target1)).IsEqualTo("new");
            await Assert.That(await File.ReadAllTextAsync(target2)).IsEqualTo("new");
        }
        finally
        {
            FileLockUtils.Cleanup(lockProcess1);
            FileLockUtils.Cleanup(lockProcess2);
        }
    }

    [Test]
    public async Task NoResolver_KeepsMovePending()
    {
        await using var tracker = new RecordingTracker();
        var lockProcess = FileLockUtils.StartFileLockProcess(target1);
        try
        {
            var tracked = tracker.AddMove(temp1, target1, "theExe", "theArguments", false, null);
            tracker.Accept(tracked);

            await Assert.That(tracker.Moves).HasSingleItem();
        }
        finally
        {
            FileLockUtils.Cleanup(lockProcess);
        }

        await Assert.That(await File.ReadAllTextAsync(target1)).IsEqualTo("old");
    }

    /// <summary>
    /// A move that fails is re-added, so whatever KillProcesses did to it has to leave it usable.
    /// It used to leave a disposed Process on the entry, and the Accept-open hot key and
    /// "Open diff tool" both read that property - throwing "No process is associated with this
    /// object" on the UI thread, where nothing catches it.
    /// </summary>
    [Test]
    public async Task Ignore_DropsTheKilledProcessFromThePendingMove()
    {
        await using var tracker = new RecordingTracker(
            (_, _) => LockedFilesResponse.Ignore);
        var lockProcess = FileLockUtils.StartFileLockProcess(target1);
        // Stands in for the diff tool: a killable process the move is tracking
        var toolProcess = FileLockUtils.StartFileLockProcess(temp2);
        try
        {
            var tracked = tracker.AddMove(temp1, target1, "theExe", "theArguments", true, toolProcess.Id);
            tracker.Accept(tracked);

            var pending = tracker.Moves.Single();
            await Assert.That(pending.Process).IsNull();
            // What the hot key and DiffToolLauncher do with it
            await Assert.That(pending.Process is { HasExited: false }).IsFalse();
        }
        finally
        {
            FileLockUtils.Cleanup(toolProcess);
            FileLockUtils.Cleanup(lockProcess);
        }
    }
    static string CreateFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"TrackerLockedMoveTest_{Guid.NewGuid()}.txt");
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        File.Delete(temp1);
        File.Delete(temp2);
        File.Delete(target1);
        File.Delete(target2);
    }

    string temp1 = CreateFile("new");
    string temp2 = CreateFile("new");
    string target1 = CreateFile("old");
    string target2 = CreateFile("old");
}