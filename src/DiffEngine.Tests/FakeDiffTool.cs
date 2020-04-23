using System.IO;

public class FakeDiffTool
{
    public static string Exe;

    static FakeDiffTool()
    {
        Exe = Path.GetFullPath(Path.Combine(AssemblyLocation.CurrentDirectory, "../../../../FakeDiffTool/bin/FakeDiffTool.exe"));
    }
}