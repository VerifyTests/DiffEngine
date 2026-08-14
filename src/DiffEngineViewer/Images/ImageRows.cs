using System.Globalization;

/// <summary>
/// Turns two image sides into two equal length row lists, the same shape
/// <see cref="DiffRows"/> produces for text, so an image comparison lays out through the machinery
/// every head already has rather than through anything new.
/// <para>
/// One row per property, each carrying its own side's value and coloured by how it stands against
/// the other: a matching format reads as unchanged and a differing size as modified, which is the
/// same vocabulary a line of text gets. A side with no file at all is filler the whole way down,
/// the way an empty text side is.
/// </para>
/// </summary>
static class ImageRows
{
    /// <summary>
    /// Wide enough for the longest label plus a gap.
    /// </summary>
    const int labelWidth = 12;

    public static (IReadOnlyList<Row> Left, IReadOnlyList<Row> Right) Build(ImageFile? left, ImageFile? right)
    {
        var leftRows = new List<Row>(3);
        var rightRows = new List<Row>(3);
        Add(leftRows, rightRows, "format", Format(left), Format(right));
        Add(leftRows, rightRows, "dimensions", Dimensions(left), Dimensions(right));
        Add(leftRows, rightRows, "bytes", Bytes(left), Bytes(right));
        return (leftRows, rightRows);
    }

    static void Add(List<Row> left, List<Row> right, string label, string? leftValue, string? rightValue)
    {
        // Numbered rather than left without a number. Filler is the only row the shim reads a
        // missing number for; a numberless row of any other kind draws its gutter differently on
        // each of the three heads, and these are rows, so numbering them costs nothing.
        var number = left.Count + 1;
        left.Add(Cell(number, label, leftValue, rightValue, RowKind.Added));
        right.Add(Cell(number, label, rightValue, leftValue, RowKind.Removed));
    }

    /// <param name="only">
    /// What this side is when the other has no file: added on the received side, removed on the
    /// expected one, matching which way round <see cref="DiffRows"/> reads.
    /// </param>
    static Row Cell(int number, string label, string? value, string? other, RowKind only)
    {
        if (value is null)
        {
            return new(null, RowKind.Filler, "");
        }

        RowKind kind;
        if (other is null)
        {
            kind = only;
        }
        else
        {
            kind = value == other ? RowKind.Unchanged : RowKind.Modified;
        }

        return new(number, kind, $"{label,-labelWidth}{value}");
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
