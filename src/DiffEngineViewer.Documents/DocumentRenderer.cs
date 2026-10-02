using System.Text;
using Morph;
using Morph.PDFium;
using SkiaSharp;
using Svg;
using Svg.Model;
using Svg.Skia;

namespace DiffEngineViewer.Documents;

/// <summary>
/// What DiffEngineViewer reads documents with: the text of a PDF or an Office document, and every
/// page of one, or an SVG, as a picture.
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
    /// Markdown. Throws when the file is not what its extension says.
    /// </summary>
    public static string Text(string path) =>
        Extension(path) switch
        {
            ".pdf" => PdfText(path),
            ".docx" => DocumentConverter.ConvertToMarkdown(path, markdown),
            ".xlsx" => ExcelConverter.ConvertToMarkdown(path, markdown),
            ".pptx" => PowerPointConverter.ConvertToMarkdown(path, markdown),
            var extension => throw new NotSupportedException($"There is no text for a {extension} file.")
        };

    /// <summary>
    /// Writes every page into <paramref name="directory"/> as a png, calling
    /// <paramref name="landed"/> with each file once it is complete, and returns how many there
    /// are. An SVG is one page. Throws when the file is not what its extension says.
    /// <para>
    /// Deterministic: the same file renders to the same bytes, which is what lets the viewer say
    /// which pages of two documents differ by comparing hashes.
    /// </para>
    /// </summary>
    public static int Render(string path, string directory, Action<string> landed)
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
        return Extension(path) switch
        {
            ".pdf" => RenderPdf(path, directory, landed),
            ".docx" => Announce(new SkiaDocumentConverter().ConvertToImages(path, directory, options), landed),
            ".xlsx" => Announce(new SkiaExcelConverter().ConvertToImages(path, directory, options), landed),
            ".pptx" => Announce(new SkiaPowerPointConverter().ConvertToImages(path, directory, options), landed),
            ".svg" => RenderSvg(path, directory, landed),
            var extension => throw new NotSupportedException($"There are no pages for a {extension} file.")
        };
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
                      throw new InvalidDataException("Not an SVG document.");
        var bounds = picture.CullRect;
        var longest = Math.Max(bounds.Width, bounds.Height);
        if (longest <= 0)
        {
            throw new InvalidDataException("The SVG has no size to be drawn at.");
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
                throw new InvalidDataException("The SVG could not be drawn.");
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
