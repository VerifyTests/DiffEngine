/// <summary>
/// What an exception thrown inside a window message does in this test host. WinForms' own answer is
/// a dialog offering Continue and Quit, and a run then waits on the desktop of whoever started it
/// for a click. <see cref="ModuleInitializer" /> asks for it to be thrown instead.
/// </summary>
[NotInParallel]
[TUnit.Core.Executors.STAThreadExecutor]
public class UnhandledExceptionTests
{
    /// <summary>
    /// Asked of WinForms rather than found out by throwing, because being wrong about it is the
    /// dialog. On the thread a test runs on, which is not the one the module initializer ran on:
    /// set for that thread alone, the mode left every test thread with the dialog.
    /// </summary>
    [Test]
    public async Task AWindowOnATestThreadLetsAnExceptionThrough()
    {
        var debuggable = typeof(NativeWindow).GetProperty(
            "WndProcShouldBeDebuggable",
            BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic);
        // WinForms' own name for it. If that goes, this has to find the answer some other way
        // that does not involve throwing.
        await Assert.That(debuggable).IsNotNull();

        var window = new NativeWindow();
        var lets = (bool) debuggable!.GetValue(debuggable.GetMethod!.IsStatic ? null : window)!;

        await Assert.That(lets).IsTrue();
    }
}
