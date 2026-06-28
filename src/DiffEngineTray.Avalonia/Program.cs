namespace DiffEngineTray;

static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Logging.Init();

        var mutex = SingleInstance.TryAcquire();
        if (mutex == null)
        {
            Log.Information("Another DiffEngineTray instance is already running. Exiting.");
            return 0;
        }

        try
        {
            AvaloniaTrayServices.Wire();
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Failed at startup");
            throw;
        }
        finally
        {
            mutex.Dispose();
        }
    }

    // Referenced by the Avalonia tooling and the headless test session.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            // Run as a menu-bar agent on macOS (no Dock icon). Ignored on other platforms.
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .LogToTrace();
}
