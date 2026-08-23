/// <summary>
/// Skips a test on Windows, with the reason given at the use site. For the cases whose subject is
/// a Unix file property - a symlink that can be made without elevation, a permission bit - which
/// Windows either does not have or reports differently.
/// </summary>
public sealed class SkipOnWindowsAttribute(string reason) : SkipAttribute(reason)
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(OperatingSystem.IsWindows());
}
