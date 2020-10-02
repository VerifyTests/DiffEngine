using System;
using System.Diagnostics;
using System.Text;
using System.Windows.Forms;
using DiffEngineTray.Common;

class WindowsAppUpdater : IUpdater
{
    public void Run()
    {
        var psCommandBytes = Encoding.Unicode.GetBytes("dotnet tool update diffenginetray --global; diffenginetray");
        var psCommandBase64 = Convert.ToBase64String(psCommandBytes);
        var info = new ProcessStartInfo(
            "powershell.exe",
            $"-NoProfile -ExecutionPolicy unrestricted -EncodedCommand {psCommandBase64}")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        Process.Start(info);
        Application.Exit();
    }
}