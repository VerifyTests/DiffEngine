/// <summary>
/// Skips a test on macOS, with the reason given at the use site.
/// <para>
/// Most uses come down to <c>deview_capture</c> making no window on macOS: what that head shows is
/// drawn by AppKit into its window, and it waits for the next frame in that window's event pump.
/// </para>
/// <para>
/// The rest are scenes that pin something each head does in its own way, and so far have a
/// baseline for the Linux head alone. A macOS one has to be taken on the pinned runner the others
/// come from, and the skip goes when it has been.
/// </para>
/// </summary>
public sealed class SkipOnMacAttribute(string reason) : SkipAttribute(reason)
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(OperatingSystem.IsMacOS());
}
