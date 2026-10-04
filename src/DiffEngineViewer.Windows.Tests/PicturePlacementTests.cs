/// <summary>
/// Where a picture goes in the space under a pane's rows, fitted and enlarged. The same numbers
/// the paint draws with and a drag is measured against, which is what keeps the picture under the
/// pointer that is moving it.
/// </summary>
public class PicturePlacementTests
{
    static readonly Rectangle space = new(100, 50, 400, 300);

    /// <summary>
    /// Never enlarged past its own size until the reader asks: an eight pixel icon stretched
    /// across a pane is an interpolation of its pixels rather than a look at them.
    /// </summary>
    [Test]
    public async Task ASmallPictureIsDrawnAtItsOwnSizeInTheMiddle()
    {
        var placement = PicturePlacement.Of(space, Picture(80, 60));

        await Assert.That(placement.Bounds).IsEqualTo(new(260, 170, 80, 60));
        await Assert.That(placement.Source).IsEqualTo(new(0, 0, 1, 1));
    }

    [Test]
    public async Task ALargePictureIsFittedToTheSpace()
    {
        var placement = PicturePlacement.Of(space, Picture(1600, 600));

        await Assert.That(placement.Bounds).IsEqualTo(new(100, 125, 400, 150));
        await Assert.That(placement.Source).IsEqualTo(new(0, 0, 1, 1));
    }

    /// <summary>
    /// Enlarged, and still smaller than the space: all of it shows, in the middle, larger.
    /// </summary>
    [Test]
    public async Task AnEnlargedPictureThatStillFitsShowsWhole()
    {
        var placement = PicturePlacement.Of(space, Picture(80, 60, zoom: 2));

        await Assert.That(placement.Bounds).IsEqualTo(new(220, 140, 160, 120));
        await Assert.That(placement.Source).IsEqualTo(new(0, 0, 1, 1));
    }

    /// <summary>
    /// Past the space it fills it, and the part that shows is the part around the centre: a
    /// quarter of the width and three eighths of the height of a picture eight times the size.
    /// </summary>
    [Test]
    public async Task AnEnlargedPictureIsCutOffAtTheEdgesOfTheSpace()
    {
        var placement = PicturePlacement.Of(space, Picture(200, 100, zoom: 8));

        await Assert.That(placement.Bounds).IsEqualTo(space);
        await Assert.That(placement.Size).IsEqualTo(new(1600, 800));
        await Assert.That(placement.Source).IsEqualTo(new(0.375f, 0.3125f, 0.25f, 0.375f));
    }

    /// <summary>
    /// Enlarged past the space one way only, it is cut off that way and centred the other.
    /// </summary>
    [Test]
    public async Task APictureCutOffOneWayIsCentredTheOther()
    {
        var placement = PicturePlacement.Of(space, Picture(200, 50, zoom: 4));

        await Assert.That(placement.Bounds).IsEqualTo(new(100, 100, 400, 200));
        await Assert.That(placement.Source).IsEqualTo(new(0.25f, 0, 0.5f, 1));
    }

    /// <summary>
    /// The model does not know how many pixels a pane has, so it can ask for a centre at the very
    /// edge. It is moved in as far as it takes to keep the space full, rather than drawing the
    /// picture half off it.
    /// </summary>
    [Test]
    public async Task ACentreAtTheEdgeIsMovedInUntilTheSpaceIsFull()
    {
        var placement = PicturePlacement.Of(space, Picture(200, 100, zoom: 8, x: 1, y: 0));

        await Assert.That(placement.Centre).IsEqualTo(new(0.875, 0.1875));
        await Assert.That(placement.Source).IsEqualTo(new(0.75f, 0, 0.25f, 0.375f));
    }

    /// <summary>
    /// The picture follows the pointer: dragged right, what shows is further left in the picture,
    /// by the fraction of the enlarged picture the pointer covered.
    /// </summary>
    [Test]
    public async Task ADragMovesThePictureWithThePointer()
    {
        var placement = PicturePlacement.Of(space, Picture(200, 100, zoom: 8));

        var dragged = placement.Dragged(new(160, -80));

        await Assert.That(dragged).IsEqualTo(new(0.4, 0.6));
    }

    [Test]
    public async Task ADragStopsAtTheEdgeOfThePicture()
    {
        var placement = PicturePlacement.Of(space, Picture(200, 100, zoom: 8));

        var dragged = placement.Dragged(new(100_000, -100_000));

        await Assert.That(dragged).IsEqualTo(new(0.125, 0.8125));
    }

    /// <summary>
    /// The centre is one point for both panes, and here it is somewhere the pane beside can show
    /// and this one cannot: a wide picture beside a tall one, eight times the size that fits, with
    /// the tall one dragged nearly to its bottom. Dragging the wide one sideways leaves that where
    /// it is. It came back as far as the wide one's own picture can go, so the tall one jumped.
    /// </summary>
    [Test]
    public async Task ADragAcrossLeavesThePaneBesideWhereItIsDown()
    {
        var wide = PicturePlacement.Of(space, Picture(200, 100, zoom: 8, y: 0.875));
        var tall = PicturePlacement.Of(space, Picture(100, 200, zoom: 8, y: 0.875));

        // What the wide one draws about, which is as far down as it goes
        await Assert.That(wide.Centre.Y).IsEqualTo(0.8125);
        await Assert.That(tall.Centre.Y).IsEqualTo(0.875);
        await Assert.That(wide.Dragged(new(160, 0), tall)).IsEqualTo(new(0.4, 0.875));
    }

    /// <summary>
    /// And a drag stops where the pane that can go further does, each way: the wide picture's
    /// range across and the tall one's down, whichever of the two is under the pointer.
    /// </summary>
    [Test]
    public async Task ADragStopsWhereThePaneThatGoesFurtherDoes()
    {
        var wide = PicturePlacement.Of(space, Picture(200, 100, zoom: 8));
        var tall = PicturePlacement.Of(space, Picture(100, 200, zoom: 8));

        await Assert.That(wide.Dragged(new(100_000, -100_000), tall)).IsEqualTo(new(0.125, 0.90625));
        await Assert.That(tall.Dragged(new(100_000, -100_000), wide)).IsEqualTo(new(0.125, 0.90625));
    }

    /// <summary>
    /// A way the picture cannot move, because all of it shows, is left as the model had it. Kept
    /// inside what this pane shows it was the middle, on every move of a drag along the other
    /// way, which took the pane beside back to its middle from wherever it had been dragged to.
    /// </summary>
    [Test]
    public async Task AWayThePictureCannotMoveIsLeftAsTheModelHadIt()
    {
        var placement = PicturePlacement.Of(space, Picture(200, 50, zoom: 4, y: 0.2));

        await Assert.That(placement.MovesAcross).IsTrue();
        await Assert.That(placement.MovesDown).IsFalse();
        await Assert.That(placement.Dragged(new(80, 50))).IsEqualTo(new(0.4, 0.2));
    }

    static ImagePane Picture(int width, int height, double zoom = 1, double x = 0.5, double y = 0.5) =>
        new("picture.png", width, height, "HASH", zoom, x, y);
}
