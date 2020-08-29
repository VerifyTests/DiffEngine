using System.IO;
using Serilog;

static class Logging
{
    public static string Directory { get; } = Path.Combine(AssemblyLocation.CurrentDirectory, "logs");

    public static void Init()
    {
        System.IO.Directory.CreateDirectory(Directory);
        var loggerConfiguration = new LoggerConfiguration();
        loggerConfiguration.MinimumLevel.Debug();
        loggerConfiguration.WriteTo.File(
            Path.Combine(Directory, "log.txt"),
            rollOnFileSizeLimit: true,
            fileSizeLimitBytes: 1000000, //1mb
            retainedFileCountLimit: 10);
        Log.Logger = loggerConfiguration
            .CreateLogger();
    }

    public static void OpenDirectory()
    {
        ExplorerLauncher.OpenDirectory(Directory);
    }
}