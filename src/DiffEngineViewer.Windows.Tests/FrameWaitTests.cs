/// <summary>
/// How long the loop waits between frames: a sixtieth of a second for a window somebody can see,
/// a tenth for one nobody can.
/// </summary>
[NotInParallel]
[TUnit.Core.Executors.STAThreadExecutor]
public class FrameWaitTests
{
    [Test]
    [Arguments(true, FormWindowState.Normal, 15)]
    [Arguments(true, FormWindowState.Maximized, 15)]
    [Arguments(true, FormWindowState.Minimized, 100)]
    [Arguments(false, FormWindowState.Normal, 100)]
    [Arguments(false, FormWindowState.Minimized, 100)]
    public async Task Waits(bool visible, FormWindowState state, int milliseconds) =>
        await Assert.That(FormsViewerWindow.FrameWait(visible, state)).IsEqualTo(milliseconds);

    /// <summary>
    /// Why the state is asked as well: a minimised window is still Visible, which was the whole of
    /// the test, so it was taken for one on screen.
    /// </summary>
    [Test]
    public async Task AMinimisedWindowIsStillVisible()
    {
        // Shown, since a window never shown is not Visible whatever its state. Minimising is asked
        // of Windows by a call that hands the keyboard on, so on a desktop nobody is at, and
        // parked and transparent besides for a machine that has only the one
        var (visible, state) = UnseenDesktop.Run(() =>
        {
            using var form = new ViewerForm("title", 800, 600)
            {
                Opacity = 0,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new(-4000, -2000),
                Parked = true
            };
            form.Show();
            form.WindowState = FormWindowState.Minimized;
            return (form.Visible, form.WindowState);
        });

        await Assert.That(visible).IsTrue();
        await Assert.That(FormsViewerWindow.FrameWait(visible, state)).IsEqualTo(100);
    }
}
