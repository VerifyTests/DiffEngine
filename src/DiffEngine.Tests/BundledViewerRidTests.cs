#if NET10_0
/// <summary>
/// Which RIDs the bundled viewer is probed for.
/// <para>
/// The framework's own RID comes first, and on Alpine that is linux-musl-x64, which the package
/// does not ship - so the probe is supposed to miss and the caller falls through to the dotnet
/// tool. The synthesised linux-{arch} that followed it undid that, because it names the glibc
/// build, and resolving one against musl hands back an apphost that cannot start.
/// </para>
/// </summary>
public class BundledViewerRidTests
{
    [Test]
    [Arguments("linux-musl-x64")]
    [Arguments("linux-musl-arm64")]
    public async Task MuslProbesItsOwnRidAndNothingElse(string runtimeIdentifier)
    {
        var rids = BundledViewerDirectory.Rids(runtimeIdentifier).ToList();

        await Assert.That(rids).IsEquivalentTo([runtimeIdentifier]);
    }

    /// <summary>
    /// Everywhere else the synthesised RID still follows, which is what makes the probe work when
    /// the framework reports something the package does not ship under that exact name.
    /// </summary>
    [Test]
    public async Task GlibcStillFallsBackToTheSynthesisedRid()
    {
        var rids = BundledViewerDirectory.Rids("linux-x64").ToList();

        await Assert.That(rids.Count).IsGreaterThan(1);
        await Assert.That(rids[0]).IsEqualTo("linux-x64");
    }
}
#endif
