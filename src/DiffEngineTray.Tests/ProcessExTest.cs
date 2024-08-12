[TestFixture]
public class ProcessExTest
{
    [Test]
    public void TryGet()
    {
        using var current = Process.GetCurrentProcess();
        True(ProcessEx.TryGet(current.Id, out var found));
        NotNull(found);
        found!.Dispose();
    }

    [Test]
    public void TryGetMissing()
    {
        False(ProcessEx.TryGet(40000, out var found));
        Null(found);
    }
}