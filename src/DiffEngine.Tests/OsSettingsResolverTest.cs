public class OsSettingsResolverTest
{
    [Fact]
    public void Simple()
    {
        var paths = OsSettingsResolver.ExpandProgramFiles(["Path"]).ToList();
        Assert.Equal("Path", paths.Single());
    }

    [Fact]
    public void Expand()
    {
        var paths = OsSettingsResolver.ExpandProgramFiles([@"%ProgramFiles%\Path"]).ToList();
        Assert.Equal(@"%ProgramFiles%\Path", paths[0]);
        Assert.Equal(@"%ProgramW6432%\Path", paths[1]);
        Assert.Equal(@"%ProgramFiles(x86)%\Path", paths[2]);
    }

    [Fact]
    public void TryFindForEnvironmentVariable_NoEnv()
    {
        Assert.False(OsSettingsResolver.TryFindForEnvironmentVariable("FakeTool", "FakeTool.exe", out var envPath));
        Assert.Null(envPath);
    }

    [Fact]
    public void TryFindForEnvironmentVariable_WithEnvFile()
    {
        var location = Assembly.GetExecutingAssembly().Location;
        Environment.SetEnvironmentVariable("DiffEngine_FakeTool", location);
        try
        {
            Assert.True(OsSettingsResolver.TryFindForEnvironmentVariable("FakeTool", "DiffEngine.Tests.dll", out var envPath));
            Assert.Equal(location, envPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DiffEngine_FakeTool", null);
        }
    }

    [Fact]
    public void TryFindForEnvironmentVariable_WithEnvFile_incorrectCase()
    {
        var location = Assembly.GetExecutingAssembly().Location;
        Environment.SetEnvironmentVariable("DiffEngine_FakeTool", location);
        try
        {
            Assert.True(OsSettingsResolver.TryFindForEnvironmentVariable("FakeTool", "diffengine.tests.dll", out var envPath));
            Assert.Equal(location, envPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DiffEngine_FakeTool", null);
        }
    }

    [Fact]
    public void TryFindForEnvironmentVariable_WithEnvDir()
    {
        var location = Assembly.GetExecutingAssembly().Location;
        Environment.SetEnvironmentVariable("DiffEngine_FakeTool", Path.GetDirectoryName(location));
        try
        {
            Assert.True(OsSettingsResolver.TryFindForEnvironmentVariable("FakeTool", "DiffEngine.Tests.dll", out var envPath));
            Assert.Equal(location, envPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DiffEngine_FakeTool", null);
        }
    }

    [Fact]
    public void TryFindForEnvironmentVariable_WithEnv_BadPath()
    {
        var location = Assembly.GetExecutingAssembly().Location;
        Environment.SetEnvironmentVariable("DiffEngine_FakeTool", location);
        try
        {
            var exception = Assert.Throws<Exception>(()=>OsSettingsResolver.TryFindForEnvironmentVariable("FakeTool", "NoFile.dll", out _));
            Assert.Equal(exception.Message, $"Could not find exe defined by DiffEngine_FakeTool. Path: {location}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DiffEngine_FakeTool", null);
        }
    }

    [Fact]
    public void EnvPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var found = OsSettingsResolver.TryFindInEnvPath("cmd.exe", out var filePath);
            Assert.True(found);
            Assert.Equal(@"C:\Windows\System32\cmd.exe", filePath, ignoreCase: true);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var found = OsSettingsResolver.TryFindInEnvPath("sh", out var filePath);
            Assert.True(found);
            Assert.NotNull(filePath);
        }
    }

    [Fact]
    public void EnvVar()
    {
        var launchArguments = new LaunchArguments(
            Left: (_, _) => string.Empty,
            Right: (_, _) => string.Empty);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var found = OsSettingsResolver.Resolve(
            "ComSpec",
            new(Windows: new("cmd.exe", launchArguments, "")),
            out var filePath,
            out _);
        Assert.True(found);
        Assert.Equal(@"C:\Windows\System32\cmd.exe", filePath, ignoreCase: true);
    }
}