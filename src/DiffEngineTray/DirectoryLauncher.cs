using System;
using System.Diagnostics;
using Serilog;

static class ExplorerLauncher
{
    public static void OpenDirectory(string directory)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true,
                Verb = "open"
            };
            using (Process.Start(info))
            {
            }
        }
        catch (Exception exception)
        {
            Log.Logger.Error(exception, "Failed to open directory: " + directory);
        }
    }

    public static void OpenFile(string file)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select, \"{file}\"",
            };
            using (Process.Start(info))
            {
            }
        }
        catch (Exception exception)
        {
            Log.Logger.Error(exception, "Failed to open file: " + file);
        }
    }
}