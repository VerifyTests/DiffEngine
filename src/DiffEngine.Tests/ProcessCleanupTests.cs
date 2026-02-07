public class ProcessCleanupTests
{
    [Fact]
    public void Find()
    {
        var list = ProcessCleanup.FindAll().ToList();
        // new processes have large Ids
        Assert.True(list[0].Process > list[1].Process);
        foreach (var x in list)
        {
            Debug.WriteLine($"{x.Process} {x.Command}");
        }
    }
}
