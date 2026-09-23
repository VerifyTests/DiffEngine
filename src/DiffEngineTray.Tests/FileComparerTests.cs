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

    /// <summary>
    /// The scan opens both files with FileShare.Read only, so for as long as a comparison
    /// runs, a test deleting its received file - what a passing re-run does - gets a sharing
    /// violation.
    /// </summary>
    [Test]
    public async Task ATestCanDeleteItsReceivedFileWhileTheScanComparesIt()
    {
        // Same size and large, so the comparison reads both through and is still reading when the
        // delete arrives
        var content = new byte[64 * 1024 * 1024];
        var temp = TempFile("");
        var target = TempFile("");
        await File.WriteAllBytesAsync(temp, content);
        await File.WriteAllBytesAsync(target, content);

        var comparing = FileComparer.FilesAreEqual(temp, target);

        Exception? refused = null;
        try
        {
            File.Delete(temp);
        }
        catch (IOException exception)
        {
            refused = exception;
        }

        // Both files are opened before FilesAreEqual first yields, and closed only as it completes,
        // so not being complete here means the delete arrived while the scan held them
        var overlapped = !comparing.IsCompleted;
        try
        {
            await comparing;
        }
        catch (IOException)
        {
        }

        await Cleanup(temp, target);
        await Assert.That(overlapped).IsTrue();
        await Assert.That(refused).IsNull();
    }


    /// <summary>
    /// With writers let in, a file can be cut short while it is compared. The shorter read is two
    /// files that differ, not two that both ended.
    /// </summary>
    [Test]
    public async Task A_file_cut_short_mid_compare_is_not_equal()
    {
        var content = new byte[64 * 1024 * 1024];
        var received = TempFile("");
        var verified = TempFile("");
        await File.WriteAllBytesAsync(received, content);
        await File.WriteAllBytesAsync(verified, content);
        try
        {
            var comparing = FileComparer.FilesAreEqual(received, verified);
            await using (var cut = new FileStream(received, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                cut.SetLength(8 * 1024 * 1024);
            }

            var overlapped = !comparing.IsCompleted;
            var equal = await comparing;

            await Assert.That(overlapped).IsTrue();
            await Assert.That(equal).IsFalse();
        }
        finally
        {
            await Cleanup(received, verified);
        }
    }
}
