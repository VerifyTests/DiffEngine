[TestFixture]
public class ProcessCleanupTests
{
    [Test]
    public void Find()
    {
        var list = ProcessCleanup.FindAll().ToList();
        // new processes have large Ids
        True(list[0].Process > list[1].Process);
        foreach (var x in list)
        {
            Debug.WriteLine($"{x.Process} {x.Command}");
        }
    }
}