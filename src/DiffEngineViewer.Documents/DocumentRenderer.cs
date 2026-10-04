using System.IO.Compression;
using GeoConvert;
using Morph;
using Morph.PDFium;
using SkiaSharp;
using Svg;
using Svg.Model;
using Svg.Skia;

namespace DiffEngineViewer.Documents;

/// <summary>
/// What DiffEngineViewer reads documents with: the text of a PDF, an Office document or a binary
/// map, and every page of one, or an SVG or a map, as a picture.
/// <para>
/// Loaded by DiffEngineViewer.Core into an AssemblyLoadContext of its own and reached by name
/// rather than by reference, so only BCL types cross: nothing here knows Core exists, and no type
/// has to be the same type in two load contexts.
/// </para>
/// <para>
/// Every path handed in is the viewer's own copy of a snapshot, never the snapshot itself. Morph
/// opens what it is given with read sharing only, and a handle held on a received file for the
/// length of a conversion is a handle that refuses the accept moving it.
/// </para>
/// </summary>
public static class DocumentRenderer
{
    /// <summary>
    /// Enough to read body text in a maximised pane, which is the most a page is ever shown at: the
    /// heads fit a picture to the pane and never enlarge it past its own size.
    /// </summary>
    const int dpi = 150;

    /// <summary>
    /// PDFium renders into a width x height x 4 buffer before encoding, so a poster sized page at
    /// <see cref="dpi"/> would take hundreds of MB. Such a page is drawn smaller instead.
    /// </summary>
    const double longestPage = 4096;

    static MarkdownExportOptions markdown = new()
    {
        // Inlined as base64 otherwise: one line per picture, every character of it a change.
        ImageHandler = _ => $"image-{_.Index}"
    };

    static DocumentRenderer()
    {
        // A snapshot is test output, so nothing in one gets to reach the network or the disk beyond
        // the file itself. All of these resolve by default.
        SvgDocument.ResolveExternalImages = ExternalType.None;
        SvgDocument.ResolveExternalElements = ExternalType.None;
        SvgDocument.ResolveExternalXmlEntites = ExternalType.None;
        SvgDocument.DisableDtdProcessing = true;
    }

    /// <summary>
    /// A PDF's text page by page, each under a <c>--- page N ---</c> line, or an Office document as
    /// Markdown. Throws when the file is not what its extension says, with a message that says so
    /// in terms of the file: see <see cref="Guard{T}"/>.
    /// </summary>
    public static string Text(string path) =>
        Guard(
            path,
            _ => _ switch
            {
                ".pdf" => PdfText(path),
                ".docx" => DocumentConverter.ConvertToMarkdown(path, markdown),
                ".xlsx" => ExcelConverter.ConvertToMarkdown(path, markdown),
                ".pptx" => PowerPointConverter.ConvertToMarkdown(path, markdown),
                // A binary map, so what it holds, as GeoJSON: features in order, their properties,
                // and their coordinates one per line.
                ".fgb" or ".geoparquet" or ".kmz" or ".wkb" => GeoJson.WriteString(GeoConverter.Read(path)),
                _ => throw new NotSupportedException($"There is no text for a {_} file.")
            });

    /// <summary>
    /// Reads a document, and when it cannot be read says why in terms of the file rather than of
    /// whichever library gave up on it.
    /// <para>
    /// A snapshot that is not what its extension says is ordinary: a test that wrote half a file
    /// before it failed, an empty one, an error page saved as a PDF. The message is all a reviewer
    /// is shown of it, and what the libraries say is about their own insides - "End of Central
    /// Directory record could not be found" for a Word document that is not a zip archive.
    /// </para>
    /// <para>
    /// Only what a damaged file causes is put this way. A file that is locked, a native library
    /// that is missing or memory running out is said as it was thrown: calling the document
    /// unreadable for those sends the reviewer to look at a file there is nothing wrong with.
    /// </para>
    /// </summary>
    static T Guard<T>(string path, Func<string, T> read)
    {
        var extension = Extension(path);
        if (new FileInfo(path).Length == 0)
        {
            throw Said("The file is empty.");
        }

        try
        {
            if (IsArchive(extension))
            {
                CheckArchive(path, extension);
            }

            return read(extension);
        }
        catch (Exception exception)
            when (IsDamage(exception))
        {
            throw Said($"Not a readable {Kind(extension)}: {Detail(exception)}", exception);
        }
    }

    /// <summary>
    /// The formats that are a zip archive of parts. Opened as one first, which reads only its
    /// directory: one that is not an archive at all then says so, rather than what the first
    /// reader to trip over it happened to be looking for.
    /// </summary>
    static bool IsArchive(string extension) =>
        extension is ".docx" or ".xlsx" or ".pptx" or ".kmz";

    static void CheckArchive(string path, string extension)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            _ = archive.Entries.Count;
        }
        catch (InvalidDataException exception)
        {
            throw Said(
                $"Not a readable {Kind(extension)}: it is not a zip archive, or was cut short.",
                exception);
        }
    }

    /// <summary>
    /// Whether an exception is what reading a damaged file comes to. Everything a parser throws is,
    /// including the ones that are its own bugs - an index out of range, a null - since bytes that
    /// are not the format are what walked it there. What is left out is what says something about
    /// the machine instead.
    /// </summary>
    static bool IsDamage(Exception exception) =>
        !exception.Data.Contains(said) &&
        exception is not (
            NotSupportedException or
            OperationCanceledException or
            OutOfMemoryException or
            UnauthorizedAccessException or
            DllNotFoundException or
            EntryPointNotFoundException or
            BadImageFormatException or
            TypeLoadException or
            TypeInitializationException or
            MissingMemberException) &&
        exception is not IOException or EndOfStreamException;

    /// <summary>
    /// What the library said, as one line, without the lead-in that repeats what
    /// <see cref="Kind"/> has already said: GeoConvert's "Invalid KML data: " and PDFium's "Failed
    /// to load PDF: ".
    /// </summary>
    static string Detail(Exception exception)
    {
        var message = exception.Message.AsSpan().Trim();
        var end = message.IndexOfAny('\r', '\n');
        if (end >= 0)
        {
            message = message[..end].TrimEnd();
        }

        const string data = " data: ";
        if (message.StartsWith("Invalid ") &&
            message.IndexOf(data) is > 0 and var at)
        {
            message = message[(at + data.Length)..];
        }

        const string pdf = "Failed to load PDF: ";
        if (message.StartsWith(pdf))
        {
            message = message[pdf.Length..];
        }

        if (message.IsEmpty)
        {
            return exception.GetType().Name;
        }

        return message.ToString();
    }

    static string Kind(string extension) =>
        extension switch
        {
            ".pdf" => "PDF",
            ".docx" => "Word document",
            ".xlsx" => "Excel workbook",
            ".pptx" => "PowerPoint presentation",
            ".svg" => "SVG",
            ".geojson" => "GeoJSON map",
            ".topojson" => "TopoJSON map",
            ".kml" => "KML map",
            ".kmz" => "KMZ map",
            ".gpx" => "GPX map",
            ".wkt" => "WKT map",
            ".wkb" => "WKB map",
            ".fgb" => "FlatGeobuf map",
            ".geoparquet" => "GeoParquet map",
            _ => $"{extension} file"
        };

    /// <summary>
    /// A reason that is already about the file, so <see cref="Guard{T}"/> passes it on as it is
    /// rather than putting it into words again. Marked rather than a type of its own, because
    /// InvalidDataException is the type for it and is sealed.
    /// </summary>
    static InvalidDataException Said(string message, Exception? inner = null) =>
        new(message, inner)
        {
            Data =
            {
                [said] = true
            }
        };

    const string said = "DiffEngineViewer.Said";

    /// <summary>
    /// Writes every page into <paramref name="directory"/> as a png, calling
    /// <paramref name="landed"/> with each file once it is complete, and returns how many there
    /// are. An SVG is one page. Throws when the file is not what its extension says.
    /// <para>
    /// Deterministic: the same file renders to the same bytes, which is what lets the viewer say
    /// which pages of two documents differ by comparing hashes.
    /// </para>
    /// </summary>
    /// <param name="projection">
    /// The name of a <see cref="MapProjection"/>, for a map. A name rather than the value because
    /// only BCL types cross to here, and one that is not a projection is drawn as <c>Auto</c>
    /// rather than refused: the map is still worth seeing.
    /// </param>
    public static int Render(string path, string directory, string projection, Action<string> landed)
    {
        var options = new ImageExportOptions
        {
            Dpi = dpi,
            DeterministicRendering = true,
            // A family the machine lacks fails the whole document otherwise, and Calibri, which most
            // Word documents are set in, is missing from Linux and from macOS without Office. Aptos
            // ships inside Morph, so it resolves everywhere, and both sides of a diff get it.
            FontFallback = _ => "Aptos"
        };
        return Guard(
            path,
            _ => _ switch
            {
                ".pdf" => RenderPdf(path, directory, landed),
                ".docx" => Announce(new SkiaDocumentConverter().ConvertToImages(path, directory, options), landed),
                ".xlsx" => Announce(new SkiaExcelConverter().ConvertToImages(path, directory, options), landed),
                ".pptx" => Announce(new SkiaPowerPointConverter().ConvertToImages(path, directory, options), landed),
                ".svg" => RenderSvg(path, directory, landed),
                ".geojson" or ".topojson" or ".kml" or ".kmz" or ".gpx" or ".wkt" or ".wkb" or ".fgb" or ".geoparquet" =>
                    RenderMap(path, directory, projection, landed),
                _ => throw new NotSupportedException($"There are no pages for a {_} file.")
            });
    }

    /// <summary>
    /// GeoConvert's own rasterizer rather than its Skia one: it has no dependency to drift from the
    /// SkiaSharp Morph pins, and draws the same features to the same bytes everywhere.
    /// </summary>
    static int RenderMap(string path, string directory, string projection, Action<string> landed)
    {
        var features = GeoConverter.Read(path);
        if (features.Count == 0)
        {
            throw Said("The map has no features to draw.");
        }

        if (!Enum.TryParse<MapProjection>(projection, out var layout))
        {
            layout = MapProjection.Auto;
        }

        var file = PageFile(directory, 0);
        MapRenderer.RenderPng(
            features,
            file,
            new()
            {
                // The longer side, whichever it is, so a tall map is no larger than a wide one.
                MaxDimension = 2048,
                Projection = layout
            });
        landed(file);
        return 1;
    }

    static string PdfText(string path)
    {
        using var document = PdfiumDocument.Load(path);
        var builder = new StringBuilder();
        for (var index = 0; index < document.PageCount; index++)
        {
            using var page = document.LoadPage(index);
            builder.Append("--- page ").Append(index + 1).Append(" ---\n");
            var text = page.GetText();
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            builder.Append(text);
            if (!text.EndsWith('\n'))
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// A page at a time, each announced as it lands, so the first page of a long document is on
    /// screen while the rest are still being drawn.
    /// </summary>
    static int RenderPdf(string path, string directory, Action<string> landed)
    {
        using var document = PdfiumDocument.Load(path);
        for (var index = 0; index < document.PageCount; index++)
        {
            var size = document.GetPageSize(index);
            var longest = Math.Max(size.Width, size.Height);
            var pageDpi = longest <= 0 ? dpi : Math.Min(dpi, longestPage * 72 / longest);
            var file = PageFile(directory, index);
            File.WriteAllBytes(file, document.RenderPage(index, pageDpi));
            landed(file);
        }

        return document.PageCount;
    }

    /// <summary>
    /// Morph lays a document out as a whole, so its pages all land together.
    /// </summary>
    static int Announce(ConversionResult result, Action<string> landed)
    {
        foreach (var image in result.ImagePaths)
        {
            landed(image);
        }

        return result.PageCount;
    }

    static int RenderSvg(string path, string directory, Action<string> landed)
    {
        using var svg = new SKSvg();
        svg.Settings.EnableJavaScript = false;
        svg.Settings.EnableExternalJavaScript = false;
        svg.Settings.NavigationHandler = null;
        var parameters = new SvgParameters(
            null,
            null,
            null,
            new()
            {
                ExternalResources = SvgExternalResourcePolicy.SameDocumentAndDataOnly
            });
        var picture = svg.Load(path, parameters) ??
                      throw Said("Not an SVG document.");
        var bounds = picture.CullRect;
        var longest = Math.Max(bounds.Width, bounds.Height);
        if (longest <= 0)
        {
            throw Said("The SVG has no size to be drawn at.");
        }

        // Vector, so drawn larger than its own size when that is small: the heads never enlarge a
        // picture, and a 24 pixel icon cannot be reviewed at 24 pixels. Capped, because each side
        // is decoded and kept by the head that draws it.
        var scale = longest switch
        {
            < 1024 => 1024 / longest,
            > 2048 => 2048 / longest,
            _ => 1f
        };
        var file = PageFile(directory, 0);
        using (var stream = File.Create(file))
        {
            if (!svg.Save(stream, SKColors.Transparent, SKEncodedImageFormat.Png, 100, scale, scale))
            {
                throw Said("The SVG could not be drawn.");
            }
        }

        landed(file);
        return 1;
    }

    /// <summary>
    /// The name Morph gives its own pages, so every format's pages read the same in a directory.
    /// </summary>
    static string PageFile(string directory, int index) =>
        Path.Combine(directory, $"page_{index + 1:0000}.png");

    static string Extension(string path) =>
        Path.GetExtension(path).ToLowerInvariant();
}
