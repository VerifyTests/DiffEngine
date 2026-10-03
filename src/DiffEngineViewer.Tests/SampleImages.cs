using SkiaSharp;

/// <summary>
/// A solid colour in every image format the viewer compares, for <see cref="FileTypeLaunchTests"/>
/// to put in front of a person: whether each head draws a format is exactly what it is there to
/// show.
/// <para>
/// BMP, ICO and GIF are written by hand, as <see cref="SamplePng"/> is. JPEG and WebP go through
/// Skia, since a hand written encoder for either is a project of its own, and nothing here is a
/// baseline whose bytes have to hold still.
/// </para>
/// </summary>
static class SampleImages
{
    const int width = 240;
    const int height = 160;

    public static byte[] Build(string extension, byte red, byte green, byte blue) =>
        extension switch
        {
            ".png" => SamplePng.Build(width, height, red, green, blue),
            ".bmp" => Bmp(red, green, blue),
            ".ico" => Ico(red, green, blue),
            ".gif" => Gif(red, green, blue),
            ".jpg" or ".jpeg" => Skia(SKEncodedImageFormat.Jpeg, red, green, blue),
            ".webp" => Skia(SKEncodedImageFormat.Webp, red, green, blue),
            _ => throw new ArgumentException($"No sample image for {extension}.", nameof(extension))
        };

    /// <summary>
    /// 24 bits a pixel, uncompressed, rows bottom up and each padded to four bytes.
    /// </summary>
    static byte[] Bmp(byte red, byte green, byte blue)
    {
        var stride = (width * 3 + 3) / 4 * 4;
        var pixels = stride * height;
        var bytes = new byte[54 + pixels];
        bytes[0] = (byte) 'B';
        bytes[1] = (byte) 'M';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), height);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28), 24);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(34), pixels);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = 54 + y * stride + x * 3;
                bytes[index] = blue;
                bytes[index + 1] = green;
                bytes[index + 2] = red;
            }
        }

        return bytes;
    }

    /// <summary>
    /// One 64 pixel PNG in an icon directory, which every reader since Vista accepts.
    /// </summary>
    static byte[] Ico(byte red, byte green, byte blue)
    {
        const int size = 64;
        var png = SamplePng.Build(size, size, red, green, blue);
        var bytes = new byte[22 + png.Length];
        // Reserved, then type 1 (icon), then one image.
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(4), 1);
        bytes[6] = size;
        bytes[7] = size;
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(10), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(12), 32);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), png.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), 22);
        png.CopyTo(bytes, 22);
        return bytes;
    }

    /// <summary>
    /// A GIF whose LZW stream compresses nothing. With a minimum code size of 7, every code is 8
    /// bits wide until the table reaches 256 entries, so a clear code every 100 pixels keeps each
    /// code exactly one byte: the pixel's palette index, which is always 0.
    /// </summary>
    static byte[] Gif(byte red, byte green, byte blue)
    {
        const byte clear = 128;
        const byte end = 129;
        var bytes = new List<byte>();
        bytes.AddRange("GIF89a"u8);
        AddInt16(bytes, width);
        AddInt16(bytes, height);
        // A global table of 128 colours, at 7 bits of resolution.
        bytes.Add(0xE6);
        bytes.Add(0);
        bytes.Add(0);
        var palette = new byte[128 * 3];
        palette[0] = red;
        palette[1] = green;
        palette[2] = blue;
        bytes.AddRange(palette);

        bytes.Add(0x2C);
        AddInt16(bytes, 0);
        AddInt16(bytes, 0);
        AddInt16(bytes, width);
        AddInt16(bytes, height);
        bytes.Add(0);

        var codes = new List<byte>();
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            if (pixel % 100 == 0)
            {
                codes.Add(clear);
            }

            codes.Add(0);
        }

        codes.Add(end);

        bytes.Add(7);
        for (var start = 0; start < codes.Count; start += 255)
        {
            var count = Math.Min(255, codes.Count - start);
            bytes.Add((byte) count);
            bytes.AddRange(codes.GetRange(start, count));
        }

        bytes.Add(0);
        bytes.Add(0x3B);
        return [.. bytes];
    }

    static void AddInt16(List<byte> bytes, int value)
    {
        bytes.Add((byte) value);
        bytes.Add((byte) (value >> 8));
    }

    static byte[] Skia(SKEncodedImageFormat format, byte red, byte green, byte blue)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(new(red, green, blue));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    /// <summary>
    /// A JPEG the size of a photograph, 4000 by 3000: big enough that decoding it and scaling it to
    /// a pane take long enough to see. A gradient out from the colour rather than the colour alone,
    /// which JPEG would reduce to almost nothing to decode.
    /// </summary>
    public static byte[] Photo(byte red, byte green, byte blue)
    {
        const int photoWidth = 4000;
        const int photoHeight = 3000;
        using var bitmap = new SKBitmap(photoWidth, photoHeight);
        using (var canvas = new SKCanvas(bitmap))
        using (var shader = SKShader.CreateRadialGradient(
                   new(photoWidth / 2f, photoHeight / 2f),
                   photoWidth / 2f,
                   [new(red, green, blue), SKColors.Black],
                   SKShaderTileMode.Clamp))
        using (var paint = new SKPaint())
        {
            paint.Shader = shader;
            canvas.DrawRect(0, 0, photoWidth, photoHeight, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }
}
