public class FakeDiffTool
{
    public static string ExePath;
    public static string ExeName = null!;

    static FakeDiffTool()
    {
        var directory = string.Empty;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ExeName = "FakeDiffTool.exe";
            directory = "../../../../FakeDiffTool/bin/win-x64/FakeDiffTool.exe";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            ExeName = "FakeDiffTool";
            directory = "../../../../FakeDiffTool/bin/osx-x64";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            ExeName = "FakeDiffTool";
            directory = "../../../../FakeDiffTool/bin/linux-x64";
        }

        ExePath = Path.GetFullPath(Path.Combine(AssemblyLocation.CurrentDirectory, directory, ExeName));
    }
}