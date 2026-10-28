public class DebugReportFileTests :
    IDisposable
{
    /// <summary>
    /// The report a head with no window opens, which is the one the Windows head shows.
    /// </summary>
    [Test]
    public async Task TheReportIsWrittenWhole()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file);
        var now = new DateTime(2026, 10, 28, 12, 0, 0);
        var path = Path.Combine(directory, "debug.txt");

        DebugReportFile.Write(tracker, path, now);

        await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(DebugReport.Build(tracker, now));
    }

    string directory = Directory.CreateTempSubdirectory("DebugReportFileTests").FullName;
    string file = Path.GetTempFileName();

    public void Dispose()
    {
        File.Delete(file);
        Directory.Delete(directory, true);
    }
}
