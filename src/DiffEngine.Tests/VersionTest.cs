public class VersionTest
{
    [Fact]
    public void Run()
    {
        var version = typeof(System.Management.ManagementObject).Assembly.GetName().Version!;
        Assert.Equal("8.0.0.0", version.ToString());
    }
}