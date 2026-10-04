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

    /// <summary>
    /// And found out by throwing, which the test above cannot do: it reads an internal by name,
    /// and says what WinForms intends rather than what a pump does.
    /// <para>
    /// Thrown from a bare <see cref="NativeWindow" /> rather than from a control. Every window
    /// WinForms makes has the one callback, which catches what its WndProc throws and either
    /// throws it on, in the mode the module initializer asks for, or hands it to the window's
    /// OnThreadException. It is a control's that goes on to the dialog. A bare window's does
    /// nothing at all, so were the mode not the one asked for, the exception would be swallowed
    /// and this would fail having shown nothing. In the mode asked for it comes out of the
    /// DoEvents that dispatched the message, which is what a failing test relies on.
    /// </para>
    /// <para>
    /// And a second net under that one, since being wrong is somebody's desktop: the dialog is
    /// WinForms' answer only when nothing has asked for the exception, so the thread that throws
    /// asks for it first, with a handler on <see cref="Application.ThreadException" />.
    /// </para>
    /// <para>
    /// On a thread of its own, since that handler is a thread's and a test's continuation need
    /// not be on the thread it started on, and to a window that is only ever a place to send
    /// messages. Everything is caught there: an exception leaving a thread ends the process.
    /// </para>
    /// </summary>
    [Test]
    public async Task AnExceptionInAWindowMessageComesOutOfThePump()
    {
        Exception? cameOut = null;
        Exception? handled = null;
        Exception? unexpected = null;
        var thread = new Thread(
            () =>
            {
                ThreadExceptionEventHandler net = (_, args) => handled = args.Exception;
                try
                {
                    // Before anything can throw
                    Application.ThreadException += net;
                    var window = new Thrower();
                    window.CreateHandle(
                        new()
                        {
                            Parent = messageOnly
                        });
                    try
                    {
                        PostMessage(window.Handle, Thrower.Throw, IntPtr.Zero, IntPtr.Zero);
                        try
                        {
                            Application.DoEvents();
                        }
                        catch (Exception exception)
                        {
                            cameOut = exception;
                        }
                    }
                    finally
                    {
                        window.DestroyHandle();
                    }
                }
                catch (Exception exception)
                {
                    unexpected = exception;
                }
                finally
                {
                    Application.ThreadException -= net;
                }
            })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        // Nothing here waits on anything but a posted message. Long, since all it bounds is a
        // run that would otherwise never end
        var finished = await Task.Run(() => thread.Join(TimeSpan.FromMinutes(1)));

        await Assert.That(finished).IsTrue();
        await Assert.That(unexpected).IsNull();
        await Assert.That(handled).IsNull();
        await Assert.That(cameOut?.Message).IsEqualTo(Thrower.Said);
    }

    /// <summary>
    /// A window with no place on any screen: a child of HWND_MESSAGE is never shown, listed or
    /// drawn.
    /// </summary>
    static readonly IntPtr messageOnly = new(-3);

    sealed class Thrower : NativeWindow
    {
        /// <summary>
        /// The first of the messages Windows leaves to an application.
        /// </summary>
        public const int Throw = 0x8000;

        public const string Said = "Thrown inside a window message.";

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == Throw)
            {
                throw new InvalidOperationException(Said);
            }

            base.WndProc(ref message);
        }
    }

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
