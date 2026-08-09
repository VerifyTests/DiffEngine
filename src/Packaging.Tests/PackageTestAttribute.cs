/// <summary>
/// Package assertions read the <c>.nupkg</c> files a Release build drops in <c>nugets</c>. A Debug
/// build packs nothing, so they skip rather than failing on an ordinary inner loop.
/// </summary>
public sealed class PackageTestAttribute() : SkipAttribute("Only a Release build produces packages.")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(Packages.Produced().Count == 0);
}
