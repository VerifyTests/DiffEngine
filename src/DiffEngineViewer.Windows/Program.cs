static class Program
{
    /// <summary>
    /// STA because WinForms requires it, and the whole app runs on this thread: ViewerProgram owns
    /// the loop and the socket listener marshals window changes back through a queue it drains.
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // The reason this head exists rather than a sixth copy of the shim: the native renderer
        // has no DPI handling at all and converts pixels to cells by dividing by a constant.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        return ViewerProgram.Run(args, FormsViewerWindow.Open);
    }
}
