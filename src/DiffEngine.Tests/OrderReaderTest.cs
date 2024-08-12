[TestFixture]
public class OrderReaderTest
{
    [Test]
    public void ParseEnvironmentVariable()
    {
        var diffTools = OrderReader.ParseEnvironment("VisualStudio,Meld").ToList();
        AreEqual(DiffTool.VisualStudio, diffTools[0]);
        AreEqual(DiffTool.Meld, diffTools[1]);
    }

    [Test]
    public void BadEnvironmentVariable()
    {
        var exception = Throws<Exception>(() => OrderReader.ParseEnvironment("Foo").ToList());
        AreEqual("Unable to parse tool from `DiffEngine_ToolOrder` environment variable: Foo", exception!.Message);
    }
}