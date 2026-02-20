using Assembly = System.Reflection.Assembly;

public class OsSettingsResolverTest
{
    [Test]
    public async Task Simple()
    {
        var paths = OsSettingsResolver.ExpandProgramFiles(["Path"]).ToList();
        await Assert.That(paths.Single()).IsEqualTo("Path");
    }

    [Test]
    public async Task Expand()
    {
        var paths = OsSettingsResolver.ExpandProgramFiles([@"%ProgramFiles%\Path"]).ToList();
        await Assert.That(paths[0]).IsEqualTo(@"%ProgramFiles%\Path");
        await Assert.That(paths[1]).IsEqualTo(@"%ProgramW6432%\Path");
        await Assert.That(paths[2]).IsEqualTo(@"%ProgramFiles(x86)%\Path");
    }

    [Test]
    public async Task TryFindForEnvironmentVariable_NoEnv()
    {
        await Assert.That(OsSettingsResolver.TryFindForEnvironmentVariable("FakeTool", "FakeTool.exe", out var envPath)).IsFalse();
        await Assert.That(envPath).IsNull();
    }

    [Test]
    public async Task TryFindForEnvironmentVariable_WithEnvFile()
    {
        var location = Assembly.GetExecutingAssembly().Location;
        Environment.SetEnvironmentVariable("DiffEngine_FakeTool", location);
        try
        {
            await Assert.That(OsSettingsResolver.TryFindForEnvironmentVariable("FakeTool", "DiffEngine.Tests.dll", out var envPath)).IsTrue();
            await Assert.That(envPath).IsEqualTo(location);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DiffEngine_FakeTool", null);
        }
    }

    [Test]
    public async Task TryFindForEnvironmentVariable_WithEnvFile_incorrectCase()
    {
        var location = Assembly.GetExecutingAssembly().Location;
        Environment.SetEnvironmentVariable("DiffEngine_FakeTool", location);
        try
        {
            await Assert.That(OsSettingsResolver.TryFindForEnvironmentVariable("FakeTool", "diffengine.tests.dll", out var envPath)).IsTrue();
            await Assert.That(envPath).IsEqualTo(location);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DiffEngine_FakeTool", null);
        }
    }

    [Test]
    public async Task TryFindForEnvironmentVariable_WithEnvDir()
    {
        var location = Assembly.GetExecutingAssembly().Location;
        Environment.SetEnvironmentVariable("DiffEngine_FakeTool", Path.GetDirectoryName(location));
        try
        {
            await Assert.That(OsSettingsResolver.TryFindForEnvironmentVariable("FakeTool", "DiffEngine.Tests.dll", out var envPath)).IsTrue();
            await Assert.That(envPath).IsEqualTo(location);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DiffEngine_FakeTool", null);
        }
    }

    [Test]
    public async Task TryFindForEnvironmentVariable_WithEnv_BadPath()
    {
        var location = Assembly.GetExecutingAssembly().Location;
        Environment.SetEnvironmentVariable("DiffEngine_FakeTool", location);
        try
        {
            var exception = Assert.Throws<Exception>(()=>OsSettingsResolver.TryFindForEnvironmentVariable("FakeTool", "NoFile.dll", out _));
            await Assert.That($"Could not find exe defined by DiffEngine_FakeTool. Path: {location}").IsEqualTo(exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DiffEngine_FakeTool", null);
        }
    }

    [Test]
    public async Task EnvPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var found = OsSettingsResolver.TryFindInEnvPath("cmd.exe", out var filePath);
            await Assert.That(found).IsTrue();
            await Assert.That(filePath).IsEqualTo(@"C:\Windows\System32\cmd.exe");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var found = OsSettingsResolver.TryFindInEnvPath("sh", out var filePath);
            await Assert.That(found).IsTrue();
            await Assert.That(filePath).IsNotNull();
        }
    }

    [Test]
    public async Task EnvVar()
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
        await Assert.That(found).IsTrue();
        await Assert.That(filePath).IsEqualTo(@"C:\Windows\System32\cmd.exe");
    }
}
