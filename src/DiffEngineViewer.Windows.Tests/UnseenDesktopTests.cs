/// <summary>
/// What the tests that show a window the ordinary way rest on: the desktop they show it on is
/// not the one somebody is working at.
/// </summary>
[NotInParallel]
public class UnseenDesktopTests
{
    /// <summary>
    /// A window shown as any window is, activated, maximised and asked for the foreground, which
    /// is everything that takes a keyboard. Skipped where no desktop could be made, since there it
    /// would be showing exactly that window to whoever is at the machine.
    /// </summary>
    [Test]
    public async Task AWindowShownThereNeverHasTheKeyboard()
    {
        var (entered, shown, hadTheKeyboard) = UnseenDesktop.Run(() =>
        {
            if (!UnseenDesktop.Entered)
            {
                return (false, false, false);
            }

            using var form = new Form
            {
                ShowInTaskbar = false
            };
            form.Show();
            var afterShow = UnseenDesktop.HasTheKeyboard(form);
            form.WindowState = FormWindowState.Maximized;
            var afterMaximise = UnseenDesktop.HasTheKeyboard(form);
            form.Activate();
            return (true, form.Visible, afterShow || afterMaximise || UnseenDesktop.HasTheKeyboard(form));
        });
        Skip.Unless(entered, "This window station does not allow a desktop to be made");

        await Assert.That(shown).IsTrue();
        await Assert.That(hadTheKeyboard).IsFalse();
    }

    /// <summary>
    /// A test that fails on the desktop's thread fails where it was written, with what it threw.
    /// </summary>
    [Test]
    public async Task WhatATestThrowsThereComesOutHere() =>
        await Assert.That(() => UnseenDesktop.Run(() => throw new InvalidOperationException("thrown there")))
            .Throws<InvalidOperationException>()
            .WithMessage("thrown there");
}
