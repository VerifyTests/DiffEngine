/// <summary>
/// Pixel snapshots need a GL context, so they are opt in. CI runs them on Linux under Xvfb with
/// Mesa llvmpipe, a pure software rasteriser and therefore more reproducible than any GPU driver.
/// Windows and macOS developers are never blocked by a missing context.
/// </summary>
public sealed class PixelTestAttribute() : SkipAttribute($"Set {Variable}=true to run pixel snapshots.")
{
    public const string Variable = "DIFFENGINE_VIEWER_PIXEL_TESTS";

    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(Environment.GetEnvironmentVariable(Variable) != "true");
}
