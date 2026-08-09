/// <summary>
/// Snapshots of what actually ships, so an accidental addition or removal shows up as a reviewable
/// diff rather than as a surprise on nuget.org. Package content is assembled by MSBuild from
/// several unrelated mechanisms, and nothing else in the build asserts the result.
/// <para>
/// The failure mode these were written for is stale build output. <c>PackAsTool</c> packages the
/// publish directory wholesale, and MSBuild's incremental copy never removes a file that stopped
/// being produced, so anything a discarded experiment once left in <c>bin</c> keeps shipping. CI
/// builds from a fresh checkout and never sees it; a maintainer packing locally does.
/// </para>
/// <para>
/// Windows only, by way of the solution file: <c>Release-NotWindows</c> drops DiffEngineTray, so
/// its package would be absent and these baselines would not describe a full release. That is also
/// why <c>publish-nuget.yml</c> runs on <c>windows-latest</c>.
/// </para>
/// <para>
/// One caveat: <c>nugets</c> is never cleaned, so a Release build followed by unrelated Debug work
/// leaves these asserting against the last packages that were actually produced.
/// </para>
/// </summary>
public class PackageTests
{
    const string bundled = "tools/viewer/";
    const string runtimeConfig = ".runtimeconfig.json";

    /// <summary>
    /// Guards the set itself. Without this, a package that stopped being produced would take its
    /// content assertions with it and everything would still pass.
    /// </summary>
    [Test]
    [PackageTest]
    public Task Produced() =>
        Verify(string.Join('\n', Packages.Produced()));

    [Test]
    [PackageTest]
    [Arguments("DiffEngine")]
    [Arguments("DiffEngineTray")]
    [Arguments("DiffEngineViewer")]
    public async Task Contents(string id)
    {
        using var archive = Packages.Open(id);
        await Verify(string.Join('\n', Packages.Entries(archive)))
            .UseFileName($"Package.{id}");
    }

    /// <summary>
    /// A runtime config with no assembly beside it is an apphost that cannot start. Cheap to check
    /// and it names the problem, where the content snapshot only records it.
    /// </summary>
    [Test]
    [PackageTest]
    [Arguments("DiffEngine")]
    [Arguments("DiffEngineTray")]
    [Arguments("DiffEngineViewer")]
    public async Task EveryApphostHasItsAssembly(string id)
    {
        using var archive = Packages.Open(id);
        var paths = Packages.Entries(archive).ToHashSet(StringComparer.Ordinal);
        var orphaned = paths
            .Where(_ => _.EndsWith(runtimeConfig, StringComparison.Ordinal))
            .Select(_ => $"{_[..^runtimeConfig.Length]}.dll")
            .Where(_ => !paths.Contains(_))
            .Order(StringComparer.Ordinal)
            .ToList();

        await Assert.That(orphaned).IsEmpty();
    }

    /// <summary>
    /// The tray resolves a viewer through <c>DiffTools</c> like any other tool rather than shipping
    /// one, so nothing named after it belongs in its package.
    /// </summary>
    [Test]
    [PackageTest]
    public async Task TheTrayShipsNoViewer()
    {
        using var archive = Packages.Open("DiffEngineTray");
        var viewerFiles = Packages.Entries(archive)
            .Where(_ => _.Contains("DiffEngineViewer", StringComparison.Ordinal) ||
                        _.Contains("diffengine_viewer", StringComparison.Ordinal))
            .ToList();

        await Assert.That(viewerFiles).IsEmpty();
    }

    /// <summary>
    /// A bundled head is only worth carrying if it can start, which takes the apphost, the managed
    /// assembly, both config files, DiffPlex and the one native renderer for that RID.
    /// </summary>
    [Test]
    [PackageTest]
    public async Task EveryBundledViewerIsComplete()
    {
        using var archive = Packages.Open("DiffEngine");
        var rids = Packages.Entries(archive)
            .Where(_ => _.StartsWith(bundled, StringComparison.Ordinal))
            .GroupBy(_ => _[bundled.Length..].Split('/')[0])
            .ToList();

        // Otherwise a package that bundled nothing at all would pass vacuously.
        await Assert.That(rids).IsNotEmpty();

        var problems = new List<string>();
        foreach (var rid in rids)
        {
            var names = rid
                .Select(_ => _[(bundled.Length + rid.Key.Length + 1)..])
                .ToList();

            foreach (var required in (string[])
                     [
                         "DiffEngineViewer.dll",
                         "DiffEngineViewer.deps.json",
                         $"DiffEngineViewer{runtimeConfig}",
                         "DiffPlex.dll"
                     ])
            {
                if (!names.Contains(required))
                {
                    problems.Add($"{rid.Key} has no {required}");
                }
            }

            // Extensionless off Windows, where NuGet would otherwise read the apphost as a folder.
            if (!names.Any(_ => _ is "DiffEngineViewer" or "DiffEngineViewer.exe"))
            {
                problems.Add($"{rid.Key} has no apphost");
            }

            var natives = names.Count(_ => _.StartsWith("runtimes/", StringComparison.Ordinal));
            if (natives != 1)
            {
                problems.Add($"{rid.Key} has {natives} native renderers");
            }
        }

        await Assert.That(problems).IsEmpty();
    }
}
