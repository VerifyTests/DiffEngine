using System.Text;
using BenchmarkDotNet.Attributes;

// How a row is cut up for drawing. A run of characters the embedded font draws a cell wide is one
// segment, drawn as one string; anything else is a segment a character, each laid out on its own
// by every head and encoded on its own for two of them. So what counts as such a character decides
// what a row of box drawing, arrows or typographic punctuation costs: a table drawn in a snapshot,
// which is rows of little else.
[MemoryDiagnoser]
public class CellGridBenchmarks
{
    [Params("ascii", "box", "cjk")]
    public string Text = "";

    string[] rows = [];

    [GlobalSetup]
    public void Setup() =>
        rows = Enumerable.Range(0, 100).Select(Row).ToArray();

    // The segments of a hundred rows, which is two panes of a tall window.
    [Benchmark]
    public int SegmentsOfAHundredRows()
    {
        var segments = 0;
        foreach (var row in rows)
        {
            segments += CellGrid.Segments(row).Count;
        }

        return segments;
    }

    string Row(int index)
    {
        var builder = new StringBuilder();
        for (var cell = 0; cell < 30; cell++)
        {
            switch (Text)
            {
                case "box":
                    // A table's rule and its cells: light box drawing, an arrow, an ellipsis
                    builder.Append(cell % 6 == 0 ? "┼" : "──");
                    builder.Append(cell % 10 == 0 ? " → …" : "");
                    break;
                case "cjk":
                    builder.Append((char) (0x4E00 + (index * 7 + cell * 13) % 2000));
                    break;
                default:
                    builder.Append("ab ");
                    break;
            }
        }

        return builder.ToString();
    }
}
