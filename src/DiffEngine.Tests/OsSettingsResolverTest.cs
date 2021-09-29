using Xunit;
using Xunit.Abstractions;

public class OsSettingsResolverTest :
    XunitContextBase
{
    [Fact]
    public void Simple()
    {
        var paths = OsSettingsResolver.ExpandProgramFiles(new[] {"Path"}).ToList();
        Assert.Equal("Path", paths.Single());
    }

    [Fact]
    public void Expand()
    {
        var paths = OsSettingsResolver.ExpandProgramFiles(new[] {@"%ProgramFiles%\Path"}).ToList();
        Assert.Equal(@"%ProgramFiles%\Path", paths[0]);
        Assert.Equal(@"%ProgramW6432%\Path", paths[1]);
        Assert.Equal(@"%ProgramFiles(x86)%\Path", paths[2]);
    }

    public OsSettingsResolverTest(ITestOutputHelper output) :
        base(output)
    {
    }
}