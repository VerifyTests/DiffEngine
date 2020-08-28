using System.Diagnostics;
using System.IO;
using Serilog;

static class Logging
{
    static string directory = Path.Combine(AssemblyLocation.CurrentDirectory, "logs");

    public static void Init()
    {
        Directory.CreateDirectory(directory);
        var loggerConfiguration = new LoggerConfiguration();
        loggerConfiguration.MinimumLevel.Debug();
        loggerConfiguration.WriteTo.File(
            Path.Combine(directory, "log.txt"),
            rollOnFileSizeLimit: true,
            fileSizeLimitBytes: 1000000, //1mb
            retainedFileCountLimit: 10);
        Log.Logger = loggerConfiguration
            .CreateLogger();
    }

    public static void OpenDirectory()
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
}