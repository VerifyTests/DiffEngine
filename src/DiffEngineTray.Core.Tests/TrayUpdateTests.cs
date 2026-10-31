/// <summary>
/// The tray's side of PlatformHeads' update: which install it updates, and what it says after.
/// How the update runs is PlatformHeads' and tested there.
/// </summary>
public class TrayUpdateTests
{
    /// <summary>
    /// A tray installed as a RID specific tool runs from its RID's package in the store, and the
    /// command is beside the store, whether it went in with --global or --tool-path.
    /// </summary>
    [Test]
    public async Task AnInstalledTrayUpdatesTheToolPathItIsIn()
    {
        var tools = Path.Combine(Path.GetTempPath(), "home", ".dotnet", "tools");
        var directory = Path.Combine(tools, ".store", "diffenginetray", "21.0.0", "diffenginetray.linux-x64", "21.0.0", "tools", "net10.0", "linux-x64");

        var update = TrayUpdate.Find(directory)!;

        await Assert.That(update.PackageId).IsEqualTo("DiffEngineTray");
        await Assert.That(update.ToolPath).IsEqualTo(tools);
        await Assert.That(update.Shim).IsEqualTo(Path.Combine(tools, OperatingSystem.IsWindows() ? "DiffEngineTray.exe" : "DiffEngineTray"));
        await Assert.That(update.Outcome).IsEqualTo(TrayUpdate.OutcomePath);
    }

    /// <summary>
    /// A build's output, as every test run is, has no tool to update.
    /// </summary>
    [Test]
    public async Task ABuildHasNothingToUpdate()
    {
        await Assert.That(TrayUpdate.Find()).IsNull();
        // Asked as a bool: handed an Action, That asserts on running it
        await Assert.That(TrayUpdate.MenuAction(() => { }) is null).IsTrue();
    }

    [Test]
    public async Task AnUpdateThatWorkedSaysWhatIsRunningNow()
    {
        using var file = new TempOutcome("ok\nTool 'diffenginetray' was successfully updated from version '21.0.0' to version '21.1.0'.\n");

        var report = TrayUpdate.Report(file.Path)!;

        await Assert.That(report.Title).IsEqualTo("DiffEngineTray updated");
        await Assert.That(report.Message).IsEqualTo($"Now running {VersionReader.VersionString}.");
        await Assert.That(report.Kind).IsEqualTo(TrayNotificationKind.Info);
        // Once: the next start is not another update
        await Assert.That(TrayUpdate.Report(file.Path)).IsNull();
    }

    [Test]
    public async Task AnUpdateThatFailedSaysWhy()
    {
        using var file = new TempOutcome("failed\nTool 'diffenginetray' failed to update due to the following:\nThe network is down.\n");

        var report = TrayUpdate.Report(file.Path)!;

        await Assert.That(report.Title).IsEqualTo("DiffEngineTray did not update");
        await Assert.That(report.Message).IsEqualTo("The network is down.");
        await Assert.That(report.Kind).IsEqualTo(TrayNotificationKind.Error);
    }

    [Test]
    public async Task NoUpdateNothingToSay()
    {
        using var file = new TempOutcome(null);

        await Assert.That(TrayUpdate.Report(file.Path)).IsNull();
    }

    /// <summary>
    /// What the update's Windows script stops, matched as it matches: anything under the store or
    /// a folder named beside it, whatever the case, and nothing in a folder that only starts with
    /// the same name.
    /// </summary>
    [Test]
    public async Task TheUpdateStopsWhatRunsFromTheStore()
    {
        var tools = Path.Combine(Path.GetTempPath(), "home", ".dotnet", "tools");
        var also = Path.Combine(Path.GetTempPath(), "fixed", "viewer");
        var update = new ToolUpdate("DiffEngineTray", Path.Combine(tools, "DiffEngineTray.exe"), Path.Combine(tools, "outcome.log"))
        {
            AlsoStopUnder = [also]
        };
        var viewer = Path.Combine(tools, ".store", "diffenginetray", "21.0.0", "diffenginetray.win-x64", "21.0.0", "tools", "net10.0", "win-x64", "viewer", "win-x64", "DiffEngineViewer.exe");

        await Assert.That(TrayUpdate.Stops(update, viewer)).IsTrue();
        await Assert.That(TrayUpdate.Stops(update, viewer.ToUpperInvariant())).IsTrue();
        await Assert.That(TrayUpdate.Stops(update, Path.Combine(also, "DiffEngineViewer.exe"))).IsTrue();
        await Assert.That(TrayUpdate.Stops(update, Path.Combine(tools, ".store", "diffenginetray.other", "DiffEngineViewer.exe"))).IsFalse();
        await Assert.That(TrayUpdate.Stops(update, Path.Combine(Path.GetTempPath(), ".nuget", "packages", "diffengine", "DiffEngineViewer.exe"))).IsFalse();
        await Assert.That(TrayUpdate.Stops(update, null)).IsFalse();
    }

    /// <summary>
    /// A queue this tray holds itself goes with the tray, which stages it as it exits.
    /// </summary>
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Windows)]
    public async Task AQueueThisTrayHoldsIsNotAskedToClose()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint) listener.LocalEndpoint).Port;
            await Assert.That(TrayUpdate.CloseOwningViewer(StoppingUnder(Environment.ProcessPath!), port)).IsFalse();
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// The process listening on the port is found by the listener table, matched by its image
    /// against what the update stops, sent <c>Quit</c> and waited for. Windows PowerShell stands
    /// in for the viewer, saying the first line it is sent, so the update is told to stop under
    /// PowerShell's folder.
    /// </summary>
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Windows)]
    public async Task AViewerTheUpdateStopsIsAskedToClose()
    {
        using var viewer = StandInViewer(out var port);

        var asked = TrayUpdate.CloseOwningViewer(StoppingUnder(StandInTool.Image), port);

        await Assert.That(asked).IsTrue();
        await Assert.That(viewer.StandardOutput.ReadLine()).IsEqualTo("verb: quit");
        await Assert.That(viewer.WaitForExit(10_000)).IsTrue();
    }

    [Test]
    [RunOn(TUnit.Core.Enums.OS.Windows)]
    public async Task AViewerTheUpdateLeavesAloneIsLeftAlone()
    {
        using var viewer = StandInViewer(out var port);
        try
        {
            var asked = TrayUpdate.CloseOwningViewer(StoppingUnder(Path.Combine(Path.GetTempPath(), "elsewhere", "DiffEngineViewer.exe")), port);

            await Assert.That(asked).IsFalse();
            await Assert.That(viewer.HasExited).IsFalse();
        }
        finally
        {
            viewer.Kill();
        }
    }

    static ToolUpdate StoppingUnder(string image)
    {
        var tools = Path.Combine(Path.GetTempPath(), "TrayUpdateTests", "tools");
        return new("DiffEngineTray", Path.Combine(tools, "DiffEngineTray.exe"), Path.Combine(tools, "outcome.log"))
        {
            AlsoStopUnder = [Path.GetDirectoryName(image)!]
        };
    }

    /// <summary>
    /// Listens on a port of its own, says which, and says the verb line of what a connection sends
    /// before it exits.
    /// </summary>
    static Process StandInViewer(out int port)
    {
        const string script =
            "$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0); " +
            "$listener.Start(); " +
            "[Console]::WriteLine($listener.LocalEndpoint.Port); " +
            "$client = $listener.AcceptTcpClient(); " +
            "$reader = [System.IO.StreamReader]::new($client.GetStream()); " +
            "do { $line = $reader.ReadLine() } until ($line -eq $null -or $line.StartsWith('verb:')); " +
            "[Console]::WriteLine($line)";
        var process = Process.Start(
            new ProcessStartInfo(StandInTool.Image, ["-NoProfile", "-Command", script])
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            })!;
        port = int.Parse(process.StandardOutput.ReadLine()!);
        return process;
    }

    sealed class TempOutcome : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"TrayUpdateTests.{Guid.NewGuid():N}.log");

        public TempOutcome(string? text)
        {
            if (text is not null)
            {
                File.WriteAllText(Path, text);
            }
        }

        public void Dispose() =>
            File.Delete(Path);
    }
}
