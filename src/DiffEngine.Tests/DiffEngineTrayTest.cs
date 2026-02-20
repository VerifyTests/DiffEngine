#pragma warning disable CS0618 // Type or member is obsolete
public class DiffEngineTrayTest
{
    [Test]
    public async Task IsRunning() =>
        await Assert.That(DiffEngineTray.IsRunning).IsFalse();
}
