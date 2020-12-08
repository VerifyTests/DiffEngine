using System;
using System.Diagnostics;

static class ExplorerLauncher
{
    public static void OpenDirectory(string directory)
    {
        ProcessStartInfo info = new()
        {
            FileName = directory,
            UseShellExecute = true,
            Verb = "open"
        };
        try
        {
            using (Process.Start(info))
            {
            }
        }
        catch (Exception exception)
        {
            ExceptionHandler.Handle($"Failed to open directory: {directory}", exception);
        }
    }

    public static void ShowFileInExplorer(string file)
    {
        ProcessStartInfo info = new()
        {
            FileName = "explorer.exe",
            Arguments = $"/select, \"{file}\"",
        };
        try
        {
            using (Process.Start(info))
            {
            }
        }
        catch (Exception exception)
        {
            ExceptionHandler.Handle($"Failed to open file: {file}", exception);
        }
    }
}