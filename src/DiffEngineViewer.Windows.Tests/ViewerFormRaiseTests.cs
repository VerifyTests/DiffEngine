/// <summary>
/// Bringing the window up for a snapshot that has just arrived. The queue owner asks for this over
/// the socket, and it is the only thing that puts a new snapshot in front of anyone.
/// <para>
/// Which is to say in front of whoever is at the machine, with their keyboard: a raise that did
/// not do that would not be one. So every window here is raised on <see cref="UnseenDesktop" />,
/// where it is raised for real and nobody is there.
/// </para>
/// </summary>
[NotInParallel]
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
        var state = UnseenDesktop.Run(() =>
        {
            using var form = new ViewerForm("title", 800, 600);
            form.WindowState = FormWindowState.Minimized;

            form.Raise();

            return form.WindowState;
        });

        await Assert.That(state).IsEqualTo(FormWindowState.Normal);
    }

    /// <summary>
    /// A window the reader had maximised stays maximised: it is already as visible as it gets, and
    /// restoring it would be undoing something they chose.
    /// </summary>
    [Test]
    public async Task Leaves_a_maximised_window_maximised()
    {
        var state = UnseenDesktop.Run(() =>
        {
            using var form = new ViewerForm("title", 800, 600);
            form.WindowState = FormWindowState.Maximized;

            form.Raise();

            return form.WindowState;
        });

        await Assert.That(state).IsEqualTo(FormWindowState.Maximized);
    }

    /// <summary>
    /// Minimised from maximised, it comes back maximised, as it would from the taskbar. Put back to
    /// normal instead, it was also remembered that way: the next hide or close saved a window the
    /// reader had maximised as one they had not.
    /// </summary>
    [Test]
    public async Task Restores_a_window_minimised_from_maximised_as_maximised()
    {
        var (state, placement) = UnseenDesktop.Run(() =>
        {
            // Shown, since what it was minimised from is learnt from the window as it resizes,
            // and transparent, so that on a machine with only the one desktop a maximised window
            // does not flash over whoever is running the tests
            using var form = new ViewerForm("title", 800, 600)
            {
                Opacity = 0,
                ShowInTaskbar = false
            };
            form.Show();
            form.WindowState = FormWindowState.Maximized;
            form.WindowState = FormWindowState.Minimized;

            form.Raise();

            return (form.WindowState, form.Placement);
        });

        await Assert.That(state).IsEqualTo(FormWindowState.Maximized);
        await Assert.That(placement!.Value.Maximized).IsTrue();
    }

    [Test]
    public async Task Shows_a_hidden_window()
    {
        var visible = UnseenDesktop.Run(() =>
        {
            using var form = new ViewerForm("title", 800, 600);
            form.Visible = false;

            form.Raise();

            return form.Visible;
        });

        await Assert.That(visible).IsTrue();
    }
}
