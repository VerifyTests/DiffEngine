public class DiffEngineTrayTest
{
    [Fact]
    public void IsRunning() =>
        Assert.False(DiffEngineTray.IsRunning);
}