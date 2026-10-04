using System.Runtime.ExceptionServices;

/// <summary>
/// A desktop of this process's own, for a test whose subject is what <see cref="ParkedForm" />
/// takes away: a window that is activated, maximised, minimised and brought back. Each of those
/// is asked of Windows by a call that also makes the window the one the keyboard goes to, shown
/// without activation or not, and a maximised window is on a display wherever it was put. So on
/// the desktop somebody is working at, such a test took their keyboard and covered their screen.
/// <para>
/// A window belongs to the desktop of the thread that made it, and only the desktop that has the
/// display takes input or is drawn. On another one a window is still a window: it is shown,
/// activated and sized by the same calls, against the same displays, and says the same of itself
/// afterwards. Nobody sees it, and nothing typed reaches it.
/// </para>
/// </summary>
static class UnseenDesktop
{
    /// <summary>
    /// Made the first time it is asked for and kept for the life of the process, which is what
    /// closes it. Zero where one could not be made.
    /// </summary>
    static readonly Lazy<IntPtr> desktop = new(Create);

    /// <summary>
    /// Runs <paramref name="test"/> on a thread of its own that is on the desktop, and waits for
    /// it. Every window the test is about has to be made, used and disposed inside it.
    /// <para>
    /// A thread of its own because one that has a window cannot be moved, and the thread a test
    /// starts on has one before the test's first line: it is a single threaded apartment, which
    /// COM gives a window. This one is in no apartment of its own, which nothing these windows do
    /// asks for: no clipboard and no drag and drop.
    /// </para>
    /// <para>
    /// Where no desktop could be made, which is a window station that does not allow one, the
    /// test runs on the desktop it always did. A machine like that is not one with somebody
    /// typing at it.
    /// </para>
    /// </summary>
    public static void Run(Action test)
    {
        ExceptionDispatchInfo? thrown = null;
        var thread = new Thread(() =>
        {
            try
            {
                Enter();
                test();
            }
            catch (Exception exception)
            {
                thrown = ExceptionDispatchInfo.Capture(exception);
            }
        });
        thread.Start();
        thread.Join();
        thrown?.Throw();
    }

    /// <summary>
    /// As <see cref="Run(Action)"/>, for a test that asserts on what the window came to.
    /// </summary>
    public static T Run<T>(Func<T> test)
    {
        T result = default!;
        Run(() =>
        {
            result = test();
        });
        return result;
    }

    /// <summary>
    /// Whether the calling thread is on the desktop, which is every thread <see cref="Run(Action)"/>
    /// starts on a machine that could make one.
    /// </summary>
    public static bool Entered =>
        desktop.Value != IntPtr.Zero &&
        GetThreadDesktop(GetCurrentThreadId()) == desktop.Value;

    /// <summary>
    /// Whether <paramref name="form"/> is the window the keyboard goes to at the machine.
    /// </summary>
    public static bool HasTheKeyboard(Form form) =>
        GetForegroundWindow() == form.Handle;

    static void Enter()
    {
        if (desktop.Value == IntPtr.Zero)
        {
            return;
        }

        // Not something to carry on from: the desktop is there and this thread is not on it, so
        // what the test shows next is shown to whoever is at the machine
        if (!SetThreadDesktop(desktop.Value))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "The thread could not be moved to the tests' own desktop.");
        }
    }

    // Named for the process, since a desktop is found by its name and two runs at once would
    // otherwise share one
    static IntPtr Create() =>
        CreateDesktop($"DiffEngineViewer.Windows.Tests.{Environment.ProcessId}", null, IntPtr.Zero, 0, genericAll, IntPtr.Zero);

    const uint genericAll = 0x10000000;

    [DllImport("user32.dll", EntryPoint = "CreateDesktopW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateDesktop(string name, string? device, IntPtr deviceMode, uint flags, uint access, IntPtr security);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetThreadDesktop(IntPtr desktop);

    [DllImport("user32.dll")]
    static extern IntPtr GetThreadDesktop(uint thread);

    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();
}
