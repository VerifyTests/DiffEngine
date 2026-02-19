public class OrderReaderTest
{
    [Test]
    public async Task ParseEnvironmentVariable()
    {
        var diffTools = OrderReader.ParseEnvironment("VisualStudio,Meld").ToList();
        await Assert.That(diffTools[0]).IsEqualTo(DiffTool.VisualStudio);
        await Assert.That(diffTools[1]).IsEqualTo(DiffTool.Meld);
    }

    [Test]
    public async Task BadEnvironmentVariable()
    {
        // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
        var exception = Assert.Throws<Exception>(() => OrderReader.ParseEnvironment("Foo").ToList());
        await Assert.That(exception.Message).IsEqualTo("Unable to parse tool from `DiffEngine_ToolOrder` environment variable: Foo");
    }
}
