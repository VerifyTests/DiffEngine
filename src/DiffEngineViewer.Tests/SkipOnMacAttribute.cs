/// <summary>
/// Skips a test on macOS, with the reason given at the use site.
/// <para>
/// Both uses so far come down to <c>deview_capture</c> making no window on macOS: what that head
/// shows is drawn by AppKit into its window, and it waits for the next frame in that window's
/// event pump.
/// </para>
/// </summary>
public sealed class SkipOnMacAttribute(string reason) : SkipAttribute(reason)
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(OperatingSystem.IsMacOS());
}
