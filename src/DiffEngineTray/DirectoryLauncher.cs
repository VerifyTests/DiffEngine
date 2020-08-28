using System;
using System.Diagnostics;
using Serilog;

static class DirectoryLauncher
{
    public static void Open(string directory)
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
}