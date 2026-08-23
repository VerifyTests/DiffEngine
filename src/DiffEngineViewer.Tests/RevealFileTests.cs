/// <summary>
/// What "reveal" opens. The path it is given is often one that does not exist: revealing a pending
/// move points at the target, and for a snapshot being written for the first time nothing is there
/// yet.
/// </summary>
public class RevealFileTests :
    IDisposable
{
    [Test]
    public async Task A_file_that_is_there_is_selected()
    {
        var file = Path.Combine(directory, "sample.verified.txt");
        File.WriteAllText(file, "");

        var resolved = RevealFile.Resolve(file);

        await Assert.That(resolved!.Value.Target).IsEqualTo(file);
        await Assert.That(resolved.Value.Select).IsTrue();
    }

    /// <summary>
    /// Explorer opens the default folder when asked to select a path that is not there - Documents,
    /// nothing to do with the review - and <c>open -R</c> errors. The directory the file is about
    /// to be written into is the useful answer.
    /// </summary>
    [Test]
    public async Task A_file_that_is_not_there_yet_falls_back_to_its_directory()
    {
        var resolved = RevealFile.Resolve(Path.Combine(directory, "new.verified.txt"));

        await Assert.That(resolved!.Value.Target).IsEqualTo(directory);
        await Assert.That(resolved.Value.Select).IsFalse();
    }

    [Test]
    public async Task Nothing_is_opened_for_a_path_with_no_directory_either()
    {
        var missing = Path.Combine(directory, "gone", "new.verified.txt");

        await Assert.That(RevealFile.Resolve(missing)).IsNull();
    }

    public RevealFileTests()
    {
        directory = Path.Combine(Path.GetTempPath(), $"RevealFileTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(directory);
    }

    public void Dispose() =>
        Directory.Delete(directory, true);

    readonly string directory;
}
