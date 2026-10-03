#nullable enable
using BenchmarkDotNet.Attributes;

/// <summary>
/// What one frame costs the Linux head to draw: a comparison of text, for what a frame costs with
/// no picture in it, and a comparison of two pictures that fill their panes, once with pictures
/// that can be seen through and once with pictures that cannot. At the size the viewer opens at,
/// and at the size of a window maximised on a 4K screen.
/// <para>
/// Linux only, and only with a display to open a window on: anywhere else these are left out of
/// the run. The numbers in this file's history are from an <c>ubuntu:24.04</c> container set up
/// as the <c>unix</c> job in <c>build.yml</c> is, which is Xvfb and Mesa's software rasteriser,
/// here on the four threads a hosted runner would give it. The shim is built from <c>native</c>
/// and copied to <c>src/DiffEngineViewer.Linux/runtimes/linux-x64/native</c>, as that job does,
/// and then: <c>LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe LP_NUM_THREADS=4 xvfb-run -a
/// --server-args="-screen 0 3840x2160x24" dotnet run -c Release --project
/// src/DiffEngineViewer.Benchmarks -- --filter "*NativeFrame*" --warmupCount 5 --iterationCount
/// 30</c>.
/// </para>
/// <para>
/// Mean here is the time from a frame being handed over to the window having it, and that is
/// only so because of <see cref="FallOutOfStep"/>. The CPU and Triangles columns do not depend
/// on it.
/// </para>
/// </summary>
[MemoryDiagnoser]
[NativeHead]
public class NativeFrameBenchmarks
{
    [Params("1100x700", "3840x2160")]
    public string Window = "";

    NativeHead? head;
    Screen[] screens = [];
    int turn;

    [GlobalSetup(Target = nameof(Text))]
    public void OpenOnText() =>
        Open(NativeScenes.Text);

    [GlobalSetup(Target = nameof(OpaquePictures))]
    public void OpenOnOpaquePictures() =>
        Open(NativeScenes.OpaquePictures);

    [GlobalSetup(Target = nameof(TranslucentPictures))]
    public void OpenOnTranslucentPictures() =>
        Open(NativeScenes.TranslucentPictures);

    [GlobalCleanup]
    public void Close() =>
        head?.Dispose();

    /// <summary>
    /// The shim holds the loop to sixty frames a second by waiting out what is left of a sixtieth
    /// of a second once a frame is on the screen, so frames presented one after another all take
    /// 16.7 ms however little there was to draw. A frame presented later than that after the one
    /// before has nothing left to wait out, and takes as long as it took. BenchmarkDotNet runs a
    /// benchmark that has one of these once an iteration, so every measured frame is such a frame.
    /// </summary>
    [IterationSetup]
    public void FallOutOfStep() =>
        Thread.Sleep(20);

    [Benchmark]
    public bool Text() =>
        Frame();

    [Benchmark]
    public bool OpaquePictures() =>
        Frame();

    [Benchmark]
    public bool TranslucentPictures() =>
        Frame();

    bool Frame() =>
        head!.Run(screens[turn++ & 1]);

    void Open(Func<int, int, SessionState> scene)
    {
        var size = Window.Split('x');
        head = NativeHead.Open(int.Parse(size[0]), int.Parse(size[1]));
        screens = NativeScenes.Alternating(scene(head.Columns, head.Rows));
        head.Settle(screens[0]);
    }
}
