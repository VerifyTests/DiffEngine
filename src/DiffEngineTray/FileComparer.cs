static class FileComparer
{
    static bool FilesAreSameSize(string file1, string file2)
    {
        var first = new FileInfo(file1);
        var second = new FileInfo(file2);
        return first.Length == second.Length;
    }

    /// <summary>
    /// Shared with writers and deleters. The scan compares for as long as reading both files takes,
    /// and read sharing alone failed a test's rewrite or delete of its received file with "being
    /// used by another process" for all of it. Which means a file can change under the compare,
    /// and <see cref="StreamsAreEqual"/> is what makes that read as different rather than equal.
    /// </summary>
    static FileStream OpenRead(string path) =>
        new(path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            useAsync: true);

    public static async Task<bool> FilesAreEqual(string file1, string file2)
    {
        if (!FilesAreSameSize(file1, file2))
        {
            return false;
        }

        await using var fs1 = OpenRead(file1);
        await using var fs2 = OpenRead(file2);
        return await StreamsAreEqual(fs1, fs2);
    }

    static async Task<bool> StreamsAreEqual(Stream stream1, Stream stream2)
    {
        const int bufferSize = 1024 * sizeof(long);
        var buffer1 = new byte[bufferSize];
        var buffer2 = new byte[bufferSize];

        while (true)
        {
            var t1 = ReadBuffer(stream1, buffer1);
            var count2 = await ReadBuffer(stream2, buffer2);

            var count = await t1;

            // The sizes matched when the compare began, but either file can be rewritten or cut
            // short while it runs. A pass that read less from one than the other is two files that
            // are no longer the same size, and ending on the shorter as though both had ended took
            // a received file truncated mid compare for equal to its verified file - and the scan
            // drops an equal pair and kills its diff tool.
            if (count != count2)
            {
                return false;
            }

            if (count == 0)
            {
                return true;
            }

            for (var i = 0; i < count; i += sizeof(long))
            {
                if (BitConverter.ToInt64(buffer1, i) != BitConverter.ToInt64(buffer2, i))
                {
                    return false;
                }
            }
        }
    }

    static async Task<int> ReadBuffer(Stream stream, byte[] buffer)
    {
        var bytesRead = 0;
        while (bytesRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer, bytesRead, buffer.Length - bytesRead);
            if (read == 0)
            {
                // Reached end of stream.
                return bytesRead;
            }

            bytesRead += read;
        }

        return bytesRead;
    }
}