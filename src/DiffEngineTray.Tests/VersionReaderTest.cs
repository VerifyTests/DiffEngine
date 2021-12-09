public class VersionReaderTest
{
    [Fact]
    public void AddSingle()
    {
        Assert.NotEmpty(VersionReader.VersionString);
        Assert.NotNull(VersionReader.VersionString);
    }
}