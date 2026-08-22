public class GuardTests
{
    /// <summary>
    /// FileExists passed its arguments to AgainstEmpty the wrong way round, so the empty check was
    /// run against the literal parameter name - which is never empty. An empty path therefore fell
    /// through to "File not found. Path: " with no ParamName on it, naming nothing at all.
    /// </summary>
    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task FileExistsRejectsAnEmptyPathByName(string path)
    {
        var exception = await Assert.That(() => Guard.FileExists(path, "tempFile"))
            .Throws<ArgumentNullException>();

        await Assert.That(exception!.ParamName).IsEqualTo("tempFile");
    }

    [Test]
    public async Task FileExistsStillReportsAMissingFile()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"missing{Guid.NewGuid():N}.txt");

        await Assert.That(() => Guard.FileExists(missing, "tempFile"))
            .Throws<ArgumentException>();
    }
}
