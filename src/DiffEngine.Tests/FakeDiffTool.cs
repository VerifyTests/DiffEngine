public class FakeDiffTool
{
    public static string ExePath;
    public static string ExeName;

    static FakeDiffTool()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ExeName = "FakeDiffTool.exe";
            ExePath = Path.GetFullPath(Path.Combine(AssemblyLocation.CurrentDirectory, "../../../../FakeDiffTool/bin/win-x64/FakeDiffTool.exe"));
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            ExeName = "FakeDiffTool";
            ExePath = Path.GetFullPath(Path.Combine(AssemblyLocation.CurrentDirectory, "../../../../FakeDiffTool/bin/osx-x64/FakeDiffTool"));
            return;
        }

        throw new();
    }
}