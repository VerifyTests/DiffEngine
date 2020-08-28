using System;
using System.Diagnostics;
using Serilog;

static class ExplorerLauncher
{
    public static void OpenDirectory(string directory)
    {
        var info = new ProcessStartInfo
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
            Log.Error(exception, $"Failed to open directory: {directory}");
        }
    }

    public static void OpenFile(string file)
    {
        var info = new ProcessStartInfo
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
            Log.Error(exception, $"Failed to open file: {file}");
        }
    }
}