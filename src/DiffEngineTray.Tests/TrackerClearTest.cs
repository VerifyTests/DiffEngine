public class TrackerClearTest :
    IDisposable
{
    [Fact]
    public async Task Simple()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AddMove(file2, file2, "theExe", "theArguments", true, null);
        tracker.Clear();
        tracker.AssertEmpty();
    }

    public void Dispose()
    {
        File.Delete(file1);
        File.Delete(file2);
    }

    string file1 = Path.GetTempFileName();
    string file2 = Path.GetTempFileName();
}
