/// <summary>
/// The menu's "Debug view" for a head with no window to show it in: the report written to a file
/// beside the logs and opened by the desktop, which is where it is copied into an issue from, as
/// the Windows head's form is.
/// </summary>
static class DebugReportFile
{
    public static string FilePath { get; } = Path.Combine(Logging.LogsDirectory, "debug.txt");

    public static void Open(Tracker tracker)
    {
        Write(tracker, FilePath, DateTime.Now);
        DirectoryLauncher.Open(FilePath);
    }

    internal static void Write(Tracker tracker, string path, DateTime now) =>
        File.WriteAllText(path, DebugReport.Build(tracker, now));
}
