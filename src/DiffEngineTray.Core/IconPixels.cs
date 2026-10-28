using System.Buffers.Binary;

/// <summary>
/// A tray icon as a StatusNotifierItem carries it: ARGB32, a row at a time from the top, each
/// pixel's bytes in network order. Read out of the icon's <c>.ico</c>, whose one picture is a 48
/// pixel, 32 bit bitmap: BGRA rows from the bottom, which needs no decoder, where the PNG beside it
/// would, and System.Drawing is Windows'.
/// </summary>
static class IconPixels
{
    /// <summary>
    /// The picture at <paramref name="size" />, or null where the icon has none, or has it as a PNG
    /// or with fewer bits.
    /// </summary>
    public static byte[]? FromIco(byte[] ico, int size)
    {
        // An ICONDIR, then an ICONDIRENTRY a picture
        var count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + i * 16;
            var width = ico[entry] == 0 ? 256 : ico[entry];
            if (width != size)
            {
                continue;
            }

            var offset = BinaryPrimitives.ReadInt32LittleEndian(ico.AsSpan(entry + 12));
            // A BITMAPINFOHEADER, whose height counts the mask under the colours as well
            var header = BinaryPrimitives.ReadInt32LittleEndian(ico.AsSpan(offset));
            var bits = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(offset + 14));
            if (header != 40 ||
                bits != 32)
            {
                return null;
            }

            return Pixels(ico, offset + header, size);
        }

        return null;
    }

    static byte[] Pixels(byte[] ico, int start, int size)
    {
        var pixels = new byte[size * size * 4];
        for (var row = 0; row < size; row++)
        {
            var from = start + (size - 1 - row) * size * 4;
            for (var column = 0; column < size; column++)
            {
                var source = from + column * 4;
                var target = (row * size + column) * 4;
                pixels[target] = ico[source + 3];
                pixels[target + 1] = ico[source + 2];
                pixels[target + 2] = ico[source + 1];
                pixels[target + 3] = ico[source];
            }
        }

        return pixels;
    }
}
