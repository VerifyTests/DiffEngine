/// <summary>
/// Who gets to refuse a close.
/// <para>
/// The form cancels a user close because whether closing means hide or exit is ViewerProgram's
/// rule, not the form's. It used to cancel every close, including the one Windows sends when the
/// session is ending — and WinForms answers WM_QUERYENDSESSION with !e.Cancel, so the viewer
/// reported itself as preventing shutdown.
/// </para>
/// <para>
/// Driven through OnFormClosing by reflection, because CloseReason is set by the message that
/// started the close and there is no way to ask a form to close as though Windows had.
/// </para>
/// </summary>
[NotInParallel]
[TUnit.Core.Executors.STAThreadExecutor]
public class ViewerFormClosingTests
{
    [Test]
    [Arguments(CloseReason.UserClosing, true)]
    [Arguments(CloseReason.None, true)]
    [Arguments(CloseReason.WindowsShutDown, false)]
    [Arguments(CloseReason.TaskManagerClosing, false)]
    public async Task Cancels(CloseReason reason, bool expected)
    {
        using var form = new ViewerForm("title", 800, 600);
        var args = new FormClosingEventArgs(reason, false);

        Raise(form, args);

        await Assert.That(args.Cancel).IsEqualTo(expected);
    }

    /// <summary>
    /// And CloseForReal still wins, whatever the reason, since that is the loop answering its own
    /// question.
    /// </summary>
    [Test]
    public async Task CloseForRealIsNeverCancelled()
    {
        using var form = new ViewerForm("title", 800, 600);
        form.CloseForReal();
        var args = new FormClosingEventArgs(CloseReason.UserClosing, false);

        Raise(form, args);

        await Assert.That(args.Cancel).IsFalse();
    }

    static void Raise(ViewerForm form, FormClosingEventArgs args) =>
        typeof(ViewerForm)
            .GetMethod("OnFormClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, [args]);
}
