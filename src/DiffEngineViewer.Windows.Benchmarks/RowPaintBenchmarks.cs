using System.Text;
using BenchmarkDotNet.Attributes;

// One paint of the canvas over rows far longer than a pane is wide. Nothing scrolls sideways, so
// all of a row past the pane's right edge is never seen, and a paint should cost what the part
// that shows costs. Every wheel notch and every frame of a selection drag is one such paint.
[MemoryDiagnoser]
public class RowPaintBenchmarks
{
    // The window a viewer opens at, where a pane shows 54 characters of a row, and one maximised
    // across a wide display, at 1600 pixels a pane and 174 characters.
    [Params(1100, 3212)]
    public int Width;

    // Room for 36 rows a pane, so 72 rows a paint.
    const int height = 800;

    const int megabyte = 1024 * 1024;

    OffscreenCanvas longLines = null!;
    OffscreenCanvas megabyteLine = null!;

    [GlobalSetup]
    public void Setup()
    {
        // As the tests' host is: at one scale whatever display this runs on, so the cell a pane
        // is counted in is the same number of pixels on every machine.
        ViewerApp.ConfigureUnscaled();

        // 2,000 characters a line, which is more than a pane has pixels at either width, let
        // alone cells. One line in ten differs, as the lines of a snapshot that failed do.
        longLines = Showing(
            Records(lines: 60, length: 2000, changeEvery: -1),
            Records(lines: 60, length: 2000, changeEvery: 10));

        // What a minified bundle or a serialized blob is: all one line. Half way along it, far
        // past anything that shows, is one character from outside ASCII, which is all it takes
        // for the row to stop being a single run of plain text.
        var line = Line(megabyte, 0).Remove(megabyte / 2, 1).Insert(megabyte / 2, "中");
        megabyteLine = Showing($"before\n{line}\nafter", $"before\n{line}\nand after");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        longLines.Dispose();
        megabyteLine.Dispose();
    }

    [Benchmark]
    public void LongLines() =>
        longLines.Paint();

    [Benchmark]
    public void MegabyteLine() =>
        megabyteLine.Paint();

    OffscreenCanvas Showing(string received, string expected)
    {
        var canvas = new OffscreenCanvas(Width, height);
        var entry = QueueEntry.ForFiles(
            "Sample.received.txt",
            "Sample.verified.txt",
            FileSide.OfText(received),
            FileSide.OfText(expected));
        canvas.Show(canvas.Fit(ViewerSession.EnqueueFile(SessionState.Start(ViewerMode.File), entry)));
        // Once here, so the font is measured and every brush made before anything is timed.
        canvas.Paint();
        return canvas;
    }

    // The records of a minified array, a line each: the shape of a snapshot whose lines are long.
    static string Records(int lines, int length, int changeEvery)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < lines; index++)
        {
            if (index > 0)
            {
                builder.Append('\n');
            }

            // A thousand apart, so no line is another's content and a changed one pairs with its own.
            var changed = changeEvery > 0 && index % changeEvery == 0;
            builder.Append(Line(length, index * 1000 + (changed ? 500 : 0)));
        }

        return builder.ToString();
    }

    static string Line(int length, int first)
    {
        var builder = new StringBuilder(length + 64);
        for (var item = first; builder.Length < length; item++)
        {
            builder.Append($"{{\"id\":{item},\"name\":\"item {item}\",\"tags\":[\"alpha\",\"beta\"],\"price\":{item % 90 + 10}.5}},");
        }

        builder.Length = length;
        return builder.ToString();
    }
}
