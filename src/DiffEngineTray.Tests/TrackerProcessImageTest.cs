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

    /// <summary>
    /// The image says which program a process is, and not which window. A stale id that Windows
    /// handed to another copy of the same tool - the one open on the next snapshot along, or one
    /// started by hand for a merge - passed on the image alone, was tracked as this pair's, and
    /// was ended when the pair was accepted. The tool for a pair was started with its received
    /// file, and this one was not.
    /// </summary>
    [Test]
    public async Task AnotherCopyOfTheToolIsNotTrackedAndNotEnded()
    {
        await using var tracker = new RecordingTracker();
        var copy = StartAnotherCopy(shows: Path.Combine(directory, "received", "another.trackerprocessimage"));
        try
        {
            var tracked = tracker.AddMove(temp, target, Image, "theArguments", true, copy.Id);

            await Assert.That(tracked.Process).IsNull();
            await Assert.That(tracked.IsOpen).IsFalse();

            tracker.Accept(tracked);

            await Assert.That(tracker.Moves).IsEmpty();
            await Assert.That(copy.WaitForExit(1000)).IsFalse();
        }
        finally
        {
            FileLockUtils.Cleanup(copy);
        }
    }

    /// <summary>
    /// Open on a file whose path only starts as the received file's does.
    /// </summary>
    [Test]
    public async Task ACopyOfTheToolShowingALongerPathIsNotTracked()
    {
        await using var tracker = new RecordingTracker();
        var copy = StartAnotherCopy(shows: $"{temp}.bak");
        try
        {
            var tracked = tracker.AddMove(temp, target, Image, "theArguments", true, copy.Id);

            await Assert.That(tracked.Process).IsNull();
        }
        finally
        {
            FileLockUtils.Cleanup(copy);
        }
    }

    [Test]
    public async Task AReRunNamingAnotherCopyOfTheToolLeavesTheMoveWithNone()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddMove(temp, target, Image, "theArguments", true, stranger.Id);
        var copy = StartAnotherCopy(shows: null);
        try
        {
            var tracked = tracker.AddMove(temp, target, Image, "theArguments", true, copy.Id);

            await Assert.That(tracked.Process).IsNull();
            tracker.Accept(tracked);
            await Assert.That(copy.WaitForExit(1000)).IsFalse();
        }
        finally
        {
            FileLockUtils.Cleanup(copy);
        }
    }

    /// <summary>
    /// What the comparison reads, asked of a process whose command line is known.
    /// </summary>
    [Test]
    public async Task TheCommandLineOfAProcessIsRead()
    {
        var commandLine = ProcessEx.CommandLine(stranger);

        await Assert.That(commandLine).IsNotNull();
        await Assert.That(commandLine!).Contains("-NoProfile");
        await Assert.That(commandLine).Contains(temp);
        await Assert.That(ProcessEx.WasStartedFor(commandLine, temp)).IsTrue();
    }

    /// <summary>
    /// A 32 bit process asked from a 64 bit one, which is most diff tools: they install under
    /// Program Files (x86). The command line of one is in another place in its memory, which is
    /// why it is asked for rather than read out of there.
    /// </summary>
    [Test]
    public async Task TheCommandLineOfA32BitProcessIsRead()
    {
        var powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
            @"WindowsPowerShell\v1.0\powershell.exe");
        // Nothing to ask where there is no 32 bit side, or no 32 bit PowerShell on it
        if (!Environment.Is64BitProcess ||
            !File.Exists(powershell))
        {
            return;
        }

        using var process = new Process
        {
            StartInfo = new()
            {
                FileName = powershell,
                Arguments = $"-NoProfile -Command \"[Console]::WriteLine('ready'); Start-Sleep -Seconds 60 # {temp}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            }
        };
        process.Start();
        try
        {
            await process.StandardOutput.ReadLineAsync();

            var commandLine = ProcessEx.CommandLine(process);

            await Assert.That(ProcessEx.WasStartedFor(commandLine, temp)).IsTrue();
        }
        finally
        {
            process.Kill();
            process.WaitForExit(5000);
        }
    }

    [Test]
    public async Task ACommandLineNamesAPathOnlyAsTheWholeOfAnArgument()
    {
        const string path = @"C:\temp\Sample.received.txt";

        await Assert.That(ProcessEx.WasStartedFor($"\"tool.exe\" \"C:\\code\\Sample.verified.txt\" \"{path}\"", path)).IsTrue();
        await Assert.That(ProcessEx.WasStartedFor($"\"tool.exe\" /left:\"{path}\" /right:\"C:\\code\\Sample.verified.txt\"", path)).IsTrue();
        await Assert.That(ProcessEx.WasStartedFor($"tool.exe {path} other", path)).IsTrue();
        await Assert.That(ProcessEx.WasStartedFor($"tool.exe {path.ToUpperInvariant()}", path)).IsTrue();
        // Found first as the start of a longer path, and then as itself
        await Assert.That(ProcessEx.WasStartedFor($"tool.exe \"{path}.bak\" \"{path}\"", path)).IsTrue();
        await Assert.That(ProcessEx.WasStartedFor($"tool.exe \"{path}.bak\"", path)).IsFalse();
        await Assert.That(ProcessEx.WasStartedFor("tool.exe \"C:\\temp\\Other.received.txt\"", path)).IsFalse();
        await Assert.That(ProcessEx.WasStartedFor(null, path)).IsFalse();
    }

    /// <summary>
    /// A second process running the image the first does, started with another file or with none.
    /// </summary>
    Process StartAnotherCopy(string? shows)
    {
        var locked = Path.Combine(directory, $"locked-{Guid.NewGuid():N}.txt");
        File.WriteAllText(locked, "");
        return FileLockUtils.StartFileLockProcess(locked, shows);
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
        // Started with the received file on its command line, as a diff tool showing the pair is
        stranger = FileLockUtils.StartFileLockProcess(locked, shows: temp);
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
