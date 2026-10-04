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

    /// <summary>
    /// PackAsTool packs the publish directory wholesale, so the tray's copy sits under its tool
    /// payload rather than at a path of its own choosing.
    /// </summary>
    const string trayBundled = "tools/net10.0/any/viewer/";

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
    [Arguments("DiffEngineViewer.Windows")]
    [Arguments("DiffEngineViewer.Mac")]
    [Arguments("DiffEngineViewer.Linux")]
    public async Task Contents(string id)
    {
        await using var archive = Packages.Open(id);
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
    [Arguments("DiffEngineViewer.Windows")]
    [Arguments("DiffEngineViewer.Mac")]
    [Arguments("DiffEngineViewer.Linux")]
    public async Task EveryApphostHasItsAssembly(string id)
    {
        await using var archive = Packages.Open(id);
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
    /// The SBOM is dropped from the content snapshots, since it only exists on CI, so its absence
    /// there would otherwise go unnoticed. This is the half of that which can still be checked.
    /// </summary>
    [Test]
    [PackageTest]
    public async Task TheSbomIsGeneratedOnCi()
    {
        if (!Packages.OnCi())
        {
            return;
        }

        await using var archive = Packages.Open("DiffEngine");
        await Assert.That(Packages.HasSbom(archive)).IsTrue();
    }

    /// <summary>
    /// The tray can own the inline queue, so it has to be able to open a window on one, and the
    /// copy inside DiffEngine.nupkg is only reachable from a project that references that package.
    /// <para>
    /// This assertion used to be the reverse. The tray once shipped 13 MB of broken viewer payload
    /// through a solution level build dependency nobody intended; what it carries now is a
    /// deliberate few hundred KB, and <see cref="EveryBundledViewerIsComplete"/> is what tells the
    /// difference.
    /// </para>
    /// </summary>
    [Test]
    [PackageTest]
    public async Task TheTrayShipsAViewer()
    {
        await using var archive = Packages.Open("DiffEngineTray");
        var stray = Packages.Entries(archive)
            .Where(_ => _.Contains("DiffEngineViewer", StringComparison.Ordinal) ||
                        _.Contains("diffengine_viewer", StringComparison.Ordinal))
            .Where(_ => !_.StartsWith(trayBundled, StringComparison.Ordinal))
            .ToList();

        // Under tools/net10.0/any/viewer and nowhere else. Loose beside the tray's own assemblies
        // is how the leak looked.
        await Assert.That(stray).IsEmpty();
        await Assert.That(Rids(archive, trayBundled)).IsNotEmpty();
    }

    /// <summary>
    /// A bundled head is only worth carrying if it can start, which takes the apphost, the managed
    /// assembly, both config files and the one native renderer for that RID.
    /// </summary>
    [Test]
    [PackageTest]
    [Arguments("DiffEngine", bundled)]
    [Arguments("DiffEngineTray", trayBundled)]
    public async Task EveryBundledViewerIsComplete(string id, string root)
    {
        await using var archive = Packages.Open(id);
        var rids = Rids(archive, root);

        // Otherwise a package that bundled nothing at all would pass vacuously.
        await Assert.That(rids).IsNotEmpty();

        var problems = new List<string>();
        foreach (var rid in rids)
        {
            var names = rid
                .Select(_ => _[(root.Length + rid.Key.Length + 1)..])
                .ToList();

            foreach (var required in (string[])
                     [
                         "DiffEngineViewer.dll",
                         "DiffEngineViewer.Core.dll",
                         "DiffEngineViewer.deps.json",
                         $"DiffEngineViewer{runtimeConfig}"
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

            // Windows renders with WinForms, so a native renderer there is a leftover rather than
            // a payload. Everywhere else exactly one, for this RID and no other.
            var expected = rid.Key.StartsWith("win-", StringComparison.Ordinal) ? 0 : 1;
            var natives = names.Count(_ => _.StartsWith("runtimes/", StringComparison.Ordinal));
            if (natives != expected)
            {
                problems.Add($"{rid.Key} has {natives} native renderers, expected {expected}");
            }
        }

        await Assert.That(problems).IsEmpty();
    }

    /// <summary>
    /// The documents folder is what makes the dotnet tool and the tray's viewer read PDFs and Office
    /// files, and is tens of MB per RID. The copy DiffEngine bundles must stay without it, which is
    /// what keeps the DiffEngine package the size it was.
    /// </summary>
    [Test]
    [PackageTest]
    public async Task TheBundleCarriesNoDocuments()
    {
        await using var archive = Packages.Open("DiffEngine");
        var documents = Packages.Entries(archive)
            .Where(_ => _.Contains("/documents/", StringComparison.Ordinal) ||
                        _.Contains("DiffEngineViewer.Documents", StringComparison.Ordinal))
            .ToList();

        await Assert.That(documents).IsEmpty();
    }

    /// <summary>
    /// A documents folder loads only when its deps.json and its files agree for the running RID:
    /// AssemblyDependencyResolver refuses one naming an asset that is not there. So every native
    /// it names for a RID the package ships has to be in the package, and nothing for any other RID
    /// should be. And no .pdb, which SkiaSharp puts beside its Windows natives at 120 MB a RID.
    /// </summary>
    [Test]
    [PackageTest]
    [Arguments("DiffEngineViewer.Windows", tool, "win-x64;win-arm64")]
    [Arguments("DiffEngineViewer.Mac", tool, "osx;osx-x64;osx-arm64")]
    [Arguments("DiffEngineViewer.Linux", tool, "linux-x64;linux-arm64")]
    [Arguments("DiffEngineTray", trayDocuments, "win-x64;win-arm64")]
    public async Task EveryDocumentsFolderIsComplete(string id, string root, string rids)
    {
        await using var archive = Packages.Open(id);
        var paths = Packages.Entries(archive)
            .Where(_ => _.StartsWith(root, StringComparison.Ordinal))
            .Select(_ => _[root.Length..])
            .ToHashSet(StringComparer.Ordinal);
        var shipped = rids.Split(';').ToHashSet(StringComparer.Ordinal);

        var problems = new List<string>();
        foreach (var required in (string[]) ["DiffEngineViewer.Documents.dll", depsJson])
        {
            if (!paths.Contains(required))
            {
                problems.Add($"no {required}");
            }
        }

        problems.AddRange(paths
            .Where(_ => _.EndsWith(".pdb", StringComparison.Ordinal))
            .Select(_ => $"a pdb: {_}"));

        var folders = paths
            .Where(_ => _.StartsWith("runtimes/", StringComparison.Ordinal))
            .Select(_ => _.Split('/')[1])
            .ToHashSet(StringComparer.Ordinal);
        if (!folders.SetEquals(shipped))
        {
            problems.Add($"natives for {string.Join(", ", folders.Order(StringComparer.Ordinal))}, expected {rids}");
        }

        if (paths.Contains(depsJson))
        {
            await using var stream = await archive.GetEntry(root + depsJson)!.OpenAsync();
            var document = await JsonDocument.ParseAsync(stream);
            foreach (var library in document.RootElement.GetProperty("targets").EnumerateObject().SelectMany(_ => _.Value.EnumerateObject()))
            {
                if (!library.Value.TryGetProperty("runtimeTargets", out var targets))
                {
                    continue;
                }

                foreach (var target in targets.EnumerateObject())
                {
                    if (shipped.Contains(target.Value.GetProperty("rid").GetString()!) &&
                        !paths.Contains(target.Name))
                    {
                        problems.Add($"deps.json names {target.Name}, which is not in the package");
                    }
                }
            }
        }

        await Assert.That(problems).IsEmpty();
    }

    const string tool = "tools/net10.0/any/documents/";

    /// <summary>
    /// One folder beside the tray's two RID copies of the viewer, which each find one directory up.
    /// </summary>
    const string trayDocuments = "tools/net10.0/any/viewer/documents/";

    const string depsJson = "DiffEngineViewer.Documents.deps.json";

    /// <summary>
    /// The viewer copies under <paramref name="root"/>, by RID. Not the tray's documents folder,
    /// which sits beside them and is not a RID.
    /// </summary>
    static List<IGrouping<string, string>> Rids(ZipArchive archive, string root) =>
        Packages.Entries(archive)
            .Where(_ => _.StartsWith(root, StringComparison.Ordinal))
            .GroupBy(_ => _[root.Length..].Split('/')[0])
            .Where(_ => _.Key != "documents")
            .ToList();
}
