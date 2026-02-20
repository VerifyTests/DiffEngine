public class ProcessExTest
{
    [Test]
    public async Task TryGet()
    {
        using var current = Process.GetCurrentProcess();
        await Assert.That(ProcessEx.TryGet(current.Id, out var found)).IsTrue();
        found!.Dispose();
    }

    [Test]
    public async Task TryGetMissing()
    {
        await Assert.That(ProcessEx.TryGet(40000, out var found)).IsFalse();
        await Assert.That(found).IsNull();
    }
}
