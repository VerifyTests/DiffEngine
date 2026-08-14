using System.Buffers.Binary;

/// <summary>
/// The format and pixel size of an image, read from its leading bytes without decoding it.
/// <para>
/// Hand rolled rather than System.Drawing, because this assembly is the renderer independent half
/// of the viewer and runs on macOS and Linux, where System.Drawing.Common does not. Only the
/// Windows head has a decoder, and the screen model may not describe an image differently
/// depending on which head is about to draw it.
/// </para>
/// <para>
/// A format may be recognized without its size being readable — a JPEG whose frame header sits
/// past the bytes handed in, for instance — so <see cref="HasSize"/> is a separate question from
/// whether this was recognized at all.
/// </para>
/// </summary>
readonly record struct ImageHeader(ImageFormat Format, int Width, int Height)
{
    public bool HasSize =>
        Width > 0 &&
        Height > 0;

    public static bool TryRead(ReadOnlySpan<byte> bytes, out ImageHeader header) =>
        TryPng(bytes, out header) ||
        TryJpeg(bytes, out header) ||
        TryGif(bytes, out header) ||
        TryBmp(bytes, out header) ||
        TryWebp(bytes, out header) ||
        TryIco(bytes, out header);

    static bool TryPng(ReadOnlySpan<byte> bytes, out ImageHeader header)
    {
        header = default;
        ReadOnlySpan<byte> signature = [0x89, (byte) 'P', (byte) 'N', (byte) 'G', 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes.Length < 24 ||
            !bytes[..8].SequenceEqual(signature))
        {
            return false;
        }

        // IHDR is required to be the first chunk, so the size is at a fixed offset rather than
        // somewhere in a chunk walk.
        header = new(
            ImageFormat.Png,
            BinaryPrimitives.ReadInt32BigEndian(bytes[16..]),
            BinaryPrimitives.ReadInt32BigEndian(bytes[20..]));
        return true;
    }

    /// <summary>
    /// The one format that has to be walked: the frame header giving the size follows however many
    /// metadata segments the encoder chose to write, and Exif thumbnails routinely push it past the
    /// first few hundred bytes.
    /// </summary>
    static bool TryJpeg(ReadOnlySpan<byte> bytes, out ImageHeader header)
    {
        header = default;
        if (bytes.Length < 4 ||
            bytes[0] != 0xFF ||
            bytes[1] != 0xD8)
        {
            return false;
        }

        // Recognized from the two byte signature, so a truncated or unwalkable file is still
        // reported as a JPEG of unknown size rather than as not an image.
        header = new(ImageFormat.Jpeg, 0, 0);
        var index = 2;
        while (index + 1 < bytes.Length)
        {
            if (bytes[index] != 0xFF)
            {
                return true;
            }

            var marker = bytes[index + 1];
            index += 2;

            // Fill bytes before a marker, and the standalone markers, carry no length to skip.
            if (marker == 0xFF)
            {
                index--;
                continue;
            }

            if (marker is 0x01 or >= 0xD0 and <= 0xD9)
            {
                continue;
            }

            if (index + 1 >= bytes.Length)
            {
                return true;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[index..]);
            if (IsStartOfFrame(marker))
            {
                if (index + 6 >= bytes.Length)
                {
                    return true;
                }

                // Two bytes of length, one of sample precision, then height before width.
                header = new(
                    ImageFormat.Jpeg,
                    BinaryPrimitives.ReadUInt16BigEndian(bytes[(index + 5)..]),
                    BinaryPrimitives.ReadUInt16BigEndian(bytes[(index + 3)..]));
                return true;
            }

            // A length shorter than the field itself would not advance, so the walk would not end.
            if (length < 2)
            {
                return true;
            }

            index += length;
        }

        return true;
    }

    /// <summary>
    /// C0 through CF are the frame headers, apart from the huffman table, the arithmetic coding
    /// table, and the reserved marker between them.
    /// </summary>
    static bool IsStartOfFrame(byte marker) =>
        marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;

    static bool TryGif(ReadOnlySpan<byte> bytes, out ImageHeader header)
    {
        header = default;
        if (bytes.Length < 10 ||
            !bytes[..4].SequenceEqual("GIF8"u8) ||
            bytes[4] is not ((byte) '7' or (byte) '9') ||
            bytes[5] != (byte) 'a')
        {
            return false;
        }

        header = new(
            ImageFormat.Gif,
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]));
        return true;
    }

    static bool TryBmp(ReadOnlySpan<byte> bytes, out ImageHeader header)
    {
        header = default;
        if (bytes.Length < 22 ||
            bytes[0] != (byte) 'B' ||
            bytes[1] != (byte) 'M')
        {
            return false;
        }

        // The original OS/2 header is the only one with sixteen bit dimensions. Every later one
        // starts with the same thirty-two bit pair, whatever else it goes on to add.
        if (BinaryPrimitives.ReadInt32LittleEndian(bytes[14..]) == 12)
        {
            header = new(
                ImageFormat.Bmp,
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[18..]),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[20..]));
            return true;
        }

        if (bytes.Length < 26)
        {
            header = new(ImageFormat.Bmp, 0, 0);
            return true;
        }

        // A negative height means the rows are stored top down, which is not something the size
        // should report.
        header = new(
            ImageFormat.Bmp,
            BinaryPrimitives.ReadInt32LittleEndian(bytes[18..]),
            Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(bytes[22..])));
        return true;
    }

    static bool TryWebp(ReadOnlySpan<byte> bytes, out ImageHeader header)
    {
        header = default;
        if (bytes.Length < 16 ||
            !bytes[..4].SequenceEqual("RIFF"u8) ||
            !bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return false;
        }

        var chunk = bytes[12..16];

        // Recognized from the container, so a chunk this does not read still reports a WebP of
        // unknown size rather than falling through to "not an image".
        header = new(ImageFormat.Webp, 0, 0);

        // Lossless: a signature byte, then two fourteen bit fields packed one short of their size.
        if (chunk.SequenceEqual("VP8L"u8))
        {
            if (bytes.Length >= 25 &&
                bytes[20] == 0x2F)
            {
                header = Packed(BinaryPrimitives.ReadUInt32LittleEndian(bytes[21..]));
            }

            return true;
        }

        // Extended: an animation, an alpha channel or metadata, whose canvas size stands in for the
        // size of the frames inside it.
        if (chunk.SequenceEqual("VP8X"u8))
        {
            if (bytes.Length >= 30)
            {
                header = new(
                    ImageFormat.Webp,
                    ThreeByte(bytes[24..]) + 1,
                    ThreeByte(bytes[27..]) + 1);
            }

            return true;
        }

        if (!chunk.SequenceEqual("VP8 "u8))
        {
            return true;
        }

        // Lossy: the size follows the keyframe start code, and the top two bits of each field are
        // a scaling hint rather than part of the number.
        ReadOnlySpan<byte> startCode = [0x9D, 0x01, 0x2A];
        if (bytes.Length < 30 ||
            !bytes[23..26].SequenceEqual(startCode))
        {
            return true;
        }

        header = new(
            ImageFormat.Webp,
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[26..]) & 0x3FFF,
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[28..]) & 0x3FFF);
        return true;
    }

    static ImageHeader Packed(uint bits) =>
        new(
            ImageFormat.Webp,
            (int) (bits & 0x3FFF) + 1,
            (int) ((bits >> 14) & 0x3FFF) + 1);

    static int ThreeByte(ReadOnlySpan<byte> bytes) =>
        bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);

    /// <summary>
    /// An icon is a container, so this is the size of its first image. Zero in either byte means
    /// 256, which is what the field could not hold once icons outgrew it.
    /// </summary>
    static bool TryIco(ReadOnlySpan<byte> bytes, out ImageHeader header)
    {
        header = default;
        if (bytes.Length < 8 ||
            bytes[0] != 0 ||
            bytes[1] != 0 ||
            bytes[2] != 1 ||
            bytes[3] != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]) == 0)
        {
            return false;
        }

        header = new(
            ImageFormat.Ico,
            bytes[6] == 0 ? 256 : bytes[6],
            bytes[7] == 0 ? 256 : bytes[7]);
        return true;
    }
}
