/// <summary>
/// Bringing the window up for a snapshot that has just arrived. The queue owner asks for this over
/// the socket, and it is the only thing that puts a new snapshot in front of anyone.
/// </summary>
[NotInParallel]
[TUnit.Core.Executors.STAThreadExecutor]
public class ViewerFormRaiseTests
{
    /// <summary>
    /// BringToFront and Activate leave a minimised window minimised: the taskbar button flashes
    /// and nothing else happens. So a viewer that had been minimised was never actually shown the
    /// snapshot, and the queue filled up out of sight.
    /// </summary>
    [Test]
    public async Task Restores_a_minimised_window()
    {
        using var form = new ViewerForm("title", 800, 600);
        form.WindowState = FormWindowState.Minimized;

        form.Raise();

        await Assert.That(form.WindowState).IsEqualTo(FormWindowState.Normal);
    }

    /// <summary>
    /// A window the reader had maximised stays maximised: it is already as visible as it gets, and
    /// restoring it would be undoing something they chose.
    /// </summary>
    [Test]
    public async Task Leaves_a_maximised_window_maximised()
    {
        using var form = new ViewerForm("title", 800, 600);
        form.WindowState = FormWindowState.Maximized;

        form.Raise();

        await Assert.That(form.WindowState).IsEqualTo(FormWindowState.Maximized);
    }

    /// <summary>
    /// Minimised from maximised, it comes back maximised, as it would from the taskbar. Put back to
    /// normal instead, it was also remembered that way: the next hide or close saved a window the
    /// reader had maximised as one they had not.
    /// </summary>
    [Test]
    public async Task Restores_a_window_minimised_from_maximised_as_maximised()
    {
        // Shown, since what it was minimised from is learnt from the window as it resizes, and
        // transparent, so a maximised window does not flash over whoever is running the tests
        using var form = new ViewerForm("title", 800, 600)
        {
            Opacity = 0,
            ShowInTaskbar = false
        };
        form.Show();
        form.WindowState = FormWindowState.Maximized;
        form.WindowState = FormWindowState.Minimized;

        form.Raise();

        await Assert.That(form.WindowState).IsEqualTo(FormWindowState.Maximized);
        await Assert.That(form.Placement!.Value.Maximized).IsTrue();
    }

    [Test]
    public async Task Shows_a_hidden_window()
    {
        using var form = new ViewerForm("title", 800, 600);
        form.Visible = false;

        form.Raise();

        await Assert.That(form.Visible).IsTrue();
    }
}
