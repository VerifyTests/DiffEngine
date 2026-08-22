/// <summary>
/// Which stamped path the bundled viewer is looked for under.
/// <para>
/// On .NET Framework there can be several: the path is carried as assembly metadata, and every
/// assembly built against the package carries one - including prebuilt dependencies, which hold
/// the package path of the machine that built them.
/// </para>
/// </summary>
public class BundledViewerRootTests
{
    [Test]
    public async Task Passes_over_a_root_that_is_not_on_this_machine()
    {
        var stale = Path.Combine(Path.GetTempPath(), $"BundledViewerRootTests_{Guid.NewGuid()}");
        var here = Path.GetTempPath();

        var root = BundledViewerDirectory.FirstUsable([null, "", stale, here]);

        await Assert.That(root).IsEqualTo(here);
    }

    [Test]
    public async Task Finds_nothing_when_no_root_is_on_this_machine()
    {
        var stale = Path.Combine(Path.GetTempPath(), $"BundledViewerRootTests_{Guid.NewGuid()}");

        var root = BundledViewerDirectory.FirstUsable([stale]);

        await Assert.That(root).IsNull();
    }
}
