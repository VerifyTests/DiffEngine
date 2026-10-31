/// <summary>
/// "Purge verified files" for a head with no toolkit: the folder and the go ahead asked through a
/// <see cref="DesktopDialog" />, and what came of it said through the head's notifications. What
/// it finds and deletes is the Windows head's (<see cref="VerifiedFiles" />). A file that cannot
/// be deleted is passed over and counted, where the Windows head asks whether to try it again: a
/// Unix lock keeps nothing out, so what fails here is a permission, which asking again does not
/// change.
/// </summary>
static class DesktopPurge
{
    /// <summary>
    /// The menu's action, or null where there is nothing to ask with. It returns at once: the
    /// purge runs on a thread of its own, because each question waits on the person and the
    /// menu's thread is the one the tray's loop turns on, which on macOS is AppKit's.
    /// </summary>
    public static Action? For(DesktopDialog? dialog, Action<string> tell)
    {
        if (dialog is null)
        {
            return null;
        }

        var running = 0;
        return () =>
        {
            // One at a time: a second chosen while the first waits on its dialog would put up another
            if (Interlocked.Exchange(ref running, 1) == 1)
            {
                return;
            }

            var thread = new Thread(() =>
            {
                try
                {
                    Run(dialog.PickFolder, dialog.Confirm, tell);
                }
                catch (Exception exception)
                {
                    // Nothing above this catches, and an exception on a bare Thread ends the
                    // process, which is the tray and everything it holds
                    Log.Error(exception, "Failed to purge verified files");
                    tell($"Failed to purge verified files. {exception.Message}");
                }
                finally
                {
                    Volatile.Write(ref running, 0);
                }
            })
            {
                IsBackground = true,
                Name = "Purge verified files"
            };
            thread.Start();
        };
    }

    internal static void Run(Func<string?> pickFolder, Func<string, bool> confirm, Action<string> tell)
    {
        if (pickFolder() is not { } path)
        {
            return;
        }

        var files = VerifiedFiles.Find(path);
        if (files.Length == 0)
        {
            tell($"No *.verified.* or *.received.* files found in {path}");
            return;
        }

        if (!confirm(
                $"""
                 Files found: {files.Length}, in {path}.
                 Delete files?
                 """))
        {
            return;
        }

        var failed = 0;
        string? firstFailure = null;
        foreach (var file in files)
        {
            var result = VerifiedFiles.TryDelete(file);
            if (result.Deleted)
            {
                continue;
            }

            failed++;
            firstFailure ??= $"{file}: {result.Exception!.Message}";
        }

        if (failed == 0)
        {
            tell($"Deleted {files.Length} files from {path}.");
            return;
        }

        tell($"Deleted {files.Length - failed} of {files.Length} files from {path}. {failed} could not be deleted, the first being {firstFailure}");
    }
}
