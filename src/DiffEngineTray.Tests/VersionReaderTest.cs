[TestFixture]
public class VersionReaderTest
{
    [Test]
    public void AddSingle()
    {
        IsTrue(VersionReader.VersionString.Length > 0);
        NotNull(VersionReader.VersionString);
    }
}