/// <summary>
/// Pixel snapshots are opt in because their baselines are pinned to the CI images that produced
/// them: Linux under Xvfb with Mesa llvmpipe, and the pinned macos-14 runner. A developer machine
/// renders correctly but will not match them, and on Linux there may be no GL context at all.
/// <para>
/// The WinForms head does not use this. Its baselines reproduce off CI, the same way
/// DiffEngineTray's do.
/// </para>
/// </summary>
public sealed class PixelTestAttribute() : SkipAttribute($"Set {Variable}=true to run pixel snapshots.")
{
    public const string Variable = "DIFFENGINE_VIEWER_PIXEL_TESTS";

    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(Environment.GetEnvironmentVariable(Variable) != "true");
}
