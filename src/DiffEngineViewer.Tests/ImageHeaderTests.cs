/// <summary>
/// What the sniffer makes of each format's leading bytes.
/// <para>
/// The bytes are built here rather than committed as image files. The point of the sniffer is that
/// it reads fixed offsets instead of running a decoder, so a fixture holding real pixels would
/// exercise nothing extra and would hide the one thing under test — which byte says what — inside a
/// binary blob nobody can review.
/// </para>
/// </summary>
public class ImageHeaderTests
{
    [Test]
    public Task Formats() =>
        Verify(
            new
            {
                Png = Read(Png(800, 600)),
                Jpeg = Read(Jpeg(1920, 1080)),
                Gif = Read(Gif(320, 240)),
                // Stored top down, so the height on disk is negative and the size is not.
                Bmp = Read(Bmp(64, 48)),
                WebpLossy = Read(WebpLossy(500, 400)),
                WebpLossless = Read(WebpLossless(500, 400)),
                Ico = Read(Ico(32, 32)),
                // Zero is how the field says 256, which is the size icons outgrew it at.
                LargeIco = Read(Ico(256, 256))
            });

    /// <summary>
    /// Recognizing the format and reading the size are separate answers, and something that is
    /// plainly an image has to keep the first when it cannot give the second. Otherwise a truncated
    /// or unusual file falls all the way back to being rendered as text.
    /// </summary>
    [Test]
    public Task SizeUnknown() =>
        Verify(
            new
            {
                // The signature, and then nothing to walk to the frame header.
                TruncatedJpeg = Read([0xFF, 0xD8, 0xFF, 0xE0]),
                // A container holding a chunk this does not read. Still a WebP.
                UnknownWebpChunk = Read(Webp("ANIM")),
                BmpMissingItsSize = Read(Bmp(64, 48)[..22])
            });

    [Test]
    public Task NotImages() =>
        Verify(
            new
            {
                Empty = Read([]),
                Text = Read("the quick brown fox"u8.ToArray()),
                // The right length and the wrong first byte, which is the case a length-only guard
                // would wave through.
                AlmostPng = Read(Broken())
            });

    static object Read(byte[] bytes) =>
        ImageHeader.TryRead(bytes, out var header) ? header : "not an image";

    static byte[] Broken()
    {
        var bytes = Png(800, 600);
        bytes[0] = 0x88;
        return bytes;
    }

    static byte[] Png(int width, int height)
    {
        var bytes = new byte[24];
        ReadOnlySpan<byte> signature = [0x89, (byte) 'P', (byte) 'N', (byte) 'G', 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    /// <summary>
    /// With a metadata segment in front of the frame header, because stepping over one is the whole
    /// reason this format is walked rather than read at an offset.
    /// </summary>
    static byte[] Jpeg(int width, int height)
    {
        List<byte> bytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
        bytes.AddRange("JFIF\0"u8);
        bytes.AddRange(new byte[9]);
        bytes.AddRange([0xFF, 0xC0, 0x00, 0x11, 0x08]);
        // Height before width, which is the pair this format stores the other way round.
        bytes.AddRange([(byte) (height >> 8), (byte) height, (byte) (width >> 8), (byte) width]);
        bytes.AddRange(new byte[6]);
        return [.. bytes];
    }

    static byte[] Gif(int width, int height)
    {
        var bytes = new byte[13];
        "GIF89a"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), (ushort) width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), (ushort) height);
        return bytes;
    }

    static byte[] Bmp(int width, int height)
    {
        var bytes = new byte[54];
        "BM"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), -height);
        return bytes;
    }

    static byte[] Webp(string chunk)
    {
        var bytes = new byte[30];
        "RIFF"u8.CopyTo(bytes);
        "WEBP"u8.CopyTo(bytes.AsSpan(8));
        Encoding.ASCII.GetBytes(chunk).CopyTo(bytes.AsSpan(12));
        return bytes;
    }

    static byte[] WebpLossy(int width, int height)
    {
        var bytes = Webp("VP8 ");
        bytes[23] = 0x9D;
        bytes[24] = 0x01;
        bytes[25] = 0x2A;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(26), (ushort) width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28), (ushort) height);
        return bytes;
    }

    static byte[] WebpLossless(int width, int height)
    {
        var bytes = Webp("VP8L");
        bytes[20] = 0x2F;
        // Two fourteen bit fields, each one short of the real size.
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(21),
            (uint) (width - 1) | ((uint) (height - 1) << 14));
        return bytes;
    }

    static byte[] Ico(int width, int height)
    {
        var bytes = new byte[22];
        bytes[2] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), 1);
        bytes[6] = (byte) (width == 256 ? 0 : width);
        bytes[7] = (byte) (height == 256 ? 0 : height);
        return bytes;
    }
}
