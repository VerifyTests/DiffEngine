/// <summary>
/// A real PDF built by hand: one line of text per page, so a test knows exactly what each page
/// says, and so which page of two documents differs.
/// <para>
/// Written out rather than committed, for the reason <see cref="SamplePng"/> is: a fixture whose
/// every byte is a function of its arguments says what it is in the test that uses it.
/// </para>
/// </summary>
static class SamplePdf
{
    public static byte[] Build(params string[] pages)
    {
        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();

        void Add(string body)
        {
            // ASCII throughout, so a character offset is a byte offset.
            offsets.Add(builder.Length);
            builder.Append($"{offsets.Count} 0 obj\n{body}\nendobj\n");
        }

        var kids = string.Join(" ", pages.Select((_, index) => $"{4 + index * 2} 0 R"));
        Add("<< /Type /Catalog /Pages 2 0 R >>");
        Add($"<< /Type /Pages /Kids [{kids}] /Count {pages.Length} >>");
        Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        foreach (var page in pages)
        {
            var content = $"BT /F1 24 Tf 40 100 Td ({page}) Tj ET";
            Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Resources << /Font << /F1 3 0 R >> >> /Contents {offsets.Count + 2} 0 R >>");
            Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        }

        var table = builder.Length;
        builder.Append($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            builder.Append($"{offset:D10} 00000 n \n");
        }

        builder.Append($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{table}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}
