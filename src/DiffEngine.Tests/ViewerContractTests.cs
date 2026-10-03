/// <summary>
/// Which installed copy of the viewer resolves when there is more than one.
/// <para>
/// A globally installed tool and the copy shipped with a tray are looked for ahead of the bundled
/// one, and either can be older than the library about to launch it. A viewer from before 20.5.0
/// exits on the <c>--payload</c> an inline launch names, so with one of those first in the order
/// no inline snapshot reached a viewer, whatever newer copy sat behind it.
/// </para>
/// <para>
/// The stand-ins are an empty file for the executable and, beside it, an assembly whose version
/// is the one read: an executable with no version of its own is every apphost off Windows, so
/// the same arrangement reads the same way on all three platforms.
/// </para>
/// </summary>
public class ViewerContractTests :
    IDisposable
{
    [Test]
    [Arguments("20.5.0+de0e3be5177093d6e915f2fa27970455f29dfc53")]
    [Arguments("20.5")]
    [Arguments("20.6.0")]
    [Arguments("21.0.0-beta.1+0cb4f830cb45e9a87aabfd16928ac27eb0fced94")]
    public async Task ACopyFromTwentyFiveOnMeetsIt(string productVersion) =>
        await Assert.That(ViewerContract.IsMetBy(productVersion)).IsTrue();

    [Test]
    [Arguments("20.4.0+80a966e0b1add0f6f65c16f42db594a7eda972d1")]
    [Arguments("20.3.1+65e8e790c676ae4cf6d98783f35345db2a926e47")]
    [Arguments("20.0.0-beta.30+5c6d11e917702eb170a505647a4ce6cda8576b21")]
    [Arguments("19.3.3")]
    public async Task ACopyFromBeforeItDoesNot(string productVersion) =>
        await Assert.That(ViewerContract.IsMetBy(productVersion)).IsFalse();

    /// <summary>
    /// A copy that does not say what it is is not passed over on a guess.
    /// </summary>
    [Test]
    public async Task ACopyWithNoVersionIsTakenToMeetIt()
    {
        await Assert.That(ViewerContract.IsMetBy(null)).IsTrue();
        await Assert.That(ViewerContract.IsMetBy("")).IsTrue();
        await Assert.That(ViewerContract.IsMetBy("a local build")).IsTrue();
    }

    /// <summary>
    /// The read itself, off an assembly this build produced, so the version is one known here.
    /// </summary>
    [Test]
    public async Task TheVersionIsReadFromTheAssemblyBesideAnExecutableThatHasNone()
    {
        var viewer = Copy("current", typeof(DiffRunner));

        var version = ViewerContract.ProductVersion(viewer);

        await Assert.That(version).IsNotNull();
        await Assert.That(version!).StartsWith(FallbackViewerDirectories.LibraryVersion()!, StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task AnOlderCopyIsPassedOverForANewerOneBehindIt()
    {
        Copy("tray", typeof(AllFiles));
        var bundled = Copy("bundled", typeof(DiffRunner));

        await Assert.That(Resolve("tray", "bundled")).IsEqualTo(bundled);
    }

    /// <summary>
    /// The order is otherwise the one written: a copy that meets the contract is not passed over
    /// for a later one that also does.
    /// </summary>
    [Test]
    public async Task TheFirstCopyThatMeetsItIsTheOneTaken()
    {
        var global = Copy("global", typeof(DiffRunner));
        Copy("bundled", typeof(DiffRunner));

        await Assert.That(Resolve("global", "bundled")).IsEqualTo(global);
    }

    /// <summary>
    /// Passed over, not refused. An older viewer still takes a pair and a delete, which is better
    /// than no viewer, and the launch gate reports the inline launch it cannot take.
    /// </summary>
    [Test]
    public async Task AnOlderCopyIsStillFoundWhenItIsTheOnlyOne()
    {
        var tray = Copy("tray", typeof(AllFiles));

        await Assert.That(Resolve("tray", "bundled")).IsEqualTo(tray);
    }

    /// <summary>
    /// Only the viewer is chosen between this way. Every other tool is started with the two
    /// paths its own definition gives it, and which copy of it is found is none of this
    /// library's business.
    /// </summary>
    [Test]
    public async Task OnlyTheViewerIsChosenBetween()
    {
        await Assert.That(DiffTools.PreferredCopy(DiffTool.DiffEngineViewer)).IsNotNull();
        await Assert.That(DiffTools.PreferredCopy(DiffTool.BeyondCompare)).IsNull();
        await Assert.That(DiffTools.PreferredCopy(null)).IsNull();
    }

    /// <summary>
    /// Through the resolution DiffTools runs for the viewer, over search directories of the test's
    /// own in the order given.
    /// </summary>
    string? Resolve(params string[] directories)
    {
        var launchArguments = new LaunchArguments(
            Left: (temp, target) => $"\"{target}\" \"{temp}\"",
            Right: (temp, target) => $"\"{temp}\" \"{target}\"");
        var settings = new OsSettings(
            viewerName,
            launchArguments,
            directories.Select(_ => Path.Combine(root, _)).ToArray());

        OsSettingsResolver.Resolve(
            "ViewerContractTests",
            new(settings, settings, settings),
            out var path,
            out _,
            DiffTools.PreferredCopy(DiffTool.DiffEngineViewer));
        return path;
    }

    /// <summary>
    /// A copy of the viewer whose version is that of the assembly <paramref name="versioned" />
    /// is in. EmptyFiles stands in for an older one: it is a dependency of the library, so it is
    /// always beside the tests, and it is a long way short of version 20.
    /// </summary>
    string Copy(string directory, Type versioned)
    {
        var full = Path.Combine(root, directory);
        Directory.CreateDirectory(full);
        var viewer = Path.Combine(full, viewerName);
        File.WriteAllText(viewer, "");
        File.Copy(versioned.Assembly.Location, Path.ChangeExtension(viewer, ".dll"));
        return viewer;
    }

    // Not the viewer's own name, which PATH is searched for after the directories: on a machine
    // with the viewer installed as a tool that copy would join the ones under test
    static readonly string viewerName =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ViewerContractStandIn.exe" : "ViewerContractStandIn";

    readonly string root = Path.Combine(Path.GetTempPath(), $"DiffEngine.ViewerContract.{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }
}
