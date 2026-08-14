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
