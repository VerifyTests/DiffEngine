public class FakeDiffTool
{
    public static string Exe;

    static FakeDiffTool()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Exe = Path.GetFullPath(Path.Combine(AssemblyLocation.CurrentDirectory, "../../../../FakeDiffTool/bin/win-x64/FakeDiffTool.exe"));
            return;
        }

        throw new($"FakeDiffTool is a WinForms app and only runs on Windows. OS: {RuntimeInformation.OSDescription}");
    }
}
