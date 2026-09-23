static class Program
{
    /// <summary>
    /// STA because WinForms requires it, and the whole app runs on this thread: ViewerProgram owns
    /// the loop and the socket listener marshals window changes back through a queue it drains.
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        ViewerApp.Configure();
        if (CommandLine.Parse(args).Error is not null)
        {
            AttachParentConsole();
        }

        return ViewerProgram.Run(args, FormsViewerWindow.Open);
    }

    /// <summary>
    /// A GUI executable has no console, so arguments typed wrong at a terminal would print their
    /// usage nowhere. Only for that: attached to for the whole run, closing the terminal a test
    /// run was started from would take the viewer down with it.
    /// <para>
    /// A GUI executable because DiffEngine starts it with ShellExecute, so that it inherits none of
    /// the test host's handles, and ShellExecute gives a console executable a console window of its
    /// own. Does nothing where the parent has no console, and leaves an output that was redirected
    /// where it was pointed.
    /// </para>
    /// </summary>
    static void AttachParentConsole() =>
        AttachConsole(attachParentProcess);

    const int attachParentProcess = -1;

    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);
}
