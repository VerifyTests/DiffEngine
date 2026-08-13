using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

/// <summary>
/// A real, decodable PNG built byte by byte.
/// <para>
/// Not a platform encoder, because these bytes reach committed pixel baselines on three platforms:
/// the pane prints the file's byte count, so an encoder that packs one byte differently on one of
/// them would move a number on screen and fail a baseline for a reason that has nothing to do with
/// the viewer. Stored deflate blocks and no filtering make the output a function of the arguments
/// alone.
/// </para>
/// <para>
/// PNG rather than a spread of formats, because every head has a decoder for it. The formats that
/// only some of them read are covered by the ASCII screens, which is where the difference is
/// visible: the rows are what all three draw.
/// </para>
/// </summary>
static class SamplePng
{
    /// <summary>
    /// A solid colour whose right hand half fades to fully transparent, so a capture shows both
    /// the picture and the checkerboard that has to be visible through it.
    /// </summary>
    public static byte[] Build(int width, int height, byte red, byte green, byte blue)
    {
        var raw = new byte[height * (1 + width * 4)];
        var index = 0;
        for (var y = 0; y < height; y++)
        {
            // Filter type 0: the row is stored as it is. The other four exist to help a compressor
            // that is not being asked to compress anything here.
            raw[index++] = 0;
            for (var x = 0; x < width; x++)
            {
                raw[index++] = red;
                raw[index++] = green;
                raw[index++] = blue;
                raw[index++] = Alpha(x, width);
            }
        }

        List<byte> bytes = [0x89, (byte) 'P', (byte) 'N', (byte) 'G', 0x0D, 0x0A, 0x1A, 0x0A];
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        // Eight bits a channel, truecolour with alpha, and none of the three optional encodings.
        header[8] = 8;
        header[9] = 6;
        Chunk(bytes, "IHDR", header);
        Chunk(bytes, "IDAT", Compress(raw));
        Chunk(bytes, "IEND", []);
        return [.. bytes];
    }

    static byte Alpha(int x, int width)
    {
        var half = width / 2;
        if (x < half)
        {
            return 255;
        }

        return (byte) (255 - 255 * (x - half) / Math.Max(1, width - half - 1));
    }

    /// <summary>
    /// A zlib stream of stored blocks. Deterministic across runtimes in a way a real deflate is
    /// not promised to be, which is the whole reason this exists rather than a call to an encoder.
    /// </summary>
    static byte[] Compress(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.NoCompression, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        return output.ToArray();
    }

    static void Chunk(List<byte> bytes, string name, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        bytes.AddRange(length);

        // The name and the data are one run for the checksum, which covers both and not the length.
        var payload = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(name).CopyTo(payload, 0);
        data.CopyTo(payload, 4);
        bytes.AddRange(payload);

        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(payload));
        bytes.AddRange(crc);
    }

    /// <summary>
    /// CRC-32 a bit at a time. A table would be faster and this runs over a few hundred bytes in a
    /// test.
    /// </summary>
    static uint Crc(byte[] bytes)
    {
        var value = 0xFFFFFFFFu;
        foreach (var current in bytes)
        {
            value ^= current;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) == 0 ? value >> 1 : 0xEDB88320u ^ (value >> 1);
            }
        }

        return value ^ 0xFFFFFFFFu;
    }
}
