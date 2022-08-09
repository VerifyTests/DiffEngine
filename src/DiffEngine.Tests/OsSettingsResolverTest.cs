using System.Runtime.InteropServices;

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

    [Fact]
    public void CliDefinition()
    {
        var cli = OsSettingsResolver.IsCliDefinition("Path");
        Assert.Equal(true, cli);
    }

    [Fact]
    public void NotCliDefinition()
    {
        var path = Path.Combine("SomeDirectory", "Path");
        var cli = OsSettingsResolver.IsCliDefinition(path);
        Assert.Equal(false, cli);
    }

    [Fact]
    public void EnvPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var found = OsSettingsResolver.TryFindInEnvPath("cmd.exe", out var filePath);
            Assert.Equal(true, found);
            Assert.Equal(@"C:\Windows\System32\cmd.exe", filePath, ignoreCase: true);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var found = OsSettingsResolver.TryFindInEnvPath("sh", out var filePath);
            Assert.Equal(true, found);
            Assert.NotNull(filePath);
        }
    }

    public OsSettingsResolverTest(ITestOutputHelper output) :
        base(output)
    {
    }
}