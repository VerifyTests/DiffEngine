using System.Text;
using BenchmarkDotNet.Attributes;

// What an owning viewer does with a failing pair before it answers the test process that sent it:
// read both files and diff them. The sender's synchronous wait is three seconds, so this is the
// number that decides whether a pair is answered at all. Two files with no line in common, which
// is what a change of indentation leaves, and files rather than strings so the reads are in it.
[MemoryDiagnoser]
public class TrackedEntryBenchmarks
{
    [Params(10_000, 40_000)]
    public int Lines;

    string directory = "";
    string received = "";
    string verified = "";

    [GlobalSetup]
    public void Setup()
    {
        directory = Path.Combine(Path.GetTempPath(), "deview-benchmarks", $"entry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        received = Path.Combine(directory, "Sample.received.json");
        verified = Path.Combine(directory, "Sample.verified.json");
        File.WriteAllText(received, Json(Lines, "    "));
        File.WriteAllText(verified, Json(Lines, "  "));
    }

    [GlobalCleanup]
    public void Cleanup() =>
        Directory.Delete(directory, true);

    [Benchmark]
    public object ReadAndDiffAPair() =>
        TrackedEntry.ForMove(received, verified);

    static string Json(int lines, string indent)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < lines; index++)
        {
            builder.Append(indent);
            builder.Append("\"property");
            builder.Append(index);
            builder.Append("\": ");
            builder.Append(index);
            builder.Append(",\n");
        }

        return builder.ToString();
    }
}
