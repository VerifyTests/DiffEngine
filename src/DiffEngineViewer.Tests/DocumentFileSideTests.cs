/// <summary>
/// How a document file is read, which turns on whether the viewer has its documents folder: the
/// dotnet tool and the tray's copy do, the copy bundled in DiffEngine does not and must read these
/// files exactly as it always has.
/// </summary>
public class DocumentFileSideTests :
    IDisposable
{
    /// <summary>
    /// The bundled viewer's behaviour, pinned: a PDF is what it always was to it, text.
    /// </summary>
    [Test]
    public async Task WithoutTheFolderAPdfIsText()
    {
        var path = Write("sample.pdf", "%PDF-1.4 not really");
        var side = FileSide.Read(path);
        await Assert.That(side.Document).IsNull();
        await Assert.That(side.Text).IsEqualTo("%PDF-1.4 not really");
    }

    [Test]
    public async Task WithoutTheFolderAnSvgIsText()
    {
        var path = Write("logo.svg", "<svg/>");
        var side = FileSide.Read(path);
        await Assert.That(side.Document).IsNull();
        await Assert.That(side.Text).IsEqualTo("<svg/>");
    }

    /// <summary>
    /// The bytes and their hash, and nothing slow: the text is read later, off the thread a test
    /// process may be waiting on.
    /// </summary>
    [Test]
    public async Task APdfIsADocumentStillBeingRead()
    {
        var path = Write("sample.pdf", "%PDF-1.4 not really");
        var side = FileSide.Read(path, Plugin());
        await Assert.That(side.Text).IsEmpty();
        await Assert.That(side.Document).IsEqualTo(new DocumentFile(
            path,
            19,
            DocumentFormat.Pdf,
            Convert.ToHexString(SHA256.HashData("%PDF-1.4 not really"u8.ToArray())),
            Reading: true));
    }

    /// <summary>
    /// Text already read for the same bytes - a re-run that wrote the same document, or the other
    /// side of the pair - is used at once.
    /// </summary>
    [Test]
    public async Task TextAlreadyReadIsUsed()
    {
        var path = Write("sample.docx", "not really a docx");
        var documents = Plugin();
        var hash = FileSide.Read(path, documents).Document!.Value.Hash!;
        documents.Remember(hash, new("# Heading", null));

        var side = FileSide.Read(path, documents);
        await Assert.That(side.Text).IsEqualTo("# Heading");
        await Assert.That(side.Document!.Value.HasText).IsTrue();
    }

    [Test]
    public async Task TextThatCouldNotBeReadSaysWhy()
    {
        var path = Write("sample.xlsx", "not really an xlsx");
        var documents = Plugin();
        var hash = FileSide.Read(path, documents).Document!.Value.Hash!;
        documents.Remember(hash, new(null, "it is encrypted."));

        var side = FileSide.Read(path, documents);
        await Assert.That(side.Document!.Value.Unreadable).IsEqualTo("it is encrypted.");
        await Assert.That(side.Document!.Value.HasText).IsFalse();
    }

    /// <summary>
    /// An SVG's text is the file, read now as any text file is; only its picture waits.
    /// </summary>
    [Test]
    public async Task AnSvgIsTextThatDraws()
    {
        var path = Write("logo.svg", "﻿<svg/>");
        var side = FileSide.Read(path, Plugin());
        await Assert.That(side.Text).IsEqualTo("<svg/>");
        await Assert.That(side.Document!.Value.Format).IsEqualTo(DocumentFormat.Svg);
        await Assert.That(side.Document!.Value.HasText).IsTrue();
    }

    /// <summary>
    /// A map that is text is read as an SVG is: the file now, one picture later.
    /// </summary>
    [Test]
    public async Task AGeoJsonMapIsTextThatDraws()
    {
        var path = Write("route.geojson", """{"type":"FeatureCollection","features":[]}""");
        var side = FileSide.Read(path, Plugin());
        await Assert.That(side.Text).IsEqualTo("""{"type":"FeatureCollection","features":[]}""");
        var document = side.Document!.Value;
        await Assert.That(document.Format).IsEqualTo(DocumentFormat.GeoJson);
        await Assert.That(document.HasText).IsTrue();
        await Assert.That(document.IsDrawn).IsTrue();
    }

    /// <summary>
    /// A binary map has its text read out of it, as GeoJSON, as a PDF has; but it is one picture
    /// rather than pages.
    /// </summary>
    [Test]
    public async Task AFlatGeobufMapIsADocumentStillBeingRead()
    {
        var path = Write("route.fgb", "not really a fgb");
        var side = FileSide.Read(path, Plugin());
        await Assert.That(side.Text).IsEmpty();
        var document = side.Document!.Value;
        await Assert.That(document.Format).IsEqualTo(DocumentFormat.FlatGeobuf);
        await Assert.That(document.Reading).IsTrue();
        await Assert.That(document.IsDrawn).IsTrue();
    }

    /// <summary>
    /// Pictures are still pictures, and text still text.
    /// </summary>
    [Test]
    public async Task OtherFilesAreUntouched()
    {
        var documents = Plugin();
        await Assert.That(FileSide.Read(Write("sample.txt", "text"), documents).Document).IsNull();
        await Assert.That(FileSide.Read(Write("sample.png", "not really a png"), documents).Image).IsNotNull();
    }

    /// <summary>
    /// A brand new snapshot has nothing on its expected side, which is an empty side rather than an
    /// unreadable document.
    /// </summary>
    [Test]
    public async Task AMissingFileIsNothingYet()
    {
        var side = FileSide.Read(Path.Combine(directory, "missing.pdf"), Plugin());
        await Assert.That(side.Document).IsNull();
        await Assert.That(side.Warning).IsNull();
    }

    DocumentPlugin Plugin()
    {
        var documents = new DocumentPlugin(
            static _ => throw new("Not read in these tests."),
            static (_, _, _) => throw new("Not drawn in these tests."));
        disposables.Add(documents);
        return documents;
    }

    string Write(string name, string content)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    readonly string directory = Directory.CreateTempSubdirectory("deview-document-sides-").FullName;
    readonly List<IDisposable> disposables = [];

    public void Dispose()
    {
        foreach (var disposable in disposables)
        {
            disposable.Dispose();
        }

        Directory.Delete(directory, true);
    }
}
