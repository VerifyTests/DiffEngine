/// <summary>
/// Two things about the build files that a build cannot tell anyone. A second property of the same
/// name replaces the first rather than adding to it, and a target framework listed twice is
/// collapsed, so both mistakes build clean and say nothing.
/// </summary>
public class BuildFileTests
{
    /// <summary>
    /// There were two, so only the later list was in force and CS0649, NU1608 and NU1109 were
    /// suppressed nowhere - which under TreatWarningsAsErrors is a build failure waiting for the
    /// first unassigned field.
    /// </summary>
    [Test]
    public async Task NoWarn_is_declared_once()
    {
        var props = await File.ReadAllTextAsync(Path.Combine(Source(), "Directory.Build.props"));

        var declarations = props.Split(["<NoWarn>"], StringSplitOptions.None).Length - 1;

        await Assert.That(declarations).IsEqualTo(1);
    }

    [Test]
    public async Task No_target_framework_is_listed_twice()
    {
        var project = await File.ReadAllTextAsync(Path.Combine(Source(), "DiffEngine", "DiffEngine.csproj"));

        var listed = project
            .Split('\n')
            .Where(_ => _.Contains("<TargetFrameworks"))
            .SelectMany(_ => _[(_.IndexOf('>') + 1)..^"</TargetFrameworks>".Length].Split(';'))
            .Where(_ => _.StartsWith("net", StringComparison.Ordinal))
            .ToList();

        await Assert.That(listed).IsNotEmpty();
        await Assert.That(listed.Distinct()).IsEquivalentTo(listed);
    }

    /// <summary>
    /// The src directory, found by walking up from the test output rather than by counting
    /// directories, which differs per target framework and configuration.
    /// </summary>
    static string Source()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new("Could not find Directory.Build.props above the test output.");
    }
}
