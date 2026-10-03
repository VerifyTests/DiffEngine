using System.Text;
using BenchmarkDotNet.Attributes;

// The macOS and Linux heads are handed a screen as flat buffers, which ScreenPayload encodes: every
// string as UTF-8, and every row as the segments CellGrid cuts it into. A row of plain text is one
// segment. A row of CJK is one per character, and each segment's byte offset was found by counting
// the row's bytes again from its start, so such a row cost by the square of its length.
//
// A window the size of a 4K display: 426 cells across and 130 down at the 9 pixel cell. Lines of
// 300 characters, since a row is encoded as far as the window is wide rather than the pane, so a
// long line is where a row's length shows.
[MemoryDiagnoser]
public class ScreenPayloadBenchmarks
{
    [Params("ascii", "cjk")]
    public string Text = "";

    readonly ScreenPayload payload = new();
    Screen first = null!;
    Screen second = null!;
    bool flip;

    [GlobalSetup]
    public void Setup()
    {
        var state = ViewerSession.EnqueueFile(
            SessionState.Start(ViewerMode.File, 426, 130),
            QueueEntry.ForFiles(
                "Sample.received.txt",
                "Sample.verified.txt",
                FileSide.OfText(Rows(changed: true)),
                FileSide.OfText(Rows(changed: false))));
        // Two screens that say the same thing, so one after the other is a screen the payload has
        // not been handed before without being a different amount of work.
        first = ScreenBuilder.Build(state);
        second = ScreenBuilder.Build(state);
    }

    // A screen that changed since the last frame, which is every frame of a scroll or a drag.
    [Benchmark]
    public void EncodeAChangedScreen()
    {
        flip = !flip;
        payload.Build(flip ? first : second);
    }

    // The screen the last frame was, which is every frame of a window nobody is touching.
    [Benchmark]
    public void EncodeTheSameScreenAgain() =>
        payload.Build(first);

    string Rows(bool changed)
    {
        var builder = new StringBuilder();
        for (var row = 0; row < 400; row++)
        {
            for (var column = 0; column < 300; column++)
            {
                if (Text == "cjk")
                {
                    // CJK Unified Ideographs, two cells each
                    builder.Append((char) (0x4E00 + (row * 7 + column * 13) % 2000));
                }
                else
                {
                    builder.Append((char) ('a' + (row + column) % 26));
                }
            }

            if (changed &&
                row % 10 == 0)
            {
                builder.Append('!');
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }
}
