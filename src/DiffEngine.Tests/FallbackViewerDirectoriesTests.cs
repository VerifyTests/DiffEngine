/// <summary>
/// Where the viewer is looked for once the bundled copy and the global tool have both missed.
/// </summary>
public class FallbackViewerDirectoriesTests
{
    [Test]
    public async Task Library_version_is_the_cache_folder_name()
    {
        var version = FallbackViewerDirectories.LibraryVersion();

        await Assert.That(version).IsNotNull();
        await Assert.That(version!).DoesNotContain("+");
        await Assert.That(version).IsEqualTo(version.ToLowerInvariant());
    }

    [Test]
    public async Task This_version_then_any_version()
    {
        var directories = FallbackViewerDirectories.Windows().ToList();
        var version = FallbackViewerDirectories.LibraryVersion()!;

        var matched = directories.FindIndex(_ => _.Contains($@"\diffengine\{version}\"));
        var any = directories.FindIndex(_ => _.Contains(@"\diffengine\*\"));

        await Assert.That(matched).IsGreaterThanOrEqualTo(0);
        await Assert.That(any).IsGreaterThan(matched);
    }

    [Test]
    public async Task Only_the_tray_lookup_names_the_tray()
    {
        await Assert.That(FallbackViewerDirectories.Tray().Single()).Contains(@"\diffenginetray\");
        await Assert.That(FallbackViewerDirectories.Windows().Any(_ => _.Contains("diffenginetray"))).IsFalse();
        await Assert.That(FallbackViewerDirectories.Osx().Any(_ => _.Contains("diffenginetray"))).IsFalse();
        await Assert.That(FallbackViewerDirectories.Linux().Any(_ => _.Contains("diffenginetray"))).IsFalse();
    }
}
