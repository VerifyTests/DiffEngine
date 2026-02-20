public class ProcessCleanupTests
{
    [Test]
    public async Task Find()
    {
        var list = ProcessCleanup.FindAll().ToList();
        // new processes have large Ids
        await Assert.That(list[0].Process > list[1].Process).IsTrue();
        foreach (var x in list)
        {
            Debug.WriteLine($"{x.Process} {x.Command}");
        }
    }
}
