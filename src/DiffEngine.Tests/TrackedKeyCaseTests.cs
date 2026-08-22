/// <summary>
/// Tracked keys fold case exactly where inline keys do.
/// <para>
/// TrackedKeys lower-cased unconditionally while InlineKey folds only on Windows and macOS, and
/// InlineKey says why: on Linux two paths differing only in case are two files, and giving them
/// one key means the second takes over the first's entry. For a tracked move that shows up as
/// ViewerSession.EnqueueTracked dropping one of them - a parameterised test with value=a and
/// value=A silently loses a pending file.
/// </para>
/// <para>
/// Asserted as agreement between the two rather than as a platform's answer, so it holds wherever
/// it runs.
/// </para>
/// </summary>
public class TrackedKeyCaseTests
{
    const string upper = "/repo/tests/Value.txt";
    const string lower = "/repo/tests/value.txt";

    [Test]
    public async Task MoveKeysFoldWithInlineKeys()
    {
        var inlineFolds = InlineKey.For(upper, 1) == InlineKey.For(lower, 1);

        await Assert.That(TrackedKeys.ForMove(upper) == TrackedKeys.ForMove(lower))
            .IsEqualTo(inlineFolds);
    }

    [Test]
    public async Task DeleteKeysFoldWithInlineKeys()
    {
        var inlineFolds = InlineKey.For(upper, 1) == InlineKey.For(lower, 1);

        await Assert.That(TrackedKeys.ForDelete(upper) == TrackedKeys.ForDelete(lower))
            .IsEqualTo(inlineFolds);
    }
}
