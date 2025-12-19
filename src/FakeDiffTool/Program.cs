using System;
using System.Threading;
using System.Windows.Forms;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
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
    }
}