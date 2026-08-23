extern alias engine;

using EngineRunner = engine::DiffEngine.DiffRunner;
using EngineTray = engine::DiffEngine.DiffEngineTray;
using EngineViewerClient = engine::DiffEngine.ViewerClient;

/// <summary>
/// Points DiffEngine at a real viewer on an ephemeral port and restores every piece of global
/// state it touches.
/// </summary>
sealed class EngineScope : IDisposable
{
    readonly string? previousPort;
    readonly string? previousOptOut;
    readonly bool previousDisabled;
    readonly bool previousTray;

    public EngineScope(bool disabled = false, bool optOut = false, bool tray = false)
    {
        Fixture = new();
        previousPort = Environment.GetEnvironmentVariable(EngineViewerClient.PortVariable);
        previousOptOut = Environment.GetEnvironmentVariable(EngineRunner.InlineViewerVariable);
        previousDisabled = EngineRunner.Disabled;
        previousTray = EngineTray.IsRunning;

        Environment.SetEnvironmentVariable(EngineViewerClient.PortVariable, Fixture.Server.Port.ToString());
        Environment.SetEnvironmentVariable(EngineRunner.InlineViewerVariable, optOut ? "false" : null);
        // Off by default in this process, because an AI CLI counts as disabled.
        EngineRunner.Disabled = disabled;
        // Stated rather than detected: the answer is cached at type initialisation from a mutex
        // this process does not control, so a developer with a tray in their notification area
        // would otherwise route pending files down the piper port and read as a failure here.
        EngineTray.IsRunning = tray;
    }

    public ServerFixture Fixture { get; }

    public void Dispose()
    {
        Fixture.Dispose();
        EngineRunner.Disabled = previousDisabled;
        EngineTray.IsRunning = previousTray;
        Environment.SetEnvironmentVariable(EngineViewerClient.PortVariable, previousPort);
        Environment.SetEnvironmentVariable(EngineRunner.InlineViewerVariable, previousOptOut);
    }
}
