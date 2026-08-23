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

    [Test]
    public async Task Shows_a_hidden_window()
    {
        using var form = new ViewerForm("title", 800, 600);
        form.Visible = false;

        form.Raise();

        await Assert.That(form.Visible).IsTrue();
    }
}
