using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

/// <summary>
/// What the Linux head is given to draw in a benchmark: a text comparison that fills the window
/// at any size, and two comparisons of pictures.
/// <para>
/// The pictures are real files, since a head draws a picture from the path the model carries, and
/// they are large enough to fill a pane of a 4K window, since a fitted picture is never drawn
/// larger than its own size. Both kinds are stored with an alpha channel. One fades to nothing
/// across its right hand half, so the checkerboard behind it has to show through. The other has
/// no pixel that is anything but opaque, which is what a drawn page of a document or a screenshot
/// is, and nothing behind it can be seen.
/// </para>
/// </summary>
static class NativeScenes
{
    const int pictureSize = 1920;

    public static SessionState Text(int columns, int rows) =>
        Comparing(
            columns,
            rows,
            QueueEntry.ForFiles(
                "sample.received.txt",
                "sample.verified.txt",
                FileSide.OfText(Lines(true)),
                FileSide.OfText(Lines(false))));

    public static SessionState OpaquePictures(int columns, int rows) =>
        Pictures(columns, rows, true);

    public static SessionState TranslucentPictures(int columns, int rows) =>
        Pictures(columns, rows, false);

    static SessionState Pictures(int columns, int rows, bool opaque)
    {
        var kind = opaque ? "opaque" : "translucent";
        var left = Write($"{kind}.received.png", Png(198, 64, 64, opaque));
        var right = Write($"{kind}.verified.png", Png(64, 150, 198, opaque));
        return Comparing(columns, rows, QueueEntry.ForFiles(left, right, FileSide.Read(left), FileSide.Read(right)));
    }

    /// <summary>
    /// The same frame twice, told apart by one letter of its status line and by nothing else. A
    /// benchmark of what a frame costs to draw presents them in turn, so that each is a frame the
    /// head has to draw, however good it gets at leaving an unchanged one alone.
    /// </summary>
    public static Screen[] Alternating(SessionState state)
    {
        var screen = ScreenBuilder.Build(state);
        return
        [
            screen with { Status = "tick" },
            screen with { Status = "tock" }
        ];
    }

    static SessionState Comparing(int columns, int rows, QueueEntry entry) =>
        ViewerSession.EnqueueFile(SessionState.Start(ViewerMode.File, columns, rows), entry);

    /// <summary>
    /// Three hundred lines, which is more than a 4K window shows, one in ten of them changed.
    /// </summary>
    static string Lines(bool changed)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < 300; index++)
        {
            var value = changed && index % 10 == 0 ? index + 1 : index;
            builder.Append($"  \"property{index:D3}\": \"the value of property {value:D3}\",\n");
        }

        return builder.ToString();
    }

    static string Write(string name, byte[] content)
    {
        var directory = Path.Combine(Path.GetTempPath(), "deview-benchmarks");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    /// <summary>
    /// A colour that darkens from top to bottom, so the picture is more than one flat fill, with
    /// eight bits a channel and an alpha channel either way.
    /// </summary>
    static byte[] Png(byte red, byte green, byte blue, bool opaque)
    {
        var half = pictureSize / 2;
        var raw = new byte[pictureSize * (1 + pictureSize * 4)];
        var index = 0;
        for (var y = 0; y < pictureSize; y++)
        {
            // Filter type 0: the row is stored as it is.
            raw[index++] = 0;
            var shade = 255 - 128 * y / pictureSize;
            for (var x = 0; x < pictureSize; x++)
            {
                raw[index++] = (byte) (red * shade / 255);
                raw[index++] = (byte) (green * shade / 255);
                raw[index++] = (byte) (blue * shade / 255);
                raw[index++] = opaque || x < half ? (byte) 255 : (byte) (255 - 255 * (x - half) / (pictureSize - half - 1));
            }
        }

        using var output = new MemoryStream();
        output.Write([0x89, (byte) 'P', (byte) 'N', (byte) 'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, pictureSize);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), pictureSize);
        // Eight bits a channel, truecolour with alpha, and none of the three optional encodings.
        header[8] = 8;
        header[9] = 6;
        Chunk(output, "IHDR", header);
        Chunk(output, "IDAT", Compress(raw));
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    static byte[] Compress(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        return output.ToArray();
    }

    static void Chunk(Stream output, string name, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);

        // The name and the data are one run for the checksum, which covers both and not the length.
        var payload = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(name).CopyTo(payload, 0);
        data.CopyTo(payload, 4);
        output.Write(payload);

        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, Crc(payload));
        output.Write(checksum);
    }

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
