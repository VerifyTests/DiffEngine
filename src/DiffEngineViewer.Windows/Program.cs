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
        return ViewerProgram.Run(args, FormsViewerWindow.Open);
    }
}
