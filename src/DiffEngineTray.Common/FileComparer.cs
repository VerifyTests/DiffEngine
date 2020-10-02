using System;
using System.IO;
using System.Threading.Tasks;

static class FileComparer
{
    static bool FilesAreSameSize(string file1, string file2)
    {
        var first = new FileInfo(file1);
        var second = new FileInfo(file2);
        return first.Length == second.Length;
    }

    static FileStream OpenRead(string path)
    {
        return new FileStream(path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
    }

    public static async Task<bool> FilesAreEqual(string file1, string file2)
    {
        if (!FilesAreSameSize(file1, file2))
        {
            return false;
        }

        using var fs1 = OpenRead(file1);
        using var fs2 = OpenRead(file2);
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
            await ReadBuffer(stream2, buffer2);

            var count = await t1;

            //no need to compare size since only enter on files being same size

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