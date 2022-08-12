public class ProcessExTest
{
    [Fact]
    public void TryGet()
    {
        using var current = Process.GetCurrentProcess();
        Assert.True(ProcessEx.TryGet(current.Id, out var found));
        Assert.NotNull(found);
        found.Dispose();
    }

    [Fact]
    public void TryGetMissing()
    {
        Assert.False(ProcessEx.TryGet(40000, out var found));
        Assert.Null(found);
    }
}