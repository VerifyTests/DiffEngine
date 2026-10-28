namespace DiffEngine;

[Obsolete("Use DiffRunner")]
public static class DiffEngineTray
{
    // TrayDetector catches what the mutex can throw, which uncaught in a static constructor is far
    // worse than a wrong answer: the type never initialises, so every later DiffRunner.Launch and
    // AddDelete in the process throws TypeInitializationException
    static DiffEngineTray() =>
        IsRunning = TrayDetector.IsRunning(PiperClient.Port);

    public static bool IsRunning { get; internal set; }

    // No IsRunning gate any more. PendingFiles is the router: the piper port when a tray is
    // running, and the inline queue's owner when one is not, rather than nothing at all.
    public static void AddDelete(string file) =>
        PendingFiles.AddDelete(file);

    public static void AddMove(
        string tempFile,
        string targetFile,
        string? exe,
        string? arguments,
        bool canKill,
        int? processId) =>
        PendingFiles.AddMove(tempFile, targetFile, exe, arguments, canKill, processId);

    public static Task AddDeleteAsync(string file, Cancel cancel = default) =>
        PendingFiles.AddDeleteAsync(file, cancel);

    public static Task AddMoveAsync(
        string tempFile,
        string targetFile,
        string? exe,
        string? arguments,
        bool canKill,
        int? processId,
        Cancel cancel = default) =>
        PendingFiles.AddMoveAsync(tempFile, targetFile, exe, arguments, canKill, processId, cancel);
}
