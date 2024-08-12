[TestFixture]
public class OsSettingsResolverTest
{
    [Test]
    public void Simple()
    {
        var paths = OsSettingsResolver.ExpandProgramFiles(["Path"]).ToList();
        AreEqual("Path", paths.Single());
    }

    [Test]
    public void Expand()
    {
        var paths = OsSettingsResolver.ExpandProgramFiles([@"%ProgramFiles%\Path"]).ToList();
        AreEqual(@"%ProgramFiles%\Path", paths[0]);
        AreEqual(@"%ProgramW6432%\Path", paths[1]);
        AreEqual(@"%ProgramFiles(x86)%\Path", paths[2]);
    }

    [Test]
    public void TryFindForEnvironmentVariable_NoEnv()
    {
        False(OsSettingsResolver.TryFindForEnvironmentVariable("FakeTool", "FakeTool.exe", out var envPath));
        Null(envPath);
    }

    [Test]
    public void TryFindForEnvironmentVariable_WithEnvFile()
    {
        var location = Assembly.GetExecutingAssembly().Location;
        Environment.SetEnvironmentVariable("DiffEngine_FakeTool", location);
        try
        {
            True(OsSettingsResolver.TryFindForEnvironmentVariable("FakeTool", "DiffEngine.Tests.dll", out var envPath));
            AreEqual(location, envPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DiffEngine_FakeTool", null);
        }
    }

    [Test]
    public void TryFindForEnvironmentVariable_WithEnvFile_incorrectCase()
    {
        var location = Assembly.GetExecutingAssembly().Location;
        Environment.SetEnvironmentVariable("DiffEngine_FakeTool", location);
        try
        {
            True(OsSettingsResolver.TryFindForEnvironmentVariable("FakeTool", "diffengine.tests.dll", out var envPath));
            AreEqual(location, envPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DiffEngine_FakeTool", null);
        }
    }

    [Test]
    public void TryFindForEnvironmentVariable_WithEnvDir()
    {
        var location = Assembly.GetExecutingAssembly().Location;
        Environment.SetEnvironmentVariable("DiffEngine_FakeTool", Path.GetDirectoryName(location));
        try
        {
            True(OsSettingsResolver.TryFindForEnvironmentVariable("FakeTool", "DiffEngine.Tests.dll", out var envPath));
            AreEqual(location, envPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DiffEngine_FakeTool", null);
        }
    }

    [Test]
    public void TryFindForEnvironmentVariable_WithEnv_BadPath()
    {
        var location = Assembly.GetExecutingAssembly().Location;
        Environment.SetEnvironmentVariable("DiffEngine_FakeTool", location);
        try
        {
            var exception = Throws<Exception>(()=>OsSettingsResolver.TryFindForEnvironmentVariable("FakeTool", "NoFile.dll", out _))!;
            AreEqual(exception.Message, $"Could not find exe defined by DiffEngine_FakeTool. Path: {location}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DiffEngine_FakeTool", null);
        }
    }

    [Test]
    public void EnvPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var found = OsSettingsResolver.TryFindInEnvPath("cmd.exe", out var filePath);
            True(found);
            AreEqual(@"c:\windows\system32\cmd.exe", filePath!.ToLower());
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var found = OsSettingsResolver.TryFindInEnvPath("sh", out var filePath);
            True(found);
            NotNull(filePath);
        }
    }

    [Test]
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
        True(found);
        AreEqual(@"c:\windows\system32\cmd.exe", filePath!.ToLower());
    }
}