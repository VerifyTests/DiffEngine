public class VersionReaderTest
{
    [Test]
    public async Task AddSingle()
    {
        await Assert.That(VersionReader.VersionString).IsNotEmpty();
        await Assert.That(VersionReader.VersionString).IsNotNull();
    }
}
