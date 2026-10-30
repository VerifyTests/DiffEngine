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
