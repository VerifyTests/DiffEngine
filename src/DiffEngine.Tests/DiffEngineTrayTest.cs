#pragma warning disable CS0618 // Type or member is obsolete
public class DiffEngineTrayTest
{
    [Fact]
    public void IsRunning() =>
        Assert.False(DiffEngineTray.IsRunning);
}
