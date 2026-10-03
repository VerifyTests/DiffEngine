#nullable enable
using BenchmarkDotNet.Attributes;

/// <summary>
/// What a second costs the Linux head while nothing happens: the same screen presented sixty
/// times with nothing arriving in between, which is the loop of a viewer left open on a
/// comparison. Of text and of two pictures, at the two sizes <see cref="NativeFrameBenchmarks"/>
/// uses, and run the way its summary says.
/// <para>
/// One operation is that second, so CPU is what a second of an idle window costs this process
/// and X server CPU what it costs the server, Drawn is how many of its sixty frames were put on
/// the screen, and Triangles what was submitted to put them there. Mean is how long the sixty
/// took. It is a second for a head that is keeping to sixty frames a second, whether it drew
/// them or left them alone, and less than a second would be a loop that has stopped waiting for
/// the next frame.
/// </para>
/// </summary>
[MemoryDiagnoser]
[NativeHead]
public class NativeIdleBenchmarks
{
    [Params("1100x700", "3840x2160")]
    public string Window = "";

    NativeHead? head;
    Screen? screen;

    [GlobalSetup(Target = nameof(Text))]
    public void OpenOnText() =>
        Open(NativeScenes.Text);

    [GlobalSetup(Target = nameof(Pictures))]
    public void OpenOnPictures() =>
        Open(NativeScenes.TranslucentPictures);

    [GlobalCleanup]
    public void Close() =>
        head?.Dispose();

    [Benchmark]
    public bool Text() =>
        head!.Run(screen!, NativeHead.MostTurns);

    [Benchmark]
    public bool Pictures() =>
        head!.Run(screen!, NativeHead.MostTurns);

    void Open(Func<int, int, SessionState> scene)
    {
        var size = Window.Split('x');
        head = NativeHead.Open(int.Parse(size[0]), int.Parse(size[1]));
        screen = ScreenBuilder.Build(scene(head.Columns, head.Rows));
        head.Settle(screen);
    }
}
