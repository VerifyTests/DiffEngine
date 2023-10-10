public class DiffEngineTrayTest(ITestOutputHelper output) :
    XunitContextBase(output)
{
    [Fact]
    public void IsRunning() =>
        Assert.False(DiffEngineTray.IsRunning);
}