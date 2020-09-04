using System.IO;
using Serilog;

static class Logging
{
    public static string Directory { get; } = Path.Combine(AssemblyLocation.CurrentDirectory, "logs");

    public static void Init()
    {
        System.IO.Directory.CreateDirectory(Directory);
        var configuration = new LoggerConfiguration();
        configuration.MinimumLevel.Debug();
        configuration.WriteTo.File(
            Path.Combine(Directory, "log.txt"),
            rollOnFileSizeLimit: true,
            fileSizeLimitBytes: 1000000, //1mb
            retainedFileCountLimit: 10);
        Log.Logger = configuration.CreateLogger();
    }

    public static void OpenDirectory()
    {
        ExplorerLauncher.OpenDirectory(Directory);
    }
}