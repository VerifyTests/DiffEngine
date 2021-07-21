using System.Diagnostics;
using Xunit;

public class ProcessExTest
{
    [Fact]
    public void AddSingle()
    {
        using var current = Process.GetCurrentProcess();
        Assert.True(ProcessEx.TryGet(current.Id, out var found));
        Assert.NotNull(found);
        found!.Dispose();
    }
}