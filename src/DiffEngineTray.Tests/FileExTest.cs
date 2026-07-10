public class FileExTest
{
    [Test]
    public async Task SafeDeleteDirectoryRemovesDirectoryWithOnlyEmptySubDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiffEngineSafeDelete", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "sub", "nested"));

        FileEx.SafeDeleteDirectory(root);

        await Assert.That(Directory.Exists(root)).IsFalse();
    }

    [Test]
    public async Task SafeDeleteDirectoryKeepsDirectoryThatContainsAFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiffEngineSafeDelete", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "keep.txt"), "data");
        try
        {
            FileEx.SafeDeleteDirectory(root);

            await Assert.That(Directory.Exists(root)).IsTrue();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
