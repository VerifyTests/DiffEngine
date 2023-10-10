public class OrderReaderTest(ITestOutputHelper output) :
    XunitContextBase(output)
{
    [Fact]
    public void ParseEnvironmentVariable()
    {
        var diffTools = OrderReader.ParseEnvironment("VisualStudio,Meld").ToList();
        Assert.Equal(DiffTool.VisualStudio, diffTools[0]);
        Assert.Equal(DiffTool.Meld, diffTools[1]);
    }

    [Fact]
    public void BadEnvironmentVariable()
    {
        var exception = Assert.Throws<Exception>(() => OrderReader.ParseEnvironment("Foo").ToList());
        Assert.Equal("Unable to parse tool from `DiffEngine_ToolOrder` environment variable: Foo", exception.Message);
    }
}