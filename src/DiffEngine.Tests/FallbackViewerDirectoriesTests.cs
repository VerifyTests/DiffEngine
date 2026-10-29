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
        var windowsTray = FallbackViewerDirectories.WindowsTray().ToList();
        await Assert.That(windowsTray.Count).IsEqualTo(2);
        await Assert.That(windowsTray.All(_ => _.Contains(@"\diffenginetray\"))).IsTrue();
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
    /// folder. Another RID's package beside it is not taken. Every platform's, on every platform,
    /// since the finder splits on either separator.
    /// </summary>
    [Test]
    [Arguments("osx")]
    [Arguments("linux")]
    [Arguments("win")]
    public async Task The_tray_lookup_finds_the_viewer_in_the_store(string os)
    {
        var architecture = BundledViewerDirectory.Architecture()!;
        var rid = $"{os}-{architecture}";
        var viewerName = os == "win" ? "DiffEngineViewer.exe" : "DiffEngineViewer";
        var home = Path.Combine(Path.GetTempPath(), $"DiffEngine.TrayStore.{Guid.NewGuid():N}");
        try
        {
            var version = Path.Combine(home, ".dotnet", "tools", ".store", "diffenginetray", "21.0.0");
            Directory.CreateDirectory(Path.Combine(version, "diffenginetray", "21.0.0", "tools", "any", "any"));
            var viewer = Path.Combine(version, $"diffenginetray.{rid}", "21.0.0", "tools", "net10.0", rid, "viewer", rid);
            Directory.CreateDirectory(viewer);
            File.WriteAllText(Path.Combine(viewer, viewerName), "");
            var other = $"{os}-{(architecture == "arm64" ? "x64" : "arm64")}";
            var otherViewer = Path.Combine(version, $"diffenginetray.{other}", "21.0.0", "tools", "net10.0", other, "viewer", other);
            Directory.CreateDirectory(otherViewer);
            File.WriteAllText(Path.Combine(otherViewer, viewerName), "");

            var found = WildcardFileFinder.FindAll(TrayPattern(os, home) + viewerName).ToList();

            await Assert.That(found.Count).IsEqualTo(1);
            await Assert.That(Path.GetDirectoryName(found[0])).IsEqualTo(viewer);
        }
        finally
        {
            Directory.Delete(home, true);
        }
    }

    /// <summary>
    /// A Windows tray installed up to 20.8.2 was one package for both architectures, with a viewer
    /// for each under <c>any</c>, and stays that way until it is updated.
    /// </summary>
    [Test]
    public async Task A_windows_tray_from_before_the_rid_packages_is_still_found()
    {
        var rid = $"win-{BundledViewerDirectory.Architecture()!}";
        var home = Path.Combine(Path.GetTempPath(), $"DiffEngine.TrayStore.{Guid.NewGuid():N}");
        try
        {
            var tool = Path.Combine(home, ".dotnet", "tools", ".store", "diffenginetray", "20.8.2", "diffenginetray", "20.8.2", "tools", "net10.0", "any");
            var viewer = Path.Combine(tool, "viewer", rid);
            Directory.CreateDirectory(viewer);
            File.WriteAllText(Path.Combine(viewer, "DiffEngineViewer.exe"), "");

            var lookups = FallbackViewerDirectories.WindowsTray()
                .Select(_ => WildcardFileFinder.FindAll(_.Replace("%USERPROFILE%", home) + "DiffEngineViewer.exe").ToList())
                .ToList();

            await Assert.That(lookups[0]).IsEmpty();
            await Assert.That(lookups[1].Count).IsEqualTo(1);
            await Assert.That(Path.GetDirectoryName(lookups[1][0])).IsEqualTo(viewer);
        }
        finally
        {
            Directory.Delete(home, true);
        }
    }

    static string TrayPattern(string os, string home) =>
        os switch
        {
            "osx" => FallbackViewerDirectories.OsxTray().Single().Replace("%HOME%", home),
            "linux" => FallbackViewerDirectories.LinuxTray().Single().Replace("%HOME%", home),
            _ => FallbackViewerDirectories.WindowsTray().First().Replace("%USERPROFILE%", home)
        };
}
