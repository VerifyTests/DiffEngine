/// <summary>
/// The window opens the way the last one was left: where it was, as large, and maximised if it
/// was. Maximise, close and run again used to open a small window in the middle of the screen
/// every time.
/// </summary>
[NotInParallel]
[TUnit.Core.Executors.STAThreadExecutor]
public class WindowPlacementTests
{
    static readonly Rectangle display = new(0, 0, 1920, 1040);
    static readonly Rectangle second = new(1920, 0, 2560, 1400);

    [Test]
    public async Task AWindowOnADisplayOpensWhereItWas()
    {
        var restored = ViewerForm.Restorable(new(300, 200, 1100, 700, false), [display]);

        await Assert.That(restored).IsEqualTo(new Rectangle(300, 200, 1100, 700));
    }

    /// <summary>
    /// A window snapped to an edge sits a resize border past it. Tidying that into the working
    /// area would move it a border's width on every run.
    /// </summary>
    [Test]
    public async Task ASnappedWindowIsLeftExactlyWhereItWas()
    {
        var restored = ViewerForm.Restorable(new(-7, 0, 974, 1047, false), [display]);

        await Assert.That(restored).IsEqualTo(new Rectangle(-7, 0, 974, 1047));
    }

    [Test]
    public async Task AWindowOnTheSecondDisplayOpensThere()
    {
        var restored = ViewerForm.Restorable(new(2200, 100, 1500, 900, false), [display, second]);

        await Assert.That(restored).IsEqualTo(new Rectangle(2200, 100, 1500, 900));
    }

    /// <summary>
    /// The display it was on has gone, which is a laptop taken off its dock. Opening it there
    /// anyway is a viewer that looks as if it did not start.
    /// </summary>
    [Test]
    public async Task AWindowOnADisplayThatHasGoneIsNotUsed()
    {
        var restored = ViewerForm.Restorable(new(2200, 100, 1500, 900, false), [display]);

        await Assert.That(restored).IsNull();
    }

    [Test]
    public async Task AWindowMostlyOffADisplayIsMovedOntoIt()
    {
        var restored = ViewerForm.Restorable(new(1700, 800, 1100, 700, false), [display]);

        await Assert.That(restored).IsEqualTo(new Rectangle(820, 340, 1100, 700));
    }

    /// <summary>
    /// Most of it is on the display, but its title bar is above the top: nothing to drag it back
    /// down by.
    /// </summary>
    [Test]
    public async Task AWindowWithItsTitleBarOffTheTopIsMovedDown()
    {
        var restored = ViewerForm.Restorable(new(300, -200, 1100, 900, false), [display]);

        await Assert.That(restored).IsEqualTo(new Rectangle(300, 0, 1100, 900));
    }

    /// <summary>
    /// Remembered on a larger display than there is now: as much of it as fits.
    /// </summary>
    [Test]
    public async Task AWindowLargerThanTheDisplayIsCutToIt()
    {
        var restored = ViewerForm.Restorable(new(100, 100, 3000, 2000, false), [display]);

        await Assert.That(restored).IsEqualTo(new Rectangle(0, 0, 1920, 1040));
    }

    [Test]
    public async Task AWindowTooSmallToHaveBeenLeftIsNotUsed()
    {
        var restored = ViewerForm.Restorable(new(300, 200, 40, 20, false), [display]);

        await Assert.That(restored).IsNull();
    }

    /// <summary>
    /// A first window, with nothing remembered, is scaled to its display once its handle exists,
    /// and WinForms has centred it by then for the size it was before. It grew down and to the
    /// right from there: at 125% the margins came out left 1161 and right 886, and at 150% on a
    /// 1080p display the footer was under the taskbar. That placement was then the one remembered.
    /// </summary>
    [Test]
    public async Task AFirstWindowIsCentredForTheSizeItOpensAt()
    {
        // Asked for larger than any display, so the size it opens at is not the size it was
        // centred for whatever this machine's scaling is: a test host that is not DPI aware
        // scales nothing, and a window of 1100 by 700 would be left exactly as it was made
        using var form = new ViewerForm("title", 9000, 6000);
        _ = form.Handle;

        var area = System.Windows.Forms.Screen.FromControl(form).WorkingArea;
        await Assert.That(form.Width).IsLessThan(area.Width);
        await Assert.That(form.Location).IsEqualTo(ViewerForm.Centred(area, form.Size));
        await Assert.That(form.Left).IsGreaterThan(area.Left);
    }

    [Test]
    public async Task CentredIsTheMiddleOfTheDisplayItIsOn()
    {
        await Assert.That(ViewerForm.Centred(display, new(1100, 700))).IsEqualTo(new Point(410, 170));
        await Assert.That(ViewerForm.Centred(second, new(1500, 900))).IsEqualTo(new Point(2450, 250));
    }

    /// <summary>
    /// Too large for the display, it starts at the top left rather than hanging off it evenly: the
    /// title bar is what it is moved by.
    /// </summary>
    [Test]
    public async Task AWindowLargerThanTheDisplayIsCentredFromItsTopLeft() =>
        await Assert.That(ViewerForm.Centred(second, new(3000, 1600))).IsEqualTo(new Point(1920, 0));

    [Test]
    public async Task AFormOpensAtTheBoundsItIsGiven()
    {
        var bounds = OnThisDisplay();
        using var form = new ViewerForm("title", 800, 600, new(bounds.X, bounds.Y, bounds.Width, bounds.Height, false));
        _ = form.Handle;

        await Assert.That(form.Bounds).IsEqualTo(bounds);
        await Assert.That(form.WindowState).IsEqualTo(FormWindowState.Normal);
        await Assert.That(form.Placement).IsEqualTo(new WindowPlacement(bounds.X, bounds.Y, bounds.Width, bounds.Height, false));
    }

    /// <summary>
    /// The run that was reported: maximise, close, open. And then restore, which has to land on
    /// the bounds the window had before it was maximised rather than on a default.
    /// </summary>
    [Test]
    public async Task AFormLeftMaximisedOpensMaximisedAndRestoresToWhereItWas()
    {
        var bounds = OnThisDisplay();
        WindowPlacement? left;
        using (var first = new ViewerForm("title", 800, 600, new(bounds.X, bounds.Y, bounds.Width, bounds.Height, false)))
        {
            first.Show();
            first.WindowState = FormWindowState.Maximized;
            left = first.Placement;
        }

        await Assert.That(left).IsEqualTo(new WindowPlacement(bounds.X, bounds.Y, bounds.Width, bounds.Height, true));

        using var second = new ViewerForm("title", 800, 600, left);
        second.Show();

        await Assert.That(second.WindowState).IsEqualTo(FormWindowState.Maximized);
        await Assert.That(second.Placement).IsEqualTo(left);

        second.WindowState = FormWindowState.Normal;

        await Assert.That(second.Bounds).IsEqualTo(bounds);
    }

    /// <summary>
    /// A minimised window says only that it is minimised. It comes back as whichever it was, so
    /// that is what is remembered.
    /// </summary>
    [Test]
    public async Task AFormMinimisedFromMaximisedIsRememberedAsMaximised()
    {
        var bounds = OnThisDisplay();
        using var form = new ViewerForm("title", 800, 600, new(bounds.X, bounds.Y, bounds.Width, bounds.Height, false));
        form.Show();
        form.WindowState = FormWindowState.Maximized;

        form.WindowState = FormWindowState.Minimized;

        await Assert.That(form.Placement).IsEqualTo(new WindowPlacement(bounds.X, bounds.Y, bounds.Width, bounds.Height, true));
    }

    /// <summary>
    /// The loop asks once more after a session has ended, by which time the form is gone. What it
    /// last was is still the answer.
    /// </summary>
    [Test]
    public async Task ADisposedFormStillSaysWhereItWas()
    {
        var bounds = OnThisDisplay();
        var form = new ViewerForm("title", 800, 600, new(bounds.X, bounds.Y, bounds.Width, bounds.Height, false));
        _ = form.Handle;
        var before = form.Placement;

        form.Dispose();

        await Assert.That(before).IsNotNull();
        await Assert.That(form.Placement).IsEqualTo(before);
    }

    [Test]
    public async Task AFormWithNothingRememberedOpensAsItAlwaysDid()
    {
        using var form = new ViewerForm("title", 800, 600);

        await Assert.That(form.StartPosition).IsEqualTo(FormStartPosition.CenterScreen);
        await Assert.That(form.WindowState).IsEqualTo(FormWindowState.Normal);
    }

    /// <summary>
    /// Bounds that are on whatever display the tests are running on, since a form really opens.
    /// </summary>
    static Rectangle OnThisDisplay()
    {
        var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        return new(area.Left + 60, area.Top + 40, Math.Min(700, area.Width - 120), Math.Min(500, area.Height - 80));
    }
}
