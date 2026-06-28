public class FileComparerTests
{
    static string TempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"FileComparerTests_{Guid.NewGuid()}.txt");
        File.WriteAllText(path, content);
        return path;
    }

    static async Task Cleanup(params string[] files)
    {
        await Task.Yield();
        foreach (var file in files)
        {
            File.Delete(file);
        }
    }

    [Test]
    public async Task Equal_files_are_equal()
    {
        var first = TempFile("the same content");
        var second = TempFile("the same content");
        try
        {
            await Assert.That(await FileComparer.FilesAreEqual(first, second)).IsTrue();
        }
        finally
        {
            await Cleanup(first, second);
        }
    }

    [Test]
    public async Task Different_sizes_are_not_equal()
    {
        var first = TempFile("short");
        var second = TempFile("a considerably longer piece of content");
        try
        {
            await Assert.That(await FileComparer.FilesAreEqual(first, second)).IsFalse();
        }
        finally
        {
            await Cleanup(first, second);
        }
    }

    [Test]
    public async Task Same_size_different_content_are_not_equal()
    {
        var first = TempFile("aaaaa");
        var second = TempFile("aaaab");
        try
        {
            await Assert.That(await FileComparer.FilesAreEqual(first, second)).IsFalse();
        }
        finally
        {
            await Cleanup(first, second);
        }
    }

    [Test]
    public async Task Empty_files_are_equal()
    {
        var first = TempFile("");
        var second = TempFile("");
        try
        {
            await Assert.That(await FileComparer.FilesAreEqual(first, second)).IsTrue();
        }
        finally
        {
            await Cleanup(first, second);
        }
    }

    [Test]
    public async Task Large_files_spanning_multiple_buffers()
    {
        // StreamsAreEqual reads in 8192-byte (1024 * sizeof(long)) chunks; exercise more than one chunk.
        var content = new string('x', 20000);
        var first = TempFile(content);
        var second = TempFile(content);
        var differsInLastChunk = TempFile(content[..^1] + "y");
        try
        {
            await Assert.That(await FileComparer.FilesAreEqual(first, second)).IsTrue();
            await Assert.That(await FileComparer.FilesAreEqual(first, differsInLastChunk)).IsFalse();
        }
        finally
        {
            await Cleanup(first, second, differsInLastChunk);
        }
    }
}
