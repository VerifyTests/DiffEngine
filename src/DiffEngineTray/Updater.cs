static class Updater
{
    public static void Run()
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