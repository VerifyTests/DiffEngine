using System.IO;
using Serilog;

static class Logging
{
    public static void Init()
    {
        var loggerConfiguration = new LoggerConfiguration();
        loggerConfiguration.MinimumLevel.Debug();
        loggerConfiguration.WriteTo.File(
            Path.Combine(AssemblyLocation.CurrentDirectory, "log.txt"),
            rollOnFileSizeLimit: true,
            fileSizeLimitBytes: 1000000, //1mb
            retainedFileCountLimit: 10);
        Log.Logger = loggerConfiguration
            .CreateLogger();
    }
}