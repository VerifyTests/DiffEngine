#nullable enable
using System.IO.Compression;
using System.Text;
using BenchmarkDotNet.Attributes;

namespace DiffEngineViewer.Benchmarks;

// A pair of documents opened in the viewer, received beside verified, timed to the two things a
// reader waits for: the right side's first page, and both sides drawn, which is when every page
// that differs is known.
//
// Through DocumentWatch itself, a job at a time as its tests drive it, and with the documents
// folder a viewer loads rather than a stand-in for it: PDFium for the PDFs, one lock and all, and
// Morph for the Word files. So this times the renderers as well as the order they are asked in,
// and a Word file's pages all land together at the end, which makes its two rows one number.
//
// Nothing here is a viewer's own. The pair is written to a temp directory, and the copies and the
// pages go to the plugin's cache, which is a temp directory this process makes for itself. Both
// are deleted when a benchmark is done.
[MemoryDiagnoser]
public class DocumentWatchBenchmarks
{
    [Params(".pdf", ".docx")]
    public string Extension = "";

    [Params(10, 50)]
    public int Pages;

    DocumentPlugin renderer = null!;
    DocumentPlugin documents = null!;
    string directory = "";
    string left = "";
    string right = "";
    volatile SessionHost? host;
    volatile bool untilTheRightSideHasAPage;

    [GlobalSetup]
    public void Setup()
    {
        renderer = DocumentPlugin.Find() ??
                   throw new InvalidOperationException("There is no documents folder beside the benchmarks. DiffEngineViewer.Benchmarks.csproj imports Documents.targets to put one there.");
        // The renderer a viewer loads, behind one more delegate. See Render.
        documents = new(renderer.Text, Render);
        directory = Directory.CreateTempSubdirectory("deview-benchmark-documents-").FullName;
        left = Write("sample.received", changed: true);
        right = Write("sample.verified", changed: false);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // The cache goes with the plugin that made it
        documents.Dispose();
        renderer.Dispose();
        Directory.Delete(directory, true);
    }

    // How long the right pane has nothing but a spinner in it.
    [Benchmark]
    public int RightSidesFirstPage()
    {
        var state = Open(untilTheRightSideHasAPage: true);
        var drawn = Drawn(state, state.Current!.RightDocument);
        if (drawn == 0)
        {
            throw new InvalidOperationException("The right side drew no page.");
        }

        return drawn;
    }

    // How long until the status line can say which pages differ.
    [Benchmark]
    public int BothSidesComplete()
    {
        var state = Open(untilTheRightSideHasAPage: false);
        var entry = state.Current!;
        var (leftPages, rightPages) = DocumentPages.Of(state, entry);
        if (leftPages is not { Complete: true, Failure: null } ||
            rightPages is not { Complete: true, Failure: null } ||
            leftPages.Pages.Count != Pages ||
            rightPages.Pages.Count != Pages)
        {
            throw new InvalidOperationException($"The pair did not draw as {Pages} pages a side: {Describe(leftPages)} and {Describe(rightPages)}.");
        }

        return DocumentPages.Differing(entry, leftPages, rightPages).Count;
    }

    // The pair as it arrives, hashed and with its text unread, and then the watch until it has
    // nothing left to do.
    SessionState Open(bool untilTheRightSideHasAPage)
    {
        // Nothing kept from the run before. The text, the copies and the pages all go, as they do
        // when a pair leaves the queue.
        documents.Keep(new HashSet<string>());
        this.untilTheRightSideHasAPage = untilTheRightSideHasAPage;
        var entry = QueueEntry.ForFiles(left, right, FileSide.Read(left, documents), FileSide.Read(right, documents));
        var opened = new SessionHost(ViewerSession.EnqueueFile(SessionState.Start(ViewerMode.File, 160, 48), entry));
        host = opened;
        var watch = new DocumentWatch(opened, documents);
        while (watch.Pump())
        {
        }

        return opened.State;
    }

    // The renderer a viewer loads, with one thing added. A run that is timing the right side's
    // first page stops drawing once that page is in the state, by throwing from the next page to
    // land on either side. Nothing else stops a call into PDFium or Morph, and left to finish,
    // the pages after the one being timed would be most of what was measured.
    int Render(string path, string pages, string projection, Action<string> landed) =>
        renderer.Render(
            path,
            pages,
            _ =>
            {
                landed(_);
                if (untilTheRightSideHasAPage &&
                    host is { } opened &&
                    Drawn(opened.State, opened.State.Current?.RightDocument) > 0)
                {
                    throw new OperationCanceledException("The right side's first page has landed.");
                }
            },
            Enum.Parse<MapProjection>(projection));

    static int Drawn(SessionState state, DocumentFile? side) =>
        DocumentPages.Of(state, side)?.Pages.Count ?? 0;

    static string Describe(Rendering? rendering) =>
        rendering is null
            ? "not drawn"
            : $"{rendering.Pages.Count} pages{(rendering.Complete ? "" : ", still drawing")}{(rendering.Failure is null ? "" : $", {rendering.Failure}")}";

    string Write(string name, bool changed)
    {
        var path = Path.Combine(directory, $"{name}{Extension}");
        File.WriteAllBytes(path, Extension == ".pdf" ? Pdf(Pages, changed) : Docx(Pages, changed));
        return path;
    }

    // What one page says: a heading and a dozen lines, the same on both sides but for one page in
    // ten, which is the usual shape of a document snapshot that failed.
    static IEnumerable<string> Lines(int page, bool changed)
    {
        yield return $"Page {page + 1}";
        var ending = changed && page % 10 == 3
            ? "and this page has changed since it was verified."
            : "the quick brown fox jumps over the lazy dog.";
        for (var line = 1; line <= 12; line++)
        {
            yield return $"Line {line} of page {page + 1}: {ending}";
        }
    }

    // A PDF written out by hand, A4, a content stream per page.
    static byte[] Pdf(int pages, bool changed)
    {
        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();

        void Add(string body)
        {
            // ASCII throughout, so a character offset is a byte offset
            offsets.Add(builder.Length);
            builder.Append($"{offsets.Count} 0 obj\n{body}\nendobj\n");
        }

        var kids = string.Join(" ", Enumerable.Range(0, pages).Select(_ => $"{4 + _ * 2} 0 R"));
        Add("<< /Type /Catalog /Pages 2 0 R >>");
        Add($"<< /Type /Pages /Kids [{kids}] /Count {pages} >>");
        Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        for (var page = 0; page < pages; page++)
        {
            var text = string.Concat(Lines(page, changed).Select(_ => $"({_}) Tj T*\n"));
            var content = $"BT /F1 11 Tf 56 780 Td 16 TL\n{text}ET";
            Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {offsets.Count + 2} 0 R >>");
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

    // A Word document written out by hand, A4, a page break after each page's lines.
    static byte[] Docx(int pages, bool changed)
    {
        var body = new StringBuilder();
        for (var page = 0; page < pages; page++)
        {
            foreach (var line in Lines(page, changed))
            {
                body.Append($"<w:p><w:r><w:t>{line}</w:t></w:r></w:p>");
            }

            if (page < pages - 1)
            {
                body.Append("""<w:p><w:r><w:br w:type="page"/></w:r></w:p>""");
            }
        }

        body.Append("""<w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="708" w:footer="708" w:gutter="0"/></w:sectPr>""");

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Part(
                archive,
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """);
            Part(
                archive,
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            Part(
                archive,
                "word/document.xml",
                $"""
                 <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                 <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>{body}</w:body></w:document>
                 """);
        }

        return stream.ToArray();
    }

    static void Part(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(content);
    }
}
