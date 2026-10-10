/// <summary>
/// Skips a test that needs a real WSL distribution around it, which is everywhere but the
/// <c>wsl</c> job in CI and a developer who works inside one.
/// <para>
/// Asked of the variable WSL sets and not of <see cref="WslInterop.Host" />. A distribution in
/// which the host cannot be read is exactly what these tests are there to report, and skipping
/// on that would report it as nothing having been asked.
/// </para>
/// </summary>
public sealed class RequiresWslAttribute() :
    SkipAttribute("Not running inside a WSL distribution.")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(!BuildServerDetector.IsWsl);
}
