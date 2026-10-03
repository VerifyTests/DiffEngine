using System.Text;
using BenchmarkDotNet.Attributes;

// A frame while everything in a pane is selected. The status line says how much is selected, and
// working that out walks every selected row, so ctrl+a on a large file made each frame cost by
// the length of the file for as long as the selection stood. Tab indented, since a row with a tab
// in it is flattened to be measured and that is where the allocation was.
[MemoryDiagnoser]
public class SelectionFrameBenchmarks
{
    [Params(20_000, 100_000)]
    public int Lines;

    SessionState selected = null!;

    [GlobalSetup]
    public void Setup()
    {
        var state = ViewerSession.EnqueueFile(
            SessionState.Start(ViewerMode.File, 160, 50),
            QueueEntry.ForFiles(
                "Sample.received.txt",
                "Sample.verified.txt",
                FileSide.OfText(Text(Lines, changeEvery: 100)),
                FileSide.OfText(Text(Lines, changeEvery: -1))));
        selected = ViewerSession.Apply(state, CommandKind.SelectAll);
        if (selected.LiveSelection is null)
        {
            throw new("Nothing was selected, so this would measure a frame with no selection.");
        }
    }

    readonly ScreenCache screens = new();

    // What a frame of a drag still pays, since each one moves the selection's end.
    [Benchmark(Baseline = true)]
    public object BuildScreen() =>
        ScreenBuilder.Build(selected);

    // And what every frame after ctrl+a pays, for as long as the selection stands.
    [Benchmark]
    public object FrameWithNothingChanged() =>
        screens.For(selected);

    static string Text(int lines, int changeEvery)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < lines; index++)
        {
            var value = changeEvery > 0 && index % changeEvery == 0 ? index + 1 : index;
            builder.Append("\t\t\"property");
            builder.Append(index);
            builder.Append("\": ");
            builder.Append(value);
            builder.Append(",\n");
        }

        return builder.ToString();
    }
}
