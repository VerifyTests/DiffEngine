/// <summary>
/// buildTransitive/DiffEngine.targets writes values that C# constants read back, and nothing
/// else holds the two halves together: a key renamed in one place would not fail to compile, it
/// would silently stop the stamp arriving. The targets file is linked into the test output, so
/// this runs from source rather than from a packed nupkg.
/// </summary>
public class BuildTargetsTests
{
    [Test]
    public async Task CarriesTheKeysTheLibraryReads()
    {
        var targets = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "DiffEngine.targets"));
        await Assert.That(targets).Contains(RuntimeMoniker.Key);
        await Assert.That(targets).Contains(BundledViewerDirectory.Key);
    }
}
