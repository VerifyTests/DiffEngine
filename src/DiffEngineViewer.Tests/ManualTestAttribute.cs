/// <summary>
/// Opens a real window and waits for a person to close it, so these are opt in and never run on
/// CI. They cover what no automated test can: that launching actually resolves an executable,
/// hands the payload over, and puts the right thing on screen.
/// <para>
/// Run one at a time, because the viewer is single instance:
/// <code>
/// $env:DIFFENGINE_VIEWER_MANUAL='true'
/// dotnet test --project src/DiffEngineViewer.Tests --filter "FullyQualifiedName~ViewerLaunchTests.InlineQueue"
/// </code>
/// </para>
/// </summary>
public sealed class ManualTestAttribute() : SkipAttribute($"Set {Variable}=true to run the manual viewer checks.")
{
    public const string Variable = "DIFFENGINE_VIEWER_MANUAL";

    public static bool Enabled =>
        Environment.GetEnvironmentVariable(Variable) == "true";

    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(!Enabled);
}
