using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using BenchmarkDotNet.Attributes;

// One paint of the canvas over a pair of large pictures the reader has zoomed into, but not so
// far that they are drawn at their own size: more of each picture's pixels go into the pane than
// the pane has. What shows is the pane's worth of pixels however far in it is, so a paint should
// cost by the pane. Every frame of a drag across the picture is one such paint, and so is every
// wheel notch over the text of a document whose page is drawn under it.
[MemoryDiagnoser]
public class PicturePaintBenchmarks
{
    // How far in, as a percentage of the size that fits the pane. A 4000 by 3000 picture fits
    // this window at about an eighth of its own size, so these draw it at a fifth, a quarter and
    // a half: the first two take more than two of its pixels across for each one drawn, the last
    // fewer.
    [Params(150, 200, 400)]
    public int Percent;

    // The window a viewer opens at.
    const int width = 1100;
    const int height = 700;

    const int pictureWidth = 4000;
    const int pictureHeight = 3000;

    string directory = "";
    OffscreenCanvas canvas = null!;
    Screen[] dragged = [];
    int frame;

    [GlobalSetup]
    public void Setup()
    {
        ViewerApp.ConfigureUnscaled();

        directory = Directory.CreateTempSubdirectory("deview-benchmark-").FullName;
        var received = Path.Combine(directory, "Sample.received.png");
        var verified = Path.Combine(directory, "Sample.verified.png");
        Draw(received, Color.FromArgb(198, 64, 64), Color.FromArgb(240, 220, 180));
        Draw(verified, Color.FromArgb(64, 150, 198), Color.FromArgb(240, 220, 180));

        var entry = QueueEntry.ForFiles(received, verified, FileSide.Read(received), FileSide.Read(verified));
        var state = ViewerSession.EnqueueFile(SessionState.Start(ViewerMode.File), entry);
        while (PictureZoom.Factor(state.Zoom) * 100 < Percent)
        {
            state = ViewerSession.Apply(state, CommandKind.ZoomIn);
        }

        canvas = new(width, height);
        // A drag from one corner of what can be shown towards the other, a frame at each step.
        // Kept near the middle, so that at every one of these sizes each step moves the picture.
        dragged = Enumerable.Range(0, 16)
            .Select(_ => canvas.Fit(ViewerSession.PanTo(state, 0.35 + _ * 0.02, 0.45 + _ * 0.006)))
            .ToArray();

        canvas.Show(canvas.Fit(state));
        // Once here: the pictures are decoded by the first paint that needs them, and whatever a
        // paint keeps of them from one frame to the next is kept before anything is timed.
        canvas.Paint();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        canvas.Dispose();
        Directory.Delete(directory, true);
    }

    [Benchmark]
    public void Still() =>
        canvas.Paint();

    [Benchmark]
    public void Dragged()
    {
        canvas.Show(dragged[frame++ % dragged.Length]);
        canvas.Paint();
    }

    // A wash of colour under a grid of lines: nothing a resampler can skip, and edges that show
    // what it did with them.
    static void Draw(string path, Color from, Color to)
    {
        using var bitmap = new Bitmap(pictureWidth, pictureHeight, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            var all = new Rectangle(0, 0, pictureWidth, pictureHeight);
            using var wash = new LinearGradientBrush(all, from, to, 35f);
            graphics.FillRectangle(wash, all);
            using var line = new Pen(Color.FromArgb(40, 40, 40), 3);
            for (var x = 0; x < pictureWidth; x += 80)
            {
                graphics.DrawLine(line, x, 0, x, pictureHeight);
            }

            for (var y = 0; y < pictureHeight; y += 80)
            {
                graphics.DrawLine(line, 0, y, pictureWidth, y);
            }
        }

        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
