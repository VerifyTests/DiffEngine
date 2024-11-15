public class VersionTest
{
    [Fact]
    public void Run()
    {
        #if NET6_0_OR_GREATER
        // because v9 throw "windows not supported" for .net 6
        var version = typeof(System.Management.ManagementObject).Assembly.GetName().Version!;
        Assert.Equal("8.0.0.0", version.ToString());
        #endif
    }
}