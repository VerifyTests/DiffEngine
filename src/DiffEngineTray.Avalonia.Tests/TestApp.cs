// Headless Avalonia application used to render controls for Verify.Avalonia snapshots.
public class TestApp :
    Application
{
    public override void Initialize() =>
        Styles.Add(new FluentTheme());

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new() { UseHeadlessDrawing = false });
}
