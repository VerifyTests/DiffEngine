/// <summary>
/// Snapshots of what actually ships, so an accidental addition or removal shows up as a reviewable
/// diff rather than as a surprise on nuget.org. Package content is assembled by MSBuild from
/// several unrelated mechanisms, and nothing else in the build asserts the result.
/// <para>
/// Windows only, by way of the solution file: <c>Release-NotWindows</c> drops DiffEngineTray, so
/// its package would be absent and these baselines would not describe a full release. That is also
/// why <c>publish-nuget.yml</c> runs on <c>windows-latest</c>.
/// </para>
/// <para>
/// One caveat: <c>nugets</c> is never cleaned, so a Release build followed by unrelated Debug work
/// leaves these asserting against the last packages that were actually produced.
/// </para>
/// </summary>
public class PackageTests
{
    /// <summary>
    /// Guards the set itself. Without this, a package that stopped being produced would take its
    /// content assertions with it and everything would still pass.
    /// </summary>
    [Test]
    [PackageTest]
    public Task Produced() =>
        Verify(string.Join('\n', Packages.Produced()));

    [Test]
    [PackageTest]
    [Arguments("DiffEngine")]
    [Arguments("DiffEngineTray")]
    [Arguments("DiffEngineViewer")]
    public async Task Contents(string id)
    {
        using var archive = Packages.Open(id);
        await Verify(string.Join('\n', Packages.Entries(archive)))
            .UseFileName($"Package.{id}");
    }
}
