using DiffEngine;

public class DiffEngineTrayTest :
    XunitContextBase
{
    [Fact]
    public void IsRunning()
    {
        Assert.False(DiffEngineTray.IsRunning);
    }

    public DiffEngineTrayTest(ITestOutputHelper output) :
        base(output)
    {
    }
}