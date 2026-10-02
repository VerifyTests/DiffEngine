using System.Globalization;

/// <summary>
/// What a document side is when its pages are what is being looked at, or before its text has
/// been read: the <see cref="ImageRows"/> of documents, one <see cref="PropertyRows"/> row per
/// property.
/// <para>
/// Two rows, which is also the fewest the Linux head can place a picture under: it measures the
/// row pitch from the first two rows of the table before it draws one.
/// </para>
/// </summary>
static class DocumentRows
{
    public static (IReadOnlyList<Row> Left, IReadOnlyList<Row> Right) Build(DocumentFile? left, DocumentFile? right)
    {
        var leftRows = new List<Row>(2);
        var rightRows = new List<Row>(2);
        PropertyRows.Add(leftRows, rightRows, "format", Format(left), Format(right));
        PropertyRows.Add(leftRows, rightRows, "bytes", Bytes(left), Bytes(right));
        return (leftRows, rightRows);
    }

    static string? Format(DocumentFile? document) =>
        document is { } file ? DocumentFile.Name(file.Format) : null;

    static string? Bytes(DocumentFile? document)
    {
        if (document is not { } file)
        {
            return null;
        }

        // As for images: no hash means the bytes never arrived, so a zero length says nothing.
        if (file.Hash is null)
        {
            return "unreadable";
        }

        return file.Length.ToString("N0", CultureInfo.InvariantCulture);
    }
}
