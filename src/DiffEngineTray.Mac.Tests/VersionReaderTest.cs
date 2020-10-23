using Xunit;
using Xunit.Abstractions;

public class VersionReaderTest :
    XunitContextBase
{
    [Fact]
    public void AddSingle()
    {
        Assert.NotEmpty(VersionReader.VersionString);
        Assert.NotNull(VersionReader.VersionString);
    }

    public VersionReaderTest(ITestOutputHelper output) :
        base(output)
    {
    }
}