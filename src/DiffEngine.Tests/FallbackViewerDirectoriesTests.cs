/// <summary>
/// Where the viewer is looked for once the bundled copy and the global tool have both missed.
/// </summary>
public class FallbackViewerDirectoriesTests
{
    [Test]
    public async Task Library_version_is_the_cache_folder_name()
    {
        var version = FallbackViewerDirectories.LibraryVersion()!;

        await Assert.That(version).IsNotNull();
        await Assert.That(version).DoesNotContain("+");
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
    public async Task Only_the_tray_lookups_name_the_tray()
    {
        await Assert.That(FallbackViewerDirectories.WindowsTray().Single()).Contains(@"\diffenginetray\");
        await Assert.That(FallbackViewerDirectories.OsxTray().Single()).Contains("/diffenginetray/");
        await Assert.That(FallbackViewerDirectories.LinuxTray().Single()).Contains("/diffenginetray/");
        await Assert.That(FallbackViewerDirectories.Windows().Any(_ => _.Contains("diffenginetray"))).IsFalse();
        await Assert.That(FallbackViewerDirectories.Osx().Any(_ => _.Contains("diffenginetray"))).IsFalse();
        await Assert.That(FallbackViewerDirectories.Linux().Any(_ => _.Contains("diffenginetray"))).IsFalse();
    }

    /// <summary>
    /// A store laid out as .NET 10's `dotnet tool install` lays out a RID specific tool, with the
    /// viewer where the tray's package for that RID carries it: the package that was asked for,
    /// which only names one per RID, and beside it the one for this RID, its tool in the RID's
    /// folder. Another RID's package beside it is not taken.
    /// </summary>
    [Test]
    [Arguments("osx")]
    [Arguments("linux")]
    public async Task The_unix_tray_lookup_finds_the_viewer_in_the_store(string os)
    {
        var architecture = BundledViewerDirectory.Architecture()!;
        var rid = $"{os}-{architecture}";
        var home = Path.Combine(Path.GetTempPath(), $"DiffEngine.TrayStore.{Guid.NewGuid():N}");
        try
        {
            var version = Path.Combine(home, ".dotnet", "tools", ".store", "diffenginetray", "21.0.0");
            Directory.CreateDirectory(Path.Combine(version, "diffenginetray", "21.0.0", "tools", "any", "any"));
            var viewer = Path.Combine(version, $"diffenginetray.{rid}", "21.0.0", "tools", "net10.0", rid, "viewer", rid);
            Directory.CreateDirectory(viewer);
            File.WriteAllText(Path.Combine(viewer, "DiffEngineViewer"), "");
            var other = $"{os}-{(architecture == "arm64" ? "x64" : "arm64")}";
            var otherViewer = Path.Combine(version, $"diffenginetray.{other}", "21.0.0", "tools", "net10.0", other, "viewer", other);
            Directory.CreateDirectory(otherViewer);
            File.WriteAllText(Path.Combine(otherViewer, "DiffEngineViewer"), "");

            var lookup = os == "osx" ? FallbackViewerDirectories.OsxTray() : FallbackViewerDirectories.LinuxTray();
            var pattern = lookup.Single().Replace("%HOME%", home) + "DiffEngineViewer";

            var found = WildcardFileFinder.FindAll(pattern).ToList();

            await Assert.That(found.Count).IsEqualTo(1);
            await Assert.That(Path.GetDirectoryName(found[0])).IsEqualTo(viewer);
        }
        finally
        {
            Directory.Delete(home, true);
        }
    }
}
