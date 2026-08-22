namespace DiffEngine;

[Obsolete("Use DiffRunner")]
public static class DiffEngineTray
{
    static DiffEngineTray()
    {
        try
        {
            if (Mutex.TryOpenExisting("DiffEngine", out var mutex))
            {
                IsRunning = true;
                mutex.Dispose();
            }
        }
        //net7 on mac throws an exception if the mutex does not exist
        catch (IOException)
        {
        }
        // A mutex that exists but is not accessible to this caller, which Mutex.TryOpenExisting
        // documents: the tray under one account and the tests under another. Uncaught in a static
        // constructor it is far worse than a wrong answer - the type never initialises, so every
        // later DiffRunner.Launch and AddDelete in the process throws TypeInitializationException
        catch (UnauthorizedAccessException)
        {
        }
    }

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
