/// <summary>
/// A text file convention is invisible to the extension lookup, which is why Launch and Kill had
/// to stop using it.
/// <para>
/// Launch asked TryFindByExtension, which can only consult IsTextExtension, while LaunchAsync asks
/// TryFindForInputFilePath, which honours a text file convention. So a file matched by a
/// convention rather than by its extension - a name with no extension, a dotfile - opened a diff
/// tool asynchronously and reported NoDiffToolFound synchronously, and Kill then logged that it
/// could not find one for a pair LaunchAsync had opened, leaving the tool on screen.
/// </para>
/// <para>
/// This pins the gap between the two lookups. It does not drive DiffRunner.Launch, because
/// resolving a convention matched file can only ever land on the first text tool installed on the
/// machine - there is no way to register a fake one for it - and a test that launches the
/// developer's real diff tool is not worth having. The change in Launch and Kill is a change of
/// which of these two calls they make, and is verified by reading.
/// </para>
/// </summary>
[NotInParallel]
public class TextFileConventionResolutionTests
{
    [Test]
    public async Task AConventionIsInvisibleToTheExtensionLookup()
    {
        var name = $"conventionprobe{Guid.NewGuid():N}";
        FileExtensions.AddTextFileConvention(path => Path.GetFileNameWithoutExtension(path).StartsWith(name, StringComparison.Ordinal));

        var path = Path.Combine(Path.GetTempPath(), $"{name}.unknownextension");

        // What LaunchAsync resolves with
        var byPath = DiffTools.TryFindForInputFilePath(path, out var forAsync);
        // What Launch and Kill used to resolve with
        var byExtension = DiffTools.TryFindByExtension(Path.GetExtension(path), out _);

        if (!byPath)
        {
            // No text tool resolved on this machine, so there is nothing for either to find and
            // the two cannot disagree
            return;
        }

        await Assert.That(forAsync).IsNotNull();
        // The gap itself: the convention is invisible to the extension lookup
        await Assert.That(byExtension).IsFalse();

        await Assert.That(forAsync!.SupportsText).IsTrue();
    }
}
