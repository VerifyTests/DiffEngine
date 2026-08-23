/// <summary>
/// Skips a test on Windows, with the reason given at the use site. For the cases whose failure
/// mode is a Unix file permission, which Windows reports as something else or not at all.
/// </summary>
public sealed class SkipOnWindowsAttribute(string reason) : SkipAttribute(reason)
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(OperatingSystem.IsWindows());
}
