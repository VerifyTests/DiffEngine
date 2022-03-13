public class ProcessCleanupTests :
    XunitContextBase
{
    [Fact]
    public void Find()
    {
        var enumerable = ProcessCleanup.FindAll().ToList();
        foreach (var x in enumerable)
        {
            Debug.WriteLine($"{x.Process} {x.Command}");
        }
    }

    public ProcessCleanupTests(ITestOutputHelper output) :
        base(output)
    {
    }
}