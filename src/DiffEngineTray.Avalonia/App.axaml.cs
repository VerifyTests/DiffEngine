namespace DiffEngineTray;

public partial class App :
    Application
{
    TrayCoordinator? coordinator;

    public override void Initialize() =>
        AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // No main window: the app lives in the tray/menu bar until the user explicitly exits.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            coordinator = new(this, desktop);
            coordinator.Start();

            desktop.Exit += (_, _) => coordinator.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
