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

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Exe = Path.GetFullPath(Path.Combine(AssemblyLocation.CurrentDirectory, "../../../../FakeDiffTool/bin/osx-x64/FakeDiffTool"));
            return;
        }

        throw new();
    }
}