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
    /// A tool started through a script runs under the script's interpreter, so the id is that of
    /// cmd.exe, or of a shell off Windows, and not of anything named by the move. It is left
    /// alone: ending the interpreter closes no diff window, and believing any cmd.exe to be the
    /// tool is how somebody's shell would be ended.
    /// </summary>
    [Test]
    public async Task AToolStartedThroughAScriptIsNotTracked()
    {
        await using var tracker = new RecordingTracker();
        var extension = OperatingSystem.IsWindows() ? ".cmd" : ".sh";
        var script = Path.Combine(Path.GetDirectoryName(Image)!, Path.ChangeExtension(Path.GetFileName(Image), extension));

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
        // The stand-in's script, so the command line is read from its start
        await Assert.That(commandLine!).Contains("ready");
        await Assert.That(commandLine).Contains(temp);
        await Assert.That(ProcessEx.WasStartedFor(commandLine, temp)).IsTrue();
    }

    /// <summary>
    /// Off Windows nothing held keeps an id from being handed out again, so the process a move was
    /// tracked with can be another one by the time the move is accepted. Stood in for by putting a
    /// copy of the tool that was not started for the pair on the move, which is what an id handed
    /// on looks like from here. On Windows the handle held means it cannot happen.
    /// </summary>
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Linux | TUnit.Core.Enums.OS.MacOs)]
    public async Task AToolWhoseIdWasHandedOnIsNotEnded()
    {
        await using var tracker = new RecordingTracker();
        var tracked = tracker.AddMove(temp, target, Image, "theArguments", true, stranger.Id);
        var copy = StartAnotherCopy(shows: null);
        try
        {
            tracked.Process!.Dispose();
            tracked.Process = Process.GetProcessById(copy.Id);

            tracker.Accept(tracked);

            await Assert.That(tracker.Moves).IsEmpty();
            await Assert.That(tracked.Process).IsNull();
            await Assert.That(copy.WaitForExit(1000)).IsFalse();
        }
        finally
        {
            FileLockUtils.Cleanup(copy);
        }
    }

    /// <summary>
    /// A process disposed and left on its move, which is no longer anything to ask about. Off
    /// Windows the question asked before a kill read its id, which a disposed process throws for,
    /// and the accept threw where it had always let one through.
    /// </summary>
    [Test]
    public async Task AMoveHoldingADisposedProcessIsStillAccepted()
    {
        await using var tracker = new RecordingTracker();
        var tracked = tracker.AddMove(temp, target, Image, "theArguments", true, stranger.Id);
        tracked.Process!.Dispose();

        tracker.Accept(tracked);

        await Assert.That(tracker.Moves).IsEmpty();
        await Assert.That(tracked.Process).IsNull();
    }

    /// <summary>
    /// A 32 bit process asked from a 64 bit one, which is most diff tools: they install under
    /// Program Files (x86). The command line of one is in another place in its memory, which is
    /// why it is asked for rather than read out of there.
    /// <para>
    /// Windows only: the 32 bit side is Windows'.
    /// </para>
    /// </summary>
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Windows)]
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
    static Process StartAnotherCopy(string? shows) =>
        StandInTool.Start(shows);

    [Test]
    public async Task AMoveNamingNoToolIsNotGivenAProcess()
    {
        await using var tracker = new RecordingTracker();

        var tracked = tracker.AddMove(temp, target, null, null, true, stranger.Id);

        await Assert.That(tracked.Process).IsNull();
    }

    static string Image => StandInTool.Image;

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
        // Started with the received file on its command line, as a diff tool showing the pair is
        stranger = StandInTool.Start(shows: temp);
    }

    public void Dispose()
    {
        FileLockUtils.Cleanup(stranger);
        Directory.Delete(directory, true);
    }

    readonly string directory;
    readonly string temp;
    readonly string target;
    readonly Process stranger;
}
