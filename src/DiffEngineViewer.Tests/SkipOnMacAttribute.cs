/// <summary>
/// Skips a test on macOS, with the reason given at the use site.
/// <para>
/// There is one so far and it is specific: what that head shows is drawn by AppKit rather than by
/// the renderer, and <c>deview_capture</c> makes no window for AppKit to draw into.
/// </para>
/// </summary>
public sealed class SkipOnMacAttribute(string reason) : SkipAttribute(reason)
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(OperatingSystem.IsMacOS());
}
