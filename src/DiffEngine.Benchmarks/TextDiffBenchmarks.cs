using System.Text;
using BenchmarkDotNet.Attributes;

namespace DiffEngine.Benchmarks;

// The line diff behind TextDiff.Compute, TextDiff.Format and every text pair the viewer shows.
// Myers costs by the number of edits, so what matters is how much the two sides share, not how
// long they are: the same line count is measured with almost everything shared, with nothing
// shared, and with everything shared but in another order.
[MemoryDiagnoser]
public class TextDiffBenchmarks
{
    [Params(10_000, 40_000)]
    public int Lines;

    string expected = "";
    string onePercentChanged = "";
    string reindented = "";
    string reordered = "";

    [GlobalSetup]
    public void Setup()
    {
        expected = Json(Lines, "  ", -1);
        // One line in a hundred differs, the usual shape of a snapshot that failed.
        onePercentChanged = Json(Lines, "  ", 100);
        // A serializer setting that changes the indentation: no line survives it.
        reindented = Json(Lines, "    ", -1);
        // Every line survives, none in the place it was.
        var lines = expected.Split('\n');
        Array.Reverse(lines);
        reordered = string.Join('\n', lines);
    }

    [Benchmark]
    public int OnePercentChanged() =>
        LineDiff.Build(expected, onePercentChanged).Entries.Count;

    [Benchmark]
    public int NothingInCommon() =>
        LineDiff.Build(expected, reindented).Entries.Count;

    [Benchmark]
    public int SameLinesInAnotherOrder() =>
        LineDiff.Build(expected, reordered).Entries.Count;

    static string Json(int lines, string indent, int changeEvery)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < lines; index++)
        {
            var value = changeEvery > 0 && index % changeEvery == 0 ? index + 1 : index;
            builder.Append(indent);
            builder.Append("\"property");
            builder.Append(index);
            builder.Append("\": ");
            builder.Append(value);
            builder.Append(",\n");
        }

        return builder.ToString();
    }
}
