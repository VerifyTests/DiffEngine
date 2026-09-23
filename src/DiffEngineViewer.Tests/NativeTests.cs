/// <summary>
/// Loads the committed native renderer for whatever RID this is running on and checks its ABI.
/// <para>
/// Headless and cheap, which is the point: it is the only coverage most of the RIDs get. The pixel
/// tests only run on Linux, so without this a binary built for the wrong architecture, corrupted
/// by a text mode checkout, or missing a runtime dependency would ship undetected.
/// </para>
/// <para>
/// Windows is excluded because it has no native renderer to load. That head draws with WinForms,
/// which is covered by DiffEngineViewer.Windows.Tests instead.
/// </para>
/// </summary>
public class NativeTests
{
    static bool HasNativeRenderer =>
        OperatingSystem.IsLinux() ||
        OperatingSystem.IsMacOS();

    [Test]
    public async Task LoadsAndReportsItsAbiVersion()
    {
        if (!HasNativeRenderer ||
            !NativeResolver.TryFind(out var path))
        {
            // No binary is shipped for this RID, for example linux-musl. Resolution falls through
            // to a globally installed tool, so there is nothing to check here.
            return;
        }

        await Assert.That(new FileInfo(path).Length).IsGreaterThan(0);

        // Exercises the real load: wrong architecture, a corrupted file, or an unsatisfied
        // dependency all surface here rather than at a user's first inline snapshot.
        await Assert.That(Deview.Version()).IsEqualTo(Deview.ExpectedVersion);
    }

    [Test]
    public async Task ShipsABinaryForThisPlatform()
    {
        if (!HasNativeRenderer)
        {
            return;
        }

        await Assert.That(NativeResolver.TryFind(out _)).IsTrue();
    }

    /// <summary>
    /// The probe for a musl RID is that RID alone. The linux-{arch} candidate after it names the
    /// glibc build, which a musl process must not load.
    /// </summary>
    [Test]
    [Arguments("linux-musl-x64")]
    [Arguments("linux-musl-arm64")]
    public async Task MuslProbesItsOwnRidAndNothingElse(string runtimeIdentifier)
    {
        var rids = NativeResolver.Rids(runtimeIdentifier).ToList();

        await Assert.That(rids).IsEquivalentTo([runtimeIdentifier]);
    }

    /// <summary>
    /// Everywhere else the synthesised RID still follows the framework's own.
    /// </summary>
    [Test]
    public async Task OtherRidsStillFallBackToTheSynthesisedRid()
    {
        var rids = NativeResolver.Rids("some-rid").ToList();

        await Assert.That(rids.Count).IsGreaterThan(1);
        await Assert.That(rids[0]).IsEqualTo("some-rid");
    }
}
