/// <summary>
/// Finding a tool installed on Windows from inside a WSL distribution.
/// <para>
/// No WSL is needed to run these, which is the point of <see cref="WslHost" /> taking what it
/// knows about the host as arguments. A directory under temp stands in for the drive the
/// distribution has mounted, so the lookups run against real files on whatever the tests are
/// running on.
/// </para>
/// </summary>
[NotInParallel]
public class WslHostTests :
    IDisposable
{
    // Forward slashes throughout, as every path inside a distribution has
    readonly string drive = Path.Combine(Path.GetTempPath(), $"WslHostTests_{Guid.NewGuid():N}").Replace('\\', '/');

    public void Dispose()
    {
        if (Directory.Exists(drive))
        {
            Directory.Delete(drive, true);
        }
    }

    WslHost Host(params string[] pathDirectories) =>
        new(
            new([new(drive, @"C:\")], @"\\wsl.localhost\Ubuntu"),
            WslHost.ParseVariables(
                """
                =C:=C:\Users\simon
                LOCALAPPDATA=C:\Users\simon\AppData\Local
                ProgramFiles=C:\Program Files
                ProgramFiles(x86)=C:\Program Files (x86)
                ProgramW6432=C:\Program Files
                SystemRoot=C:\WINDOWS
                USERPROFILE=C:\Users\simon
                Odd=a=b
                """),
            pathDirectories);

    string Write(string relative)
    {
        var path = $"{drive}/{relative}";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    static OsSettings Settings(string exeName, params string[] directories) =>
        new(exeName, exeName, Arguments, directories);

    static LaunchArguments Arguments { get; } = new(
        Left: (temp, target) => $"\"{target}\" \"{temp}\"",
        Right: (temp, target) => $"\"{temp}\" \"{target}\"");

    /// <summary>
    /// What <c>cmd.exe /c set</c> prints. A name is matched however it is cased, since
    /// definitions write both <c>%LocalAppData%</c> and <c>%LOCALAPPDATA%</c>, and only the first
    /// equals sign divides a line.
    /// </summary>
    [Test]
    public async Task VariablesAreReadFromWhatSetPrints()
    {
        var variables = WslHost.ParseVariables("ProgramFiles=C:\\Program Files\r\n=C:=C:\\Users\r\nOdd=a=b\r\n\r\nno separator\r\n");

        await Assert.That(variables.Count).IsEqualTo(2);
        await Assert.That(variables["programfiles"]).IsEqualTo(@"C:\Program Files");
        await Assert.That(variables["Odd"]).IsEqualTo("a=b");
    }

    [Test]
    [Arguments(@"%ProgramFiles%\Beyond Compare *\", @"C:\Program Files\Beyond Compare *\")]
    [Arguments(@"%LocalAppData%\Programs\Tool\", @"C:\Users\simon\AppData\Local\Programs\Tool\")]
    [Arguments(@"%USERPROFILE%\.dotnet\tools\", @"C:\Users\simon\.dotnet\tools\")]
    [Arguments(@"%ProgramFiles(x86)%\Tool", @"C:\Program Files (x86)\Tool")]
    [Arguments(@"C:\Tools\100%", @"C:\Tools\100%")]
    public async Task ADirectoryIsExpandedWithTheHostsVariables(string directory, string expected) =>
        await Assert.That(Host().Expand(directory)).IsEqualTo(expected);

    /// <summary>
    /// Not left as written, the way Environment.ExpandEnvironmentVariables leaves it: a
    /// directory still holding a variable is not one that can be looked in.
    /// </summary>
    [Test]
    public async Task ADirectoryNamingAVariableTheHostLacksIsDropped()
    {
        var host = Host();

        await Assert.That(host.Expand(@"%NUGET_PACKAGES%\tool\")).IsNull();
        await Assert.That(host.SearchDirectories([@"%NUGET_PACKAGES%\tool\", @"Z:\tool\", @"%ProgramFiles%\Tool\"]))
            .IsEquivalentTo([$"{drive}/Program Files/Tool/"]);
    }

    [Test]
    public async Task AToolIsFoundInItsWindowsDirectory()
    {
        var exe = Write("Program Files/Tool 5/Tool.exe");
        var settings = Settings("Tool.exe", @"%ProgramFiles%\Tool *\");

        await Assert.That(OsSettingsResolver.TryFindOnHost(Host(), settings, out var path)).IsTrue();
        await Assert.That(Path.GetFullPath(path!)).IsEqualTo(Path.GetFullPath(exe));
    }

    /// <summary>
    /// The directory is searched as each of the three Program Files a Windows machine has, as it
    /// is on Windows itself.
    /// </summary>
    [Test]
    public async Task AToolIsFoundInThe32BitProgramFiles()
    {
        var exe = Write("Program Files (x86)/Tool/Tool.exe");
        var settings = Settings("Tool.exe", @"%ProgramFiles%\Tool\");

        await Assert.That(OsSettingsResolver.TryFindOnHost(Host(), settings, out var path)).IsTrue();
        await Assert.That(Path.GetFullPath(path!)).IsEqualTo(Path.GetFullPath(exe));
    }

    /// <summary>
    /// A <c>.cmd</c> is a script for <c>cmd.exe</c>. The kernel hands WSL a Windows executable
    /// and has nothing to do with a script, so a definition that names one is not found this way
    /// even where the file is there.
    /// </summary>
    [Test]
    public async Task AScriptIsNotATool()
    {
        Write("Program Files/Tool/tool.cmd");
        var settings = Settings("tool.cmd", @"%ProgramFiles%\Tool\");

        await Assert.That(OsSettingsResolver.TryFindOnHost(Host($"{drive}/Program Files/Tool"), settings, out var path)).IsFalse();
        await Assert.That(path).IsNull();
    }

    [Test]
    public async Task AToolNotInItsDirectoryIsFoundOnThePath()
    {
        var exe = Write("scoop/shims/tool.exe");
        var settings = Settings("Tool.exe", @"%ProgramFiles%\Tool\");
        var host = Host("/usr/bin", $"{drive}/empty", $"{drive}/scoop/shims/");

        await Assert.That(OsSettingsResolver.TryFindOnHost(host, settings, out var path)).IsTrue();
        await Assert.That(Path.GetFullPath(path!)).IsEqualTo(Path.GetFullPath(exe));
    }

    /// <summary>
    /// A definition can name an executable to search directories for and a script to find on the
    /// PATH, as Rider's does. The script is not looked for.
    /// </summary>
    [Test]
    public async Task AScriptIsNotLookedForOnThePath()
    {
        Write("bin/tool.cmd");
        var settings = new OsSettings("tool64.exe", "tool.cmd", Arguments, @"%ProgramFiles%\Tool\");

        await Assert.That(OsSettingsResolver.TryFindOnHost(Host($"{drive}/bin"), settings, out _)).IsFalse();
    }

    /// <summary>
    /// The PATH is the distribution's, with the host's appended. Only the host's directories are
    /// listed, the first to hold a name wins, and the Windows directory is passed over: it holds
    /// most of the files on a PATH and no diff tool.
    /// </summary>
    [Test]
    public async Task ThePathIsTheHostsDirectoriesInOrder()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), $"WslHostTests_{Guid.NewGuid():N}").Replace('\\', '/');
        Directory.CreateDirectory(elsewhere);
        try
        {
            File.WriteAllText($"{elsewhere}/tool.exe", "");
            Write("WINDOWS/system32/tool.exe");
            Write("WINDOWS/other.exe");
            var first = Write("first/Tool.exe");
            Write("second/tool.exe");
            Write("second/notes.txt");
            var host = Host(elsewhere, $"{drive}/Windows/System32", $"{drive}/WINDOWS", $"{drive}/first", $"{drive}/second");

            await Assert.That(host.TryFindOnPath("TOOL.EXE", out var path)).IsTrue();
            await Assert.That(Path.GetFullPath(path!)).IsEqualTo(Path.GetFullPath(first));
            await Assert.That(host.TryFindOnPath("other.exe", out _)).IsFalse();
            await Assert.That(host.TryFindOnPath("notes.txt", out _)).IsFalse();
        }
        finally
        {
            Directory.Delete(elsewhere, true);
        }
    }

    /// <summary>
    /// <c>DiffEngine_Tool</c> can name the Windows copy either way round: as the distribution
    /// sees the directory, or as it is written on the host.
    /// </summary>
    [Test]
    [Arguments(@"C:\Custom\Tool")]
    [Arguments("C:/Custom/Tool")]
    [Arguments(@"C:\Custom\Tool\Tool.exe")]
    public async Task AVariableCanNameTheWindowsCopyAsTheHostWritesIt(string value)
    {
        var exe = Write("Custom/Tool/Tool.exe");
        Environment.SetEnvironmentVariable("DiffEngine_WslHostTool", value);
        try
        {
            await Assert.That(OsSettingsResolver.TryFindOnHostForEnvironmentVariable(Host(), "WslHostTool", "Tool.exe", true, out var path)).IsTrue();
            await Assert.That(Path.GetFullPath(path!)).IsEqualTo(Path.GetFullPath(exe));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DiffEngine_WslHostTool", null);
        }
    }

    [Test]
    public async Task AVariableCanNameTheWindowsCopyAsTheDistributionSeesIt()
    {
        var exe = Write("Custom/Tool/Tool.exe");
        Environment.SetEnvironmentVariable("DiffEngine_WslHostTool", $"{drive}/Custom/Tool");
        try
        {
            await Assert.That(OsSettingsResolver.TryFindOnHostForEnvironmentVariable(Host(), "WslHostTool", "Tool.exe", true, out var path)).IsTrue();
            await Assert.That(Path.GetFullPath(path!)).IsEqualTo(Path.GetFullPath(exe));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DiffEngine_WslHostTool", null);
        }
    }

    /// <summary>
    /// A variable that names nothing is an error only where no other definition will be asked.
    /// A tool with a Linux definition may have it naming the Linux copy, and that lookup is the
    /// one to say whether it is there.
    /// </summary>
    [Test]
    public async Task AVariableNamingNothingThrowsOnlyWhenNothingElseWillLook()
    {
        Environment.SetEnvironmentVariable("DiffEngine_WslHostTool", @"C:\Nowhere");
        try
        {
            var host = Host();

            await Assert.That(OsSettingsResolver.TryFindOnHostForEnvironmentVariable(host, "WslHostTool", "Tool.exe", false, out var path)).IsFalse();
            await Assert.That(path).IsNull();

            var exception = Assert.Throws<Exception>(() => OsSettingsResolver.TryFindOnHostForEnvironmentVariable(host, "WslHostTool", "Tool.exe", true, out _));
            await Assert.That(exception.Message).IsEqualTo(@"Could not find exe defined by DiffEngine_WslHostTool. Path: C:\Nowhere");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DiffEngine_WslHostTool", null);
        }
    }

    [Test]
    public async Task WithNoVariableNothingIsFoundAndNothingThrown() =>
        await Assert.That(OsSettingsResolver.TryFindOnHostForEnvironmentVariable(Host(), "WslHostToolUnset", "Tool.exe", true, out _)).IsFalse();
}
