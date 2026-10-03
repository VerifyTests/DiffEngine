using System;
using System.Threading;
using System.Windows.Forms;

class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Copied to DiffEngineViewer.exe, this plays a viewer from before 20.5.0, to which
        // --payload is an argument it does not know: that one says so and exits with 2
        if (Array.IndexOf(args, "--payload") >= 0)
        {
            return 2;
        }

        // If --windowed is passed, create a simple form that can be closed gracefully
        if (args.Length > 0 && args[0] == "--windowed")
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form
            {
                Text = "FakeDiffTool",
                WindowState = FormWindowState.Minimized,
                ShowInTaskbar = false
            });
        }
        else
        {
            // Default behavior: just sleep (no main window)
            Thread.Sleep(5000);
        }

        return 0;
    }
}