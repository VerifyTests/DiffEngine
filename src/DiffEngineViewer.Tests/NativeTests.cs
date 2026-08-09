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
}
