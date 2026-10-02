using System.Globalization;

/// <summary>
/// Turns two image sides into two equal length row lists, the same shape
/// <see cref="DiffRows"/> produces for text, so an image comparison lays out through the machinery
/// every head already has rather than through anything new: one <see cref="PropertyRows"/> row per
/// property.
/// </summary>
static class ImageRows
{
    public static (IReadOnlyList<Row> Left, IReadOnlyList<Row> Right) Build(ImageFile? left, ImageFile? right)
    {
        var leftRows = new List<Row>(3);
        var rightRows = new List<Row>(3);
        PropertyRows.Add(leftRows, rightRows, "format", Format(left), Format(right));
        PropertyRows.Add(leftRows, rightRows, "dimensions", Dimensions(left), Dimensions(right));
        PropertyRows.Add(leftRows, rightRows, "bytes", Bytes(left), Bytes(right));
        return (leftRows, rightRows);
    }

    static string? Format(ImageFile? image)
    {
        if (image is not { } file)
        {
            return null;
        }

        if (file.Header is not { } header)
        {
            return "not recognized";
        }

        return Name(header.Format);
    }

    static string Name(ImageFormat format) =>
        format switch
        {
            ImageFormat.Png => "PNG",
            ImageFormat.Jpeg => "JPEG",
            ImageFormat.Gif => "GIF",
            ImageFormat.Bmp => "BMP",
            ImageFormat.Webp => "WebP",
            _ => "ICO"
        };

    static string? Dimensions(ImageFile? image)
    {
        if (image is not { } file)
        {
            return null;
        }

        if (file.Header is not { HasSize: true } header)
        {
            return "unknown";
        }

        return $"{header.Width} x {header.Height}";
    }

    static string? Bytes(ImageFile? image)
    {
        if (image is not { } file)
        {
            return null;
        }

        // No hash means the bytes never arrived, so the length is zero because nothing was read
        // rather than because the file is empty.
        if (file.Hash is null)
        {
            return "unreadable";
        }

        // Invariant, so the snapshots do not depend on the machine's group separator.
        return file.Length.ToString("N0", CultureInfo.InvariantCulture);
    }
}
