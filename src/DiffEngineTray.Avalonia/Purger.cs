namespace DiffEngineTray;

// Avalonia equivalent of the Windows FilePurger: pick a folder, then delete *.verified.* / *.received.* files.
static class Purger
{
    public static void Run() =>
        _ = RunAsync();

    static async Task RunAsync()
    {
        try
        {
            var folder = await Dialogs.PickFolder("Select a folder to purge verified/received files");
            if (folder == null)
            {
                return;
            }

            var verifiedFiles = Directory.GetFiles(folder, "*.verified.*", SearchOption.AllDirectories);
            var receivedFiles = Directory.GetFiles(folder, "*.received.*", SearchOption.AllDirectories);
            var files = verifiedFiles
                .Concat(receivedFiles)
                .ToArray();

            if (files.Length == 0)
            {
                await Dialogs.Message($"No *.verified.* or *.received.* files found in {folder}");
                return;
            }

            if (await Dialogs.ConfirmAsync(
                    $"""
                     Files found: {files.Length}.
                     Delete files?
                     """))
            {
                DeleteFiles(files);
            }
        }
        catch (Exception exception)
        {
            ExceptionHandler.Handle("Failed to purge files", exception);
        }
    }

    static void DeleteFiles(string[] files)
    {
        foreach (var file in files)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (Exception exception)
            {
                if (FileLockKiller.KillLockingProcesses(file))
                {
                    try
                    {
                        File.Delete(file);
                        continue;
                    }
                    catch
                    {
                        // fall through to log the original failure
                    }
                }

                Log.Error(exception, "Failed to delete '{File}'.", file);
            }
        }
    }
}
