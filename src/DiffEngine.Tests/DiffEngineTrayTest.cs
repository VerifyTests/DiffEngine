#pragma warning disable CS0618 // Type or member is obsolete
public class DiffEngineTrayTest(ITestOutputHelper output) :
    XunitContextBase(output)
{
    [Fact]
    public void IsRunning() =>
        Assert.False(DiffEngineTray.IsRunning);
}
